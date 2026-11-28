using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Il2CppQuantum;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // ---- raised minion tint ------------------------------------------------------------
        // The shipped RaisedMinionVisualEffect (tint/overlay from RaisedMinionComponent) is never put on
        // enemy prefabs because the feature was not released. Track minions from the simulation and
        // attach the effect to their EntityView on the main thread via EntityViewTracker.TryGetByEntityRef.

        private static readonly ConcurrentDictionary<ulong, long> Minions = new();
        private static readonly Dictionary<ulong, int> Attached = new();
        private static readonly Dictionary<ulong, UnityEngine.Component> AddedEffects = new();   // components this plugin added, by minion
        private static readonly Dictionary<IntPtr, int> TintAttempts = new(), TintSuccesses = new();
        private static readonly object VisualsLock = new();
        private static readonly NecromancyPolicy.FeatureFaultGuard VisualsGuard = new(NecromancyPolicy.DefaultFeatureMaxErrors);
        private static int _tintGiveUps;
        private static IntPtr _tintInFlight;
        private static volatile bool _visualsOn;
        private static int _visualTick, _visualLogs, _effectLogs;

        private static void VisualsError(string stage, Exception e)
        {
            if (VisualsGuard.RecordError(out bool justDisabled))
                Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (justDisabled)
            {
                _visualsOn = false;
                Write("visuals disabled after repeated errors; rest of plugin unaffected");
            }
        }

        private static void InstallMinionVisuals()
        {
            var visualHarmony = new HarmonyLib.Harmony("NRFW.Necromancy.MinionVisuals");
            try
            {
                Patch(visualHarmony, typeof(RaisedMinionSystem), "GetMinionFormationSlot", new[] { typeof(Frame), typeof(EntityRef), typeof(EntityRef) },
                    nameof(AfterFormationSlot), typeof(int));
                Patch(visualHarmony, typeof(Il2CppMoon.Forsaken.RaisedMinionVisualEffect), "ApplyTintEffect", Type.EmptyTypes, nameof(AfterApplyTint));
                PatchPrefix(visualHarmony, typeof(Il2CppMoon.Forsaken.RaisedMinionVisualEffect), "ApplyTintEffect", Type.EmptyTypes, nameof(BeforeApplyTint), typeof(void));
                _visualsOn = true;
                Write("minion-visuals hooks installed");
            }
            catch (Exception e) { visualHarmony.UnpatchSelf(); Write("minion-visuals hook FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        private static void AfterFormationSlot(Frame __0, EntityRef __1, EntityRef __2)
        {
            if (__0 == null || !__2.IsValid) return;
            Minions[__2.Raw] = Environment.TickCount64;
            if (__0.IsVerified) RememberRaisedDamageSource(__0, __2);
            if (__0.IsVerified && !MinionBirth.ContainsKey(__2.Raw) && !Expired.Contains(__2.Raw))
                MinionBirth[__2.Raw] = __0.TimeSinceStart.RawValue;
            if (__0.IsVerified && !TeamChecked.Contains(__2.Raw))
            {
                TeamChecked.Add(__2.Raw);
                try { SetMinionTeam(__0, __1, __2); }
                catch (Exception e) { Error("minion-team", e); }
            }
        }

        // 0.12.0 playtest: on darakMutantView, CollectRenderers threw inside ApplyTintEffect every frame (~45/s,
        // 26k errors). The postfix only runs when the native call returns, so attempts without a success mean it is
        // throwing. After 3 failed attempts the component is disabled and never retried.
        private static bool BeforeApplyTint(Il2CppMoon.Forsaken.RaisedMinionVisualEffect __instance)
        {
            if (__instance == null) return false;
            IntPtr key = __instance.Pointer;
            int attempts, ok;
            lock (VisualsLock)
            {
                TintAttempts.TryGetValue(key, out attempts);
                TintSuccesses.TryGetValue(key, out ok);
                if (NecromancyPolicy.ShouldRetryTint(attempts, ok))
                {
                    TintAttempts[key] = attempts + 1;
                    _tintInFlight = key;   // the postfix counts a success only for a call that really ran
                    return true;
                }
            }

            try
            {
                __instance.enabled = false;
                if (Interlocked.Increment(ref _tintGiveUps) <= 10)
                    Write("tint GAVE UP after " + attempts + " failed attempts on go=" + __instance.gameObject.name + "; component disabled");
            }
            catch { }
            return false;
        }

        private static void RemoveAddedEffect(ulong minion)
        {
            UnityEngine.Component component;
            lock (VisualsLock)
            {
                if (!AddedEffects.TryGetValue(minion, out component)) return;
                AddedEffects.Remove(minion);
                if (component != null)
                {
                    TintAttempts.Remove(component.Pointer);
                    TintSuccesses.Remove(component.Pointer);
                }
            }
            try
            {
                if (component != null) UnityEngine.Object.Destroy(component);
                if (++_visualLogs <= 150) Write("tint REMOVED added effect from pooled view of gone minion " + minion);
            }
            catch (Exception e) { VisualsError("tint-remove", e); }
        }

        private static void AfterApplyTint(Il2CppMoon.Forsaken.RaisedMinionVisualEffect __instance)
        {
            if (__instance == null) return;
            IntPtr key = __instance.Pointer;
            lock (VisualsLock)
            {
                if (key != _tintInFlight) return;   // skipped by the prefix: not a success
                _tintInFlight = IntPtr.Zero;
                TintSuccesses.TryGetValue(key, out int ok);
                TintSuccesses[key] = ok + 1;
            }
            if (Interlocked.Increment(ref _effectLogs) > 20) return;
            try
            {
                var r = __instance.m_renderers;
                Write("tint ApplyTintEffect applied=" + __instance.m_effectApplied + " color=" + __instance.m_tintColor +
                      " renderers=" + (r == null ? -1 : r.Length) + " action=" + __instance.m_raiseActionGuid.Value);
            }
            catch (Exception e) { VisualsError("tint-log", e); }
        }

        private static void EnsureMinionVisuals()
        {
            if (!_visualsOn) return;
            try
            {
                long now = Environment.TickCount64;
                foreach (var pair in Minions)
                {
                    if (now - pair.Value > 5000)
                    {
                        Minions.TryRemove(pair.Key, out _);
                        lock (VisualsLock) { Attached.Remove(pair.Key); }
                        RemoveAddedEffect(pair.Key);
                    }
                }
                if (Minions.IsEmpty) return;

                Il2CppSystem.Type effectType = null;
                foreach (var pair in Minions)
                {
                    ulong raw = pair.Key;
                    bool alreadyAttached;
                    lock (VisualsLock) { alreadyAttached = Attached.ContainsKey(raw); }
                    if (alreadyAttached) continue;

                    if (!Il2CppMoon.Forsaken.EntityViewTracker.TryGetByEntityRef(new EntityRef { Raw = raw }, out Il2CppMoon.Forsaken.EntityView view) ||
                        view == null)
                        continue;

                    effectType ??= Il2CppInterop.Runtime.Il2CppType.Of<Il2CppMoon.Forsaken.RaisedMinionVisualEffect>();
                    var go = view.gameObject;
                    if (go == null) continue;
                    var existing = go.GetComponent(effectType);
                    if (existing == null)
                    {
                        var added = go.AddComponent(effectType);
                        lock (VisualsLock)
                        {
                            AddedEffects[raw] = added;
                            Attached[raw] = 1;
                        }
                        if (++_visualLogs <= 150) Write("tint ATTACHED RaisedMinionVisualEffect to minion entity=" + raw + " go=" + go.name);
                    }
                    else
                    {
                        lock (VisualsLock) { Attached[raw] = 2; }
                        if (++_visualLogs <= 150) Write("tint minion entity=" + raw + " already has RaisedMinionVisualEffect (enabled=" +
                                                       existing.TryCast<UnityEngine.Behaviour>()?.enabled + ")");
                    }
                }
            }
            catch (Exception e) { VisualsError("minion-visuals", e); }
        }
    }
}
