using System;
using PatchOwner = HarmonyLib.Harmony;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;

namespace CrucibleUnlock
{
    internal static class BossTraceDropRepair
    {
        private static bool _enabled;
        private static int _errors;

        internal static void Install()
        {
            var owner = new PatchOwner("NRFW.CrucibleUnlock.BossTraces");
            try
            {
                TrialRepairLog.Patch(owner, typeof(InfiniteDungeonDirector), "GetAmountForEnemy",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(ViewSidePickupIntegerContentsType), typeof(InfiniteDungeonBalanceData) },
                    typeof(BossTraceDropRepair), postfix: nameof(AmountForEnemy), returnType: typeof(FP));
                _errors = 0;
                _enabled = true;
                TrialRepairLog.Write("[boss-traces] installed native amount postfix; amount=" + BossTraceDropApi.Amount + "; source=" + TrialRepairPolicy.PlaylistGuid);
            }
            catch { _enabled = false; owner.UnpatchSelf(); throw; }
        }

        private const long HuskInstanceGuid = -214597907L;
        internal static bool IsHuskInstance(Frame frame, EntityRef entity)
        {
            if (!entity.IsValid) return false;
            if (frame.GetNpcEntity(new PureId(HuskInstanceGuid)).Raw == entity.Raw) return true;
            EntityRef spawner = frame.GetNpcSpawnerForEntityInstance(entity);
            if (!spawner.IsValid || !frame.Has<NpcSpawnerComponent>(spawner)) return false;
            // Class wrapper for a native value component: use boxed Get, never TryGet.
            var data = frame.Get<NpcSpawnerComponent>(spawner);
            return data != null && data.NpcGuid == HuskInstanceGuid;
        }

        private static void AmountForEnemy(Frame __0, EntityRef __1, ViewSidePickupIntegerContentsType __2, ref FP __result)
        {
            if (!_enabled || __0 == null || __2 != ViewSidePickupIntegerContentsType.CrucibleTrace) return;
            try
            {
                bool active = InfiniteDungeonAPI.IsCrucibleRunInProgress(__0);
                if (!active) return;
                long playlist = InfiniteDungeonAPI.GetSourcePlaylist(__0).Id.Value;
                if (playlist != TrialRepairPolicy.PlaylistGuid) return;
                bool boss = NpcAPI.IsBoss(__0, __1);
                // This scene's configured boss has EntityData.IsBoss=false.
                // Use its exact scene instance only; never grant ordinary enemies traces.
                if (!boss) boss = IsHuskInstance(__0, __1);
                if (!BossTraceDropPolicy.ShouldOverride(active, playlist, (int)__2, boss)) return;
                int amount = BossTraceDropApi.Amount;
                long original = __result.RawValue;
                // Keep the original calculation/RNG consumption. Change only its return value,
                // in predicted and verified frames alike; native death processing creates the pickups.
                __result.RawValue = BossTraceDropPolicy.ToRawAmount(amount);
                if (__0.IsVerified)
                    TrialRepairLog.Write("[boss-traces] AMOUNT victim=" + __1.Raw + "; originalRaw=" + original + "; amount=" + amount + "; raw=" + __result.RawValue);
            }
            catch (Exception e)
            {
                if (++_errors <= 3) TrialRepairLog.Write("[boss-traces] ERROR " + e.GetType().Name + ": " + e.Message);
                if (_errors >= 3) { _enabled = false; TrialRepairLog.Write("[boss-traces] disabled after errors; restart required"); }
            }
        }
    }
}
