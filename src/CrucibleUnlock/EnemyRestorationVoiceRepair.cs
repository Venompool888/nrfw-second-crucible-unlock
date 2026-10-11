using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Wwise.MoonWwiseTypes;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;
using EntityView = Il2CppMoon.Forsaken.EntityView;

namespace CrucibleUnlock
{
    // R17: exact private-owner voice identity repair. The restored family
    // switches have shipped dialogue-tree entries but their audio-node targets
    // are absent from this build's banks. Select the corresponding source NPC's
    // available voice pool without changing its simulation CharacterType.
    // Native assignment and the subsequent SetAudioAttributes still run.
    internal static class EnemyRestorationVoiceRepair
    {
        private sealed class Lease
        {
            internal CharacterView View;
            internal SwitchReference Before, Applied;
            internal ulong Entity;
            internal IntPtr Manager;
            internal int ProfileIndex, CharacterType, VoiceType;
            internal bool Suppressed;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<IntPtr, Lease> Leases = new Dictionary<IntPtr, Lease>();
        private static int[] _reported = new int[10];
        private static int _reportedError;
        private static volatile bool _active;

        internal static void Install(PatchOwner owner)
        {
            _active = false;
            PrepareHelpers(null);
            TrialRepairLog.Patch(owner, typeof(CharacterView), "FulfillVoiceAssignment", Type.EmptyTypes,
                typeof(EnemyRestorationVoiceRepair), prefix: nameof(BeforeFulfill), postfix: nameof(AfterFulfill));
            TrialRepairLog.Patch(owner, typeof(CharacterView), "OnUnboundFromEntity", Type.EmptyTypes,
                typeof(EnemyRestorationVoiceRepair), prefix: nameof(BeforeUnbound));
            TrialRepairLog.Patch(owner, typeof(CharacterView), "OnReturnedToPool", Type.EmptyTypes,
                typeof(EnemyRestorationVoiceRepair), prefix: nameof(BeforeReturned));
            TrialRepairLog.Patch(owner, typeof(EntityView), "OnDestroy", Type.EmptyTypes,
                typeof(EnemyRestorationVoiceRepair), prefix: nameof(BeforeDestroyed));
            TrialRepairLog.Write("[enemy-voice] exact private-owner source-voice fallback and lifecycle hooks installed");
        }

        internal static void PrepareHelpers(Action<string> report)
        {
            foreach (var name in new[] { nameof(BeforeFulfill), nameof(AfterFulfill), nameof(BeforeUnbound),
                nameof(BeforeReturned), nameof(BeforeDestroyed), nameof(Apply), nameof(Release),
                nameof(SafeRelease), nameof(RestoreOwned), nameof(Observe), nameof(ContainsChoice), nameof(GetVoicePoolType),
                nameof(Same), nameof(Describe), nameof(ReportMissingPool), nameof(ReportError) })
                Prepare(name);
            report?.Invoke("[enemy-voice] native callbacks and helpers prepared");
        }

        private static void Prepare(string name)
        {
            var method = typeof(EnemyRestorationVoiceRepair).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException(typeof(EnemyRestorationVoiceRepair).FullName, name);
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }

        internal static void ResumeAfterViewPublication() { _active = true; }

        // Resolver reset/disable does not read Unity. Leases survive so the
        // normal return/unbind/destroy callbacks can restore before pool reuse.
        internal static void StopWithoutUnityReads()
        {
            _active = false;
            Volatile.Write(ref _reported, new int[10]);
            Interlocked.Exchange(ref _reportedError, 0);
        }

        // Explicit live-module disposal should run on Unity's thread. The
        // shared Harmony transaction owner remains managed by the runtime.
        internal static void Dispose()
        {
            lock (Gate)
            {
                foreach (var lease in Leases.Values)
                    try { RestoreOwned(lease); }
                    catch (Exception error) { ReportError(error); }
                Leases.Clear();
            }
            StopWithoutUnityReads();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeFulfill(CharacterView __instance)
        {
            if (!_active || !EnemyRestorationRuntime.CurrentViewsReady || __instance == null) return;
            try { Apply(__instance, EnemyRestorationRuntime.Catalog); }
            catch (Exception error) { ReportError(error); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AfterFulfill(CharacterView __instance)
        {
            if (!_active || __instance == null) return;
            try { Observe(__instance); }
            catch (Exception error) { ReportError(error); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeUnbound(CharacterView __instance) { SafeRelease(__instance); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeReturned(CharacterView __instance) { SafeRelease(__instance); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeDestroyed(EntityView __instance)
        {
            try { SafeRelease(__instance?.TryCast<CharacterView>()); }
            catch (Exception error) { ReportError(error); }
        }

        private static void SafeRelease(CharacterView view)
        {
            if (view == null) return;
            try { Release(view); }
            catch (Exception error) { ReportError(error); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static unsafe void Apply(CharacterView view, EnemyRestorationCatalog catalog)
        {
            if (!_active || !EnemyRestorationRuntime.CurrentViewsReady || view == null ||
                catalog?.Profiles == null || catalog.Profiles.Length != 10 || view.Pointer == IntPtr.Zero) return;
            lock (Gate)
            {
                var entity = view.EntityRef;
                var frame = view.PredictedFrame;
                IntPtr manager = frame?.Context?.ResourceManager?.Pointer ?? IntPtr.Zero;
                if (Leases.TryGetValue(view.Pointer, out var existing))
                {
                    if (entity.Raw != 0 && existing.Entity == entity.Raw && existing.Manager == manager)
                    {
                        if (!existing.Suppressed && !Same(view.CustomVoiceSwitch, existing.Applied))
                            existing.Suppressed = true;
                        return; // Honor a later switch written by another system.
                    }
                    if (existing.Manager == manager) RestoreOwned(existing);
                    Leases.Remove(view.Pointer);
                }
                if (entity.Raw == 0 || frame == null || manager == IntPtr.Zero ||
                    !EnemyRestorationOwnerScope.TryGetProfile(view, catalog, out var profile) ||
                    profile == null || profile.Index < 0 || profile.Index >= 10 ||
                    !ReferenceEquals(catalog.Profiles[profile.Index], profile)) return;
                // The owner proof checks both current instance-data slots and
                // dynamic assets. Check the exact bound component type as well.
                if (!frame.Has<NpcAttachmentComponent>(entity) || NpcAttachmentComponent.SIZE != 144)
                    return;
                var pointer = EntityRefExtensions.Get<NpcAttachmentComponent>(entity, frame);
                if (pointer == null) return;
                int attachedType = *(int*)pointer;
                if (attachedType != profile.CharacterType) return;
                int voiceType = GetVoicePoolType(profile);
                var pool = WwiseGlobalDefinitions.Instance?.Voices?.GetCharacterVoicePool((CharacterType)voiceType);
                if (pool == null || pool.Pointer == IntPtr.Zero) { ReportMissingPool(profile, attachedType, voiceType); return; }
                var choices = pool.VoiceSwitchSelects;
                if (choices == null || choices.Length == 0) { ReportMissingPool(profile, attachedType, voiceType); return; }
                var selected = pool.GetRandomVoiceFromPool();
                if (selected == null || selected.Pointer == IntPtr.Zero || !selected.IsValid() ||
                    !ContainsChoice(choices, selected)) { ReportMissingPool(profile, attachedType, voiceType); return; }
                var before = view.CustomVoiceSwitch;
                view.CustomVoiceSwitch = selected; // generated setter uses IL2CPP GC write barrier
                try
                {
                    Leases.Add(view.Pointer, new Lease { View = view, Before = before, Applied = selected,
                        Entity = entity.Raw, Manager = manager, ProfileIndex = profile.Index,
                        CharacterType = attachedType, VoiceType = voiceType });
                }
                catch
                {
                    if (Same(view.CustomVoiceSwitch, selected)) view.CustomVoiceSwitch = before;
                    throw;
                }
            }
        }

        private static int GetVoicePoolType(EnemyRestorationProfile profile)
        {
            // Steam build 22928553: only these seven exact restored profiles
            // have missing native aiVoice/VoiceEfforts audio-node targets.
            // Donor identity is independently read from the source NPC assets;
            // checking its GUID prevents carrying this fallback to a new donor.
            switch (profile.Key)
            {
                case "inquisitor_halberd":
                    if (profile.Index == 0 && profile.CharacterType == 31 && profile.NpcTemplateGuid == 1084336240379028843) return 5;
                    break;
                case "inquisitor_greatsword":
                    if (profile.Index == 1 && profile.CharacterType == 118 && profile.NpcTemplateGuid == 3492132779696034020) return 5;
                    break;
                case "inquisitor_bruiser":
                    if (profile.Index == 2 && profile.CharacterType == 119 && profile.NpcTemplateGuid == 1476551916947896192) return 47;
                    break;
                case "dead_tide_curved_sword":
                    if (profile.Index == 6 && profile.CharacterType == 120 && profile.NpcTemplateGuid == 2685015411955791552) return 5;
                    break;
                case "dead_tide_great_curved_sword":
                    if (profile.Index == 7 && profile.CharacterType == 120 && profile.NpcTemplateGuid == 3492132779696034020) return 5;
                    break;
                case "dead_tide_crossbow":
                    if (profile.Index == 8 && profile.CharacterType == 120 && profile.NpcTemplateGuid == 2387481653164533623) return 5;
                    break;
                case "dead_tide_bruiser":
                    if (profile.Index == 9 && profile.CharacterType == 121 && profile.NpcTemplateGuid == 231941218437941599) return 43;
                    break;
            }
            return profile.CharacterType;
        }

        private static bool ContainsChoice(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SwitchReference> choices,
            SwitchReference selected)
        {
            // Bounded official pool membership; no shared SwitchReference is mutated.
            if (choices.Length > 32) return false;
            for (int i = 0; i < choices.Length; ++i)
                if (Same(choices[i], selected)) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Release(CharacterView view)
        {
            if (view == null || view.Pointer == IntPtr.Zero) return;
            lock (Gate)
            {
                if (!Leases.TryGetValue(view.Pointer, out var lease)) return;
                try { RestoreOwned(lease); }
                finally { Leases.Remove(view.Pointer); }
            }
        }

        private static void RestoreOwned(Lease lease)
        {
            if (lease.Suppressed) return;
            var view = lease.View;
            if (view != null && view.Pointer != IntPtr.Zero && Same(view.CustomVoiceSwitch, lease.Applied))
                view.CustomVoiceSwitch = lease.Before;
        }

        private static bool Same(SwitchReference a, SwitchReference b) =>
            (a?.Pointer ?? IntPtr.Zero) == (b?.Pointer ?? IntPtr.Zero);

        private static void Observe(CharacterView view)
        {
            if (view.Pointer == IntPtr.Zero) return;
            Lease lease;
            lock (Gate) { if (!Leases.TryGetValue(view.Pointer, out lease)) return; }
            if (lease.Suppressed) return;
            if (lease.ProfileIndex < 0 || lease.ProfileIndex >= 10 ||
                Interlocked.CompareExchange(ref _reported[lease.ProfileIndex], 1, 0) != 0) return;
            var after = view.CustomVoiceSwitch;
            var host = view.PrimarySoundHost;
            TrialRepairLog.Write("[enemy-voice] profile=" + EnemyRestorationRuntime.Catalog.Profiles[lease.ProfileIndex].Key +
                "; entity=" + lease.Entity + "; type=" + lease.CharacterType +
                "; voiceType=" + lease.VoiceType + "; donor-fallback=" + (lease.VoiceType != lease.CharacterType).ToString().ToLowerInvariant() +
                "; before=" + Describe(lease.Before) + "; selected=" + Describe(lease.Applied) +
                "; after=" + Describe(after) + "; host=0x" + (host?.Pointer ?? IntPtr.Zero).ToInt64().ToString("X") +
                "; presence-only=true");
        }

        private static string Describe(SwitchReference value)
        {
            if (value == null || value.Pointer == IntPtr.Zero) return "0";
            return "0x" + value.Pointer.ToInt64().ToString("X") + "/" + value.Guid;
        }

        private static void ReportMissingPool(EnemyRestorationProfile profile, int type, int voiceType)
        {
            if (Interlocked.CompareExchange(ref _reported[profile.Index], 1, 0) == 0)
                TrialRepairLog.Write("[enemy-voice] profile=" + profile.Key + "; type=" + type +
                    "; voiceType=" + voiceType +
                    "; official voice pool unavailable/invalid; assignment left native");
        }

        private static void ReportError(Exception error)
        {
            if (Interlocked.Exchange(ref _reportedError, 1) != 0) return;
            try { TrialRepairLog.Write("[enemy-voice] scoped repair failed: " + error); }
            catch { /* Native callback must continue. */ }
        }
    }
}
