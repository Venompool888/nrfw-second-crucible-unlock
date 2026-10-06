using System;
using HarmonyLib;
using PatchOwner = HarmonyLib.Harmony;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using Il2CppMoon.Forsaken;

namespace CrucibleUnlock
{
    // The configured executioner director has no native health-data implementation.
    // Supply its single NPC's real health to the existing boss HUD and increase only
    // that instance's simulation HP. No game asset or save is changed.
    internal static class BrokenVowBossRepair
    {
        private static bool _enabled;
        private static int _errors;

        internal static void Install()
        {
            var owner = new PatchOwner("NRFW.CrucibleUnlock.BrokenVowBoss");
            try
            {
                TrialRepairLog.Patch(owner, typeof(BossBattleDirector), "TryGetBattleHealthData",
                    new[] { typeof(Frame), typeof(BossBattleHealthData).MakeByRefType() },
                    typeof(BrokenVowBossRepair), postfix: nameof(BattleHealth), returnType: typeof(bool));
                TrialRepairLog.Patch(owner, typeof(StatsComponent), "GetStat",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(StatType), typeof(StatModification) },
                    typeof(BrokenVowBossRepair), postfix: nameof(MaxHp), returnType: typeof(FP));
                TrialRepairLog.Patch(owner, typeof(NpcAPI), "IsBoss",
                    new[] { typeof(Frame), typeof(EntityRef) },
                    typeof(BrokenVowBossRepair), postfix: nameof(IsBoss), returnType: typeof(bool));
                foreach (var type in new[] { typeof(NpcEntityData), typeof(GenericNpcEntityData) })
                    TrialRepairLog.Patch(owner, type, "ShouldShowHealthBar",
                        new[] { typeof(Frame), typeof(EntityRef) },
                        typeof(BrokenVowBossRepair), postfix: nameof(OverheadHealth), returnType: typeof(bool));
                TrialRepairLog.Patch(owner, typeof(BossStatsView), "UpdateHud",
                    new[] { typeof(Frame), typeof(float), typeof(EntityRef) },
                    typeof(BrokenVowBossRepair), postfix: nameof(ViewHud));
                TrialRepairLog.Patch(owner, typeof(BossBattleAPI), "GetFirstValidBossInZone",
                    new[] { typeof(Frame), typeof(int) },
                    typeof(BrokenVowBossRepair), postfix: nameof(HudEntity), returnType: typeof(EntityRef));
                _errors = 0;
                _enabled = true;
                TrialRepairLog.Write("[broken-vow] installed director HUD, overhead visibility, boss identity and final MaxHealth hooks; multiplier=1.5");
            }
            catch { _enabled = false; owner.UnpatchSelf(); throw; }
        }

        private static bool InTrial(Frame frame)
        {
            return frame != null && InfiniteDungeonAPI.IsCrucibleRunInProgress(frame) &&
                   InfiniteDungeonAPI.GetSourcePlaylist(frame).Id.Value == TrialRepairPolicy.PlaylistGuid;
        }

        private static EntityRef Target(Frame frame) => frame.GetNpcEntity(new PureId(BrokenVowBossPolicy.InstanceGuid));

        private static bool IsTargetEntity(Frame frame, EntityRef entity)
        {
            if (entity.Raw == 0) return false;
            if (Target(frame).Raw == entity.Raw) return true;
            EntityRef spawner = frame.GetNpcSpawnerForEntityInstance(entity);
            if (spawner.Raw == 0 || !frame.Has<NpcSpawnerComponent>(spawner)) return false;
            // Native struct emitted as a class: TryGet's output slot is too small.
            var data = frame.Get<NpcSpawnerComponent>(spawner);
            return data != null && data.NpcGuid == BrokenVowBossPolicy.InstanceGuid &&
                   data.MainNpc.EntityInstanceRef.Raw == entity.Raw;
        }

        // BossBattleType.None can select the explicit NoBoss strategy from the
        // serialized view map. The native UpdateHud inlines that lookup, so
        // patching GetHealthBarStrategy alone does not intercept the selection.
        private static void ViewHud(BossStatsView __instance, Frame __0)
        {
            if (!_enabled || __instance == null || __0 == null) return;
            try
            {
                var strategy = __instance.m_currentViewStrategy;
                if (strategy == null || strategy.BossBattleZoneId != BrokenVowBossPolicy.DirectorZoneGuid ||
                    !InTrial(__0) || strategy.TryCast<NoBossHealthBarStrategy>() == null) return;
                var entity = Target(__0);
                if (entity.Raw == 0 || !__0.Has<HealthComponent>(entity)) return;
                var health = __0.Get<HealthComponent>(entity);
                if (health == null || health.Amount.RawValue <= 0) return;
                var coordinator = strategy.AiCoordinatorRef;
                var replacement = new SingleBossHealthBarStrategy();
                __instance.ReleaseViewStrategy();
                __instance.m_currentViewStrategy = replacement;
                replacement.Initialize(__instance, BrokenVowBossPolicy.DirectorZoneGuid, coordinator);
                TrialRepairLog.Write("[broken-vow] HUD_STRATEGY NoBoss->SingleBoss; zone=" +
                    BrokenVowBossPolicy.DirectorZoneGuid + "; entity=" + entity.Raw);
            }
            catch (Exception e) { Error("view-hud", e); }
        }

        // SingleBossHealthBarStrategy queries this API, independently of the
        // director's aggregate health data. Pin it to the main NPC, never its shield.
        private static void HudEntity(Frame __0, int __1, ref EntityRef __result)
        {
            if (!_enabled || __0 == null || __1 != BrokenVowBossPolicy.DirectorZoneGuid) return;
            try
            {
                if (!InTrial(__0)) return;
                __result = default;
                var entity = Target(__0);
                if (entity.Raw == 0 || !__0.Has<HealthComponent>(entity)) return;
                var health = __0.Get<HealthComponent>(entity);
                if (health != null && health.Amount.RawValue > 0) __result = entity;
            }
            catch (Exception e) { Error("hud-entity", e); }
        }

        private static void IsBoss(Frame __0, EntityRef __1, ref bool __result)
        {
            if (!_enabled || __result || __0 == null) return;
            try
            {
                if (InTrial(__0) && IsTargetEntity(__0, __1))
                    __result = true;
            }
            catch (Exception e) { Error("boss-identity", e); }
        }

        private static void OverheadHealth(Frame __0, EntityRef __1, ref bool __result)
        {
            if (!_enabled || !__result || __0 == null) return;
            try
            {
                if (InTrial(__0) && IsTargetEntity(__0, __1)) __result = false;
            }
            catch (Exception e) { Error("overhead-health", e); }
        }

        private static void BattleHealth(BossBattleDirector __instance, Frame __0,
            ref BossBattleHealthData __1, ref bool __result)
        {
            if (!_enabled || __result || __instance == null || __0 == null) return;
            try
            {
                if (!InTrial(__0) || !BrokenVowBossPolicy.MatchesDirector(true,
                    TrialRepairPolicy.PlaylistGuid, __instance.StrategyData.Id.Value) ||
                    __instance.GetBattleState(__0) != BossBattleState.Pending) return;
                EntityRef entity = Target(__0);
                if (entity.Raw == 0 || !__0.Has<HealthComponent>(entity)) return;
                var health = __0.Get<HealthComponent>(entity);
                if (health == null || health.MaxAmount.RawValue <= 0 || health.Amount.RawValue <= 0) return;
                __1.Representative = entity;
                __1.TotalHealthRatio = health.Ratio;
                __1.Stage = 0;
                __result = true;
            }
            catch (Exception e) { Error("hud", e); }
        }

        // HealthSystem.OnStatsUpdate reads GetStat(MaxHealth) into the component.
        // Scale the computed result, never a stored value: repeated updates cannot compound.
        private static void MaxHp(Frame __0, EntityRef __1, StatType __2, ref FP __result)
        {
            if (!_enabled || __0 == null || __2 != StatType.MaxHealth || __result.RawValue <= 0) return;
            try
            {
                if (!InTrial(__0) || !IsTargetEntity(__0, __1)) return;
                long original = __result.RawValue;
                long scaled = BrokenVowBossPolicy.ScaleHealthRaw(original);
                if (scaled == original) return;
                __result.RawValue = scaled;
                if (__0.IsVerified)
                    TrialRepairLog.Write("[broken-vow] MAX_HP entity=" + __1.Raw +
                        "; originalRaw=" + original + "; scaledRaw=" + scaled);
            }
            catch (Exception e) { Error("max-hp", e); }
        }

        private static void Error(string stage, Exception error)
        {
            if (++_errors <= 3)
                TrialRepairLog.Write("[broken-vow] ERROR " + stage + ": " + error.GetType().Name + ": " + error.Message);
            if (_errors >= 3)
            {
                _enabled = false;
                TrialRepairLog.Write("[broken-vow] disabled after errors; restart required");
            }
        }
    }
}
