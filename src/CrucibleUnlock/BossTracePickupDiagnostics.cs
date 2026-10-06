using System;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Observe the native pipeline only: never change arguments, results, RNG or resources.
    internal static class BossTracePickupDiagnostics
    {
        private static bool _enabled;
        private static int _errors;
        internal static void Install()
        {
            var owner = new PatchOwner("NRFW.CrucibleUnlock.TracePickupDiagnostics");
            try
            {
                TrialRepairLog.Patch(owner, typeof(InfiniteDungeonDirector), "GeneratePickups",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(EntityRef), typeof(ViewSidePickupIntegerContentsType),
                        typeof(FP), typeof(FP), typeof(bool), typeof(bool) },
                    typeof(BossTracePickupDiagnostics), prefix: nameof(BeforeGenerate), postfix: nameof(AfterGenerate));
                TrialRepairLog.Patch(owner, typeof(ViewSidePickupAPI), "DeliverContent",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(ViewSidePickupIntegerContentsType), typeof(int) },
                    typeof(BossTracePickupDiagnostics), postfix: nameof(AfterDeliver));
                _errors = 0; _enabled = true;
                TrialRepairLog.Write("[boss-traces/pickups] installed generation/delivery observers; no resource writes");
            }
            catch { _enabled = false; owner.UnpatchSelf(); throw; }
        }
        private static bool InScope(Frame frame, ViewSidePickupIntegerContentsType type) =>
            _enabled && frame != null && frame.IsVerified && type == ViewSidePickupIntegerContentsType.CrucibleTrace &&
            InfiniteDungeonAPI.IsCrucibleRunInProgress(frame) &&
            InfiniteDungeonAPI.GetSourcePlaylist(frame).Id.Value == TrialRepairPolicy.PlaylistGuid;

        private static void BeforeGenerate(Frame __0, EntityRef __1, EntityRef __2, ViewSidePickupIntegerContentsType __3,
            FP __4, FP __5, bool __6, bool __7) => Generated("ENTER", __0, __1, __2, __3, __4, __5, __6, __7);
        private static void AfterGenerate(Frame __0, EntityRef __1, EntityRef __2, ViewSidePickupIntegerContentsType __3,
            FP __4, FP __5, bool __6, bool __7) => Generated("RETURN", __0, __1, __2, __3, __4, __5, __6, __7);
        private static void Generated(string stage, Frame frame, EntityRef source, EntityRef target,
            ViewSidePickupIntegerContentsType type, FP total, FP perOrb, bool share, bool ignoreDistance)
        {
            try
            {
                if (!InScope(frame, type)) return;
                string positions = "";
                if (stage == "ENTER")
                {
                    try
                    {
                        if (frame.TryGet<TransformComponent>(source, out var transform))
                            positions += "; sourcePosition=" + Describe(transform.Position);
                        // Native GeneratePickups prefers the last safe grounded position.
                        // Movement is a class wrapper: never use TryGet's tiny out buffer.
                        if (frame.Has<MovementComponent>(source))
                        {
                            var movement = frame.Get<MovementComponent>(source);
                            var state = movement?.Internal;
                            if (state != null) positions += "; lastSafeGrounded=" + Describe(state.LastSafeGroundedPosition);
                        }
                    }
                    catch (Exception e) { positions += "; positionReadError=" + e.GetType().Name; }
                }
                TrialRepairLog.Write("[boss-traces/pickups] GENERATE_" + stage + "; source=" + source.Raw +
                    "; target=" + target.Raw + "; totalRaw=" + total.RawValue + "; perOrbRaw=" + perOrb.RawValue +
                    "; share=" + share + "; ignoreDistance=" + ignoreDistance + positions);
            }
            catch (Exception e) { Error(e); }
        }
        private static string Describe(FPVector3 value) => "raw(" + value.X.RawValue + "," + value.Y.RawValue + "," + value.Z.RawValue + ")";
        private static void AfterDeliver(Frame __0, EntityRef __1, ViewSidePickupIntegerContentsType __2, int __3)
        {
            try
            {
                if (!InScope(__0, __2)) return;
                TrialRepairLog.Write("[boss-traces/pickups] DELIVER_RETURN hero=" + __1.Raw + "; taken=" + __3);
            }
            catch (Exception e) { Error(e); }
        }
        private static void Error(Exception e)
        {
            if (++_errors <= 3) TrialRepairLog.Write("[boss-traces/pickups] ERROR " + e.GetType().Name + ": " + e.Message);
            if (_errors >= 3) _enabled = false;
        }
    }
}
