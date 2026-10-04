using System;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Repair this entity's action data on its simulation callback. Never change the
    // shared coordinator asset or retain a Frame/component pointer across callbacks.
    internal static class WarrickPhase2TargetRepair
    {
        private static PatchOwner _harmony;
        private static volatile bool _enabled;
        private static int _errors;

        internal static unsafe void Install(Action<string> log, Action<string> error)
        {
            if (_harmony != null) throw new InvalidOperationException("Phase2 target repair already installed.");
            WarrickCoordinatorLookup.Initialize();
            // Verified against this build's native metadata and Execute body.
            if (Marshal.OffsetOf<LeapAttackInstanceData>(nameof(LeapAttackInstanceData.TargetPosition)).ToInt64() != 0x58 ||
                Marshal.OffsetOf<LeapAttackInstanceData>(nameof(LeapAttackInstanceData.TargetPositionDirectly)).ToInt64() != 0x28 ||
                sizeof(FPVector3) != 24 || sizeof(LeapAttackInstanceData) < 0x70)
                throw new InvalidOperationException("Phase2 action data layout differs from verified build.");

            var harmony = new PatchOwner("NRFW.CrucibleUnlock.WarrickPhase2Target");
            try
            {
                TrialRepairLog.Patch(harmony, typeof(LeapAttackData), nameof(LeapAttackData.Execute),
                    new[] { typeof(Frame), typeof(EntityRef) }, typeof(WarrickPhase2TargetRepair),
                    postfix: nameof(Executed), returnType: typeof(bool));
                _errors = 0; _harmony = harmony; _enabled = true;
                log?.Invoke("[phase2-target/v2] installed per-execution entity target repair; no pre-spawn scan or shared asset override");
            }
            catch { _enabled = false; harmony.UnpatchSelf(); throw; }
        }

        private static unsafe void Executed(LeapAttackData __instance, Frame __0, EntityRef __1, bool __result)
        {
            if (!_enabled || !__result || __instance == null || __0 == null || __1.Raw == 0) return;
            try
            {
                if (__instance.Guid.Value != WarrickPhase2TargetPolicy.LeapGuid) return;
                // Apply to verified AND predicted frames, including rollback/replay.
                if (!WarrickCoordinatorLookup.TryGetGuid(__0, __1, out long coordinatorGuid) ||
                    coordinatorGuid != WarrickPhase2TargetPolicy.CoordinatorGuid)
                { Record("SKIP coordinator", __0, __1); return; }
                if (!__0.TryGet<TransformComponent>(__1, out var transform))
                { Record("SKIP transform", __0, __1); return; }

                // Native Execute has initialized TargetPosition at +0x58. Resolve this
                // action's allocation for this entity before changing only its target.
                var instance = __instance.GetOwnInstanceData<LeapAttackInstanceData>(__0, __1);
                if (instance == null) { Record("SKIP instance", __0, __1); return; }
                var before = instance->TargetPosition;
                var position = transform.Position;
                if (!WarrickPhase2TargetPolicy.ShouldRepair(__instance.Guid.Value, coordinatorGuid,
                    (bool)instance->TargetPositionDirectly, before.X.RawValue, before.Y.RawValue, before.Z.RawValue,
                    position.X.RawValue, position.Y.RawValue, position.Z.RawValue))
                { Record("KEEP target=" + Describe(before) + "; position=" + Describe(position), __0, __1); return; }

                // Same target as native zoneBound=false: this entity's current simulation
                // position. The native leap/taunt/camera sequence continues normally.
                instance->TargetPosition = position;
                var after = instance->TargetPosition;
                if (!Same(after, position)) throw new InvalidOperationException("Entity target readback mismatch.");
                Record("APPLIED original=" + Describe(before) + "; target=" + Describe(after) + "; readback=true", __0, __1);
            }
            catch (Exception ex)
            {
                int errors = Interlocked.Increment(ref _errors);
                if (errors <= 3) TrialRepairLog.Write("[phase2-target/v2] ERROR " + ex.GetType().Name + ": " + ex.Message);
                if (errors >= 3) _enabled = false;
                if (errors == 3) TrialRepairLog.Write("[phase2-target/v2] disabled after errors; restart required");
            }
        }

        private static void Record(string message, Frame frame, EntityRef entity) =>
            TrialRepairLog.Write("[phase2-target/v2] " + message + "; entity=" + entity.Raw + "; verified=" + frame.IsVerified);
        private static bool Same(FPVector3 a, FPVector3 b) =>
            a.X.RawValue == b.X.RawValue && a.Y.RawValue == b.Y.RawValue && a.Z.RawValue == b.Z.RawValue;
        private static string Describe(FPVector3 p) => "raw(" + p.X.RawValue + "," + p.Y.RawValue + "," + p.Z.RawValue + ")";

        // Preserve the loader lifecycle API for parallel ritual changes. No scans/writes.
        internal static void Tick() { }
        internal static void StopWithoutUnityReads() { _enabled = false; }
    }
}

