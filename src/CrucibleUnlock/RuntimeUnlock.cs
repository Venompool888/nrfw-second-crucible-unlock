using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Read overrides only. Does not obtain a Frame, modify quest data, or save.
    public static class RuntimeUnlock
    {
        public const long TargetStepGuid = 346285554720904463L;
        public const string OwnerId = "NRFW.CrucibleUnlock.RuntimeUnlock";
        private static PatchOwner _harmony;
        private static Action<string> _log;
        private static Action<string> _error;
        private static volatile bool _enabled;
        private static long _questHits, _conditionHits, _invertedHits;
        private static long _reportedQuest, _reportedCondition, _reportedInverted;
        private static long _lastFlush;
        private static int _prefixErrorRecorded;
        private static string _pendingError;

        // Call once from the loader thread after interop and the build guard succeed.
        public static void Install(Action<string> log, Action<string> error)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (error == null) throw new ArgumentNullException(nameof(error));
            if (_harmony != null) throw new InvalidOperationException("Runtime unlock is already installed.");
            var quest = typeof(QuestAPI).GetMethod("IsQuestStepComplete",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Frame), typeof(AssetRefGameQuestStepData) }, null);
            var condition = typeof(QuantumConditionQuestStepState).GetMethod("ConditionPassedInternal",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(Frame), typeof(EntityRef) }, null);
            RequireBooleanMethod(quest, true);
            RequireBooleanMethod(condition, false);
            if (HasOwnPatch(quest) || HasOwnPatch(condition))
                throw new InvalidOperationException("An existing runtime-unlock owner is already registered.");

            _log = log;
            _error = error;
            _enabled = false;
            _questHits = _conditionHits = _invertedHits = 0;
            _reportedQuest = _reportedCondition = _reportedInverted = 0;
            _prefixErrorRecorded = 0;
            _pendingError = null;
            _lastFlush = Stopwatch.GetTimestamp();
            var harmony = new PatchOwner(OwnerId);
            try
            {
                harmony.Patch(quest, prefix: PrefixMethod(nameof(QuestPrefix)));
                harmony.Patch(condition, prefix: PrefixMethod(nameof(ConditionPrefix)));
                if (!HasOwnPatch(quest) || !HasOwnPatch(condition))
                    throw new InvalidOperationException("Harmony did not register both runtime-unlock patches.");
                _harmony = harmony;
                _enabled = true;
                SafeInvoke(_log, "[runtime-unlock] installed two targeted read overrides; step=" + TargetStepGuid);
            }
            catch (Exception installError)
            {
                _enabled = false;
                try { harmony.UnpatchSelf(); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Runtime-unlock installation and rollback failed.", installError, rollbackError);
                }
                throw;
            }
        }

        private static void RequireBooleanMethod(MethodInfo method, bool isStatic)
        {
            if (method == null || method.ReturnType != typeof(bool) || method.IsStatic != isStatic || method.ContainsGenericParameters)
                throw new MissingMethodException("The generated target signature does not match the verified Boolean method.");
        }

        private static bool HasOwnPatch(MethodBase method)
        {
            var patches = PatchOwner.GetPatchInfo(method);
            if (patches == null) return false;
            foreach (var owner in patches.Owners) if (owner == OwnerId) return true;
            return false;
        }

        private static HarmonyMethod PrefixMethod(string name)
        {
            return new HarmonyMethod(typeof(RuntimeUnlock).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
        }

        // __1 selects the second original argument, avoiding generated parameter-name assumptions.
        private static bool QuestPrefix(AssetRefGameQuestStepData __1, ref bool __result)
        {
            if (!_enabled) return true;
            try
            {
                if (__1.Id.Value != TargetStepGuid) return true;
                __result = true;
                Interlocked.Increment(ref _questHits);
                return false;
            }
            catch (Exception error) { RecordPrefixError(error); return true; }
        }

        private static bool ConditionPrefix(QuantumConditionQuestStepState __instance, ref bool __result)
        {
            if (!_enabled) return true;
            try
            {
                if (__instance == null || __instance.Step.Id.Value != TargetStepGuid) return true;
                bool invert = __instance.Invert;
                bool completedState = (int)__instance.State == 2;
                __result = completedState != invert;
                Interlocked.Increment(ref _conditionHits);
                if (invert) Interlocked.Increment(ref _invertedHits);
                return false;
            }
            catch (Exception error) { RecordPrefixError(error); return true; }
        }

        private static void RecordPrefixError(Exception error)
        {
            // No loader logging from the simulation callback; report at the next Flush.
            if (Interlocked.CompareExchange(ref _prefixErrorRecorded, 1, 0) == 0)
                Interlocked.Exchange(ref _pendingError, "[runtime-unlock] prefix read failed; original runs: " + error.GetType().Name + ": " + error.Message);
        }

        // Call from ModMain.OnUpdate. No game state or Frame is read here.
        public static void Flush()
        {
            long now = Stopwatch.GetTimestamp();
            if (now - _lastFlush < 2L * Stopwatch.Frequency) return;
            _lastFlush = now;
            FlushValues();
        }

        private static void FlushValues()
        {
            string error = Interlocked.Exchange(ref _pendingError, null);
            if (error != null) SafeInvoke(_error, error);
            long quest = Interlocked.Read(ref _questHits);
            long condition = Interlocked.Read(ref _conditionHits);
            long inverted = Interlocked.Read(ref _invertedHits);
            if (quest == _reportedQuest && condition == _reportedCondition && inverted == _reportedInverted) return;
            _reportedQuest = quest;
            _reportedCondition = condition;
            _reportedInverted = inverted;
            SafeInvoke(_log, "[runtime-unlock] matched reads: quest=" + quest + " condition=" + condition + " inverted=" + inverted);
        }

        // Call once while the IL2CPP runtime is alive. Exceptions allow the caller to report rollback failure.
        public static void Dispose()
        {
            _enabled = false;
            FlushValues();
            var harmony = _harmony;
            if (harmony == null) return;
            harmony.UnpatchSelf();
            _harmony = null;
            SafeInvoke(_log, "[runtime-unlock] own patches removed");
        }

        private static void SafeInvoke(Action<string> callback, string message)
        {
            try { callback?.Invoke(message); }
            catch { /* A logging failure must not propagate into a game callback. */ }
        }
    }
}
