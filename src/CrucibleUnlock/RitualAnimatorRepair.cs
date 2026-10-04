using System;
using System.Collections.Generic;
using HarmonyLib;
using PatchOwner = HarmonyLib.Harmony;
using Il2CppMoon;
using Il2CppMoon.Forsaken;

namespace CrucibleUnlock
{
    internal static class RitualAnimatorRepair
    {
        private sealed class Binding
        {
            public ForsakenAnimationPlayer Track;
            public MoonReference<MoonAnimator> Original, Installed;
            public long OwnerAnimator;
        }
        private static readonly Dictionary<IntPtr, Binding> Bindings = new Dictionary<IntPtr, Binding>();
        private static PatchOwner _harmony;
        private static bool _enabled;
        private static int _errors;

        public static void Install()
        {
            var h = new PatchOwner("NRFW.CrucibleUnlock.RitualAnimator");
            try
            {
                TrialRepairLog.Patch(h, typeof(TimelineActionView), "OnStartView", Type.EmptyTypes,
                    typeof(RitualAnimatorRepair), prefix: nameof(BeforeTimeline), postfix: nameof(AfterTimeline));
                // The installed Harmony bridge writes pointer-sized values through
                // ref enums. Never patch OnStopView(ref StopReason): StopReason is 4 bytes.
                TrialRepairLog.Patch(h, typeof(ForsakenAnimationPlayer), "OnStopPlayback", Type.EmptyTypes,
                    typeof(RitualAnimatorRepair), postfix: nameof(PlaybackStopped));
                TrialRepairLog.Patch(h, typeof(ActionView), "OnReturnedToPool", Type.EmptyTypes,
                    typeof(RitualAnimatorRepair), prefix: nameof(BeforeReturnToPool));
                TrialRepairLog.Patch(h, typeof(KneelTeleportActionView), "OnStartView", Type.EmptyTypes,
                    typeof(RitualAnimatorRepair), postfix: nameof(KneelStarted));
                TrialRepairLog.Patch(h, typeof(ForsakenAnimationPlayer), "OnStartPlayback", Type.EmptyTypes,
                    typeof(RitualAnimatorRepair), postfix: nameof(PlaybackStarted));
                _harmony = h; _enabled = true;
                TrialRepairLog.Write("[ritual-animator] installed exact offering action/unique sacrifice track binding; original timeline preserved");
            }
            catch { _enabled = false; h.UnpatchSelf(); throw; }
        }

        private static long Id(MoonAnimator animator) => animator == null ? 0 : animator.Pointer.ToInt64();

        private static void BeforeTimeline(TimelineActionView __instance)
        {
            if (!_enabled || __instance == null) return;
            try
            {
                var asset = __instance.QuantumAsset;
                if (asset == null || asset.AssetGuid.Value != TrialRepairPolicy.OfferingGuid) return;
                var hero = __instance.OwningEntityView?.TryCast<HeroView>();
                if (hero == null || !hero.IsBoundToEntity) { TrialRepairLog.Write("[ritual-animator] offering has no bound hero; unchanged"); return; }
                var expected = hero.Animator;
                var timeline = __instance.EffectiveTimeline;
                var records = timeline?.EntityRecords;
                ForsakenAnimationPlayer track = null;
                int count = 0;
                if (records != null) foreach (var record in records)
                {
                    var player = record?.TimelineItem?.TryCast<ForsakenAnimationPlayer>();
                    if (player == null || player.EffectiveAnimation == null || player.EffectiveAnimation.name != "sacrifice") continue;
                    track = player; ++count;
                }
                var current = track?.Animator?.SafeResolve(null);
                var interaction = __instance.TryCast<InteractionView>();
                var interactable = interaction?.Interactable?.SafeResolve(null);
                var bowl = interactable?.TryCast<GeneralInteractableView>();
                TrialRepairLog.Write("[ritual-animator] BEFORE view=" + __instance.Pointer + "; hero=" + hero.EntityRef.Raw +
                    "; tracks=" + count + "; expected=" + Id(expected) + "; actual=" + Id(current) +
                    "; main=" + Id(__instance.MainAnimator) + "; bowl=" + (bowl?.Data?.Name ?? "unknown") +
                    "; activeInteraction=" + (bowl?.m_activeInteraction?.Pointer.ToString() ?? "null"));
                if (count != 1 || Id(expected) == 0) return;
                if (Bindings.ContainsKey(__instance.Pointer)) { TrialRepairLog.Write("[ritual-animator] duplicate active view; unchanged"); return; }
                foreach (var existing in Bindings.Values)
                    if (existing.Track != null && existing.Track.Pointer == track.Pointer)
                    { TrialRepairLog.Write("[ritual-animator] shared track already owned; unchanged"); return; }
                var binding = new Binding { Track = track, OwnerAnimator = Id(expected) };
                Bindings.Add(__instance.Pointer, binding);
                // Observe a correctly resolving cloned view too; successful cloning is not playback proof.
                if (!TrialRepairPolicy.ShouldRepairRitual(asset.AssetGuid.Value, true, count, Id(expected), Id(current))) return;
                binding.Original = track.Animator;
                binding.Installed = new MoonReference<MoonAnimator>(expected);
                // Replace only this track's reference object; never mutate a shared resolver or first-bowl state.
                track.Animator = binding.Installed;
                var readback = track.Animator.SafeResolve(null);
                if (Id(readback) != Id(expected)) throw new InvalidOperationException("Animator reference readback mismatch");
                TrialRepairLog.Write("[ritual-animator] APPLIED view=" + __instance.Pointer + "; animator=" + Id(readback));
            }
            catch (Exception e)
            {
                Restore(__instance?.Pointer ?? IntPtr.Zero);
                Error("bind", e);
            }
        }

        private static void AfterTimeline(TimelineActionView __instance)
        {
            if (__instance == null) return;
            try
            {
                if (Bindings.TryGetValue(__instance.Pointer, out var binding))
                    TrialRepairLog.Write("[ritual-animator] TIMELINE_ENTERED view=" + __instance.Pointer + "; animator_snapshot=" + Id(binding.Track.m_animator) +
                        "; expected=" + binding.OwnerAnimator + "; clip=" + binding.Track.EffectiveAnimation?.name);
                var asset = __instance.QuantumAsset;
                if (asset != null && asset.AssetGuid.Value == 1923998648926663470L)
                    TrialRepairLog.Write("[trial-transition] arrival stand-up action started; view=" + __instance.Pointer);
            }
            catch (Exception e) { Error("observe", e); }
        }

        private static void BeforeReturnToPool(ActionView __instance) { Restore(__instance?.Pointer ?? IntPtr.Zero); }
        private static void PlaybackStopped(ForsakenAnimationPlayer __instance)
        {
            try
            {
                if (__instance == null || Bindings.Count == 0) return;
                IntPtr owner = IntPtr.Zero;
                foreach (var entry in Bindings)
                    if (entry.Value.Track != null && entry.Value.Track.Pointer == __instance.Pointer)
                    { owner = entry.Key; break; }
                // Do not remove from the dictionary while enumerating. Native stop
                // has already interrupted the cached animator/handle at this point.
                if (owner != IntPtr.Zero) Restore(owner);
            }
            catch (Exception e) { Error("stop playback restoration", e); }
        }
        private static void PlaybackStarted(ForsakenAnimationPlayer __instance)
        {
            try
            {
                if (__instance == null || Bindings.Count == 0) return;
                foreach (var binding in Bindings.Values)
                    if (binding.Track != null && binding.Track.Pointer == __instance.Pointer)
                    {
                        TrialRepairLog.Write("[ritual-animator] PLAYBACK animator=" + Id(__instance.m_animator) +
                            "; expected=" + binding.OwnerAnimator + "; matches=" + (Id(__instance.m_animator) == binding.OwnerAnimator));
                        break;
                    }
            }
            catch (Exception e) { Error("playback observation", e); }
        }
        private static void KneelStarted(KneelTeleportActionView __instance)
        {
            try
            {
                var asset = __instance?.QuantumAsset;
                if (asset != null && asset.AssetGuid.Value == 2867547894435745061L)
                    TrialRepairLog.Write("[trial-transition] native kneel/channel action started; view=" + __instance.Pointer);
            }
            catch (Exception e) { Error("kneel observation", e); }
        }
        private static void Restore(IntPtr owner)
        {
            if (owner == IntPtr.Zero || !Bindings.TryGetValue(owner, out var binding)) return;
            try
            {
                if (binding.Installed == null) { Bindings.Remove(owner); return; }
                if (binding.Track != null && TrialRepairPolicy.ShouldRestore(binding.Installed.Pointer.ToInt64(), binding.Track.Animator?.Pointer.ToInt64() ?? 0))
                { binding.Track.Animator = binding.Original; TrialRepairLog.Write("[ritual-animator] RESTORED view=" + owner); }
                else TrialRepairLog.Write("[ritual-animator] restoration skipped: track destroyed or reference replaced");
                Bindings.Remove(owner);
            }
            catch (Exception e) { Error("restore; restart required", e); _enabled = false; }
        }
        private static void Error(string stage, Exception e)
        {
            if (++_errors <= 3) TrialRepairLog.Write("[ritual-animator] ERROR " + stage + ": " + e.GetType().Name + ": " + e.Message);
            if (_errors >= 3) _enabled = false;
        }
    }
}
