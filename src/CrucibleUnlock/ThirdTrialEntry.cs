using System;
using System.Threading;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Targeted read overrides and a source selection remap. Never writes a quest/save,
    // creates a Frame, or reads Unity objects from the simulation thread.
    internal static class ThirdTrialEntry
    {
        internal const long TargetStepGuid = 3364572276970703445L;
        internal const long OriginalPlaylistGuid = 5973112006018325L;
        internal const long PrivatePlaylistGuid = -611686018427287904L;
        internal const string OwnerId = "NRFW.CrucibleUnlock.ThirdTrialEntry";
        private static PatchOwner _harmony;
        private static volatile bool _enabled;
        private static volatile bool _installed;
        private static Func<Frame, InfiniteDungeonPlaylist> _resolveReadyPlaylist;
        private static Func<bool> _currentViewsReady;
        private static Action<string> _log, _error;
        private static string _pendingError;
        private static int _errorRecorded;
        internal static bool Enabled => _enabled;

        // Pure managed readiness provided by the loader. ResolveReadyPlaylist additionally
        // checks all ten profiles, storage and private assets in the caller's exact Frame.
        internal static bool CurrentViewsReady
        {
            get
            {
                if (!_enabled) return false;
                try { return _currentViewsReady(); }
                catch (Exception error) { RecordError(error); return false; }
            }
        }

        internal static void Install(Func<Frame, InfiniteDungeonPlaylist> resolveReadyPlaylist,
            Func<bool> currentViewsReady, Action<string> log, Action<string> error)
        {
            if (resolveReadyPlaylist == null) throw new ArgumentNullException(nameof(resolveReadyPlaylist));
            if (currentViewsReady == null) throw new ArgumentNullException(nameof(currentViewsReady));
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (error == null) throw new ArgumentNullException(nameof(error));
            if (_harmony != null) throw new InvalidOperationException("Third entry is already installed.");
            _resolveReadyPlaylist = resolveReadyPlaylist; _currentViewsReady = currentViewsReady;
            _log = log; _error = error; _pendingError = null; _errorRecorded = 0; _enabled = false;
            var harmony = new PatchOwner(OwnerId);
            try
            {
                TrialRepairLog.Patch(harmony, typeof(QuestAPI), "IsQuestStepComplete",
                    new[] { typeof(Frame), typeof(AssetRefGameQuestStepData) }, typeof(ThirdTrialEntry),
                    prefix: nameof(QuestPrefix), returnType: typeof(bool));
                TrialRepairLog.Patch(harmony, typeof(QuantumConditionQuestStepState), "ConditionPassedInternal",
                    new[] { typeof(Frame), typeof(EntityRef) }, typeof(ThirdTrialEntry),
                    prefix: nameof(ConditionPrefix), returnType: typeof(bool));
                TrialRepairLog.Patch(harmony, typeof(InfiniteDungeonDirector), "SelectNextPlaylist",
                    new[] { typeof(Frame) }, typeof(ThirdTrialEntry),
                    postfix: nameof(AfterSelectNextPlaylist), returnType: typeof(InfiniteDungeonPlaylist));
                _harmony = harmony; _installed = true; _enabled = true;
                SafeInvoke(_log, "[third-entry] installed targeted third-step reads and source remap; ready Frame assets + all current views required; source=" + PrivatePlaylistGuid);
            }
            catch (Exception installError)
            {
                _enabled = false; _installed = false;
                try { harmony.UnpatchSelf(); }
                catch (Exception rollbackError) { throw new AggregateException(installError, rollbackError); }
                throw;
            }
        }

        private static InfiniteDungeonPlaylist ResolveReady(Frame frame)
        {
            if (!_enabled || frame == null || !CurrentViewsReady) return null;
            var playlist = _resolveReadyPlaylist(frame);
            if (playlist == null) return null;
            if (playlist.Guid.Value != PrivatePlaylistGuid || (int)playlist.PlaylistType != 1)
                throw new InvalidOperationException("Third entry readiness returned a foreign source or non-Roguelite playlist.");
            return playlist;
        }

        private static bool QuestPrefix(Frame __0, AssetRefGameQuestStepData __1, ref bool __result)
        {
            if (!_installed || __1.Id.Value != TargetStepGuid) return true;
            try
            {
                __result = ResolveReady(__0) != null;
                return false;
            }
            catch (Exception error) { RecordError(error); __result = false; return false; }
        }

        private static bool ConditionPrefix(QuantumConditionQuestStepState __instance, Frame __0, ref bool __result)
        {
            if (!_installed || __instance == null) return true;
            bool matched = false;
            bool invert = false;
            try
            {
                if (__instance.Step.Id.Value != TargetStepGuid || (int)__instance.State != 2) return true;
                invert = __instance.Invert;
                matched = true;
                __result = (ResolveReady(__0) != null) != invert;
                return false;
            }
            catch (Exception error)
            {
                RecordError(error);
                if (!matched) return true;
                // Inverted completion conditions remain true when unavailable.
                __result = invert; return false;
            }
        }

        private static void AfterSelectNextPlaylist(Frame __0, ref InfiniteDungeonPlaylist __result)
        {
            if (!_installed || __0 == null || __result == null) return;
            bool matched = false;
            try
            {
                // Both predicted and verified generation consume the same source. Respect
                // any other mod that already selected a different source for this key.
                if (__result.Guid.Value != OriginalPlaylistGuid ||
                    InfiniteDungeonAPI.GetSelectedPlaylistKey(__0).Id.Value != TargetStepGuid) return;
                matched = true;
                var replacement = ResolveReady(__0);
                // Native UpdateGenerating explicitly tests a null selection before
                // FinishGenerating and guards its later Guid read. Block the stripped
                // Horde source even on saves that already completed the third step.
                __result = replacement;
            }
            catch (Exception error) { RecordError(error); if (matched) __result = null; }
        }

        private static void RecordError(Exception error)
        {
            _enabled = false;
            if (Interlocked.CompareExchange(ref _errorRecorded, 1, 0) == 0)
                Interlocked.Exchange(ref _pendingError, "[third-entry] unavailable after readiness/read error; third completion reads false and original Horde selection blocked until shutdown: " + error.GetType().Name + ": " + error.Message);
        }
        internal static void Flush()
        {
            string error = Interlocked.Exchange(ref _pendingError, null);
            if (error != null) SafeInvoke(_error, error);
        }
        internal static void StopWithoutUnityReads() { _enabled = false; _installed = false; }
        internal static void Dispose()
        {
            StopWithoutUnityReads(); Flush();
            var harmony = _harmony;
            if (harmony == null) return;
            harmony.UnpatchSelf(); _harmony = null;
        }
        private static void SafeInvoke(Action<string> callback, string value)
        { try { callback?.Invoke(value); } catch { } }
    }
}
