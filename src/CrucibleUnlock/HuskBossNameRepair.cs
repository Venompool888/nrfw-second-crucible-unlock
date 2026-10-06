using System;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    internal static class HuskBossNameRepair
    {
        private static bool _enabled;
        private static int _errors;
        private static string _lastApplied, _lastHidden;

        internal static void Install()
        {
            var owner = new PatchOwner("NRFW.CrucibleUnlock.HuskBossName");
            try
            {
                TrialRepairLog.Patch(owner, typeof(BossStatsView), "UpdateHud",
                    new[] { typeof(Frame), typeof(float), typeof(EntityRef) },
                    typeof(HuskBossNameRepair), postfix: nameof(AfterUpdateHud));
                foreach (var type in new[] { typeof(NpcEntityData), typeof(GenericNpcEntityData) })
                    TrialRepairLog.Patch(owner, type, "ShouldShowHealthBar",
                        new[] { typeof(Frame), typeof(EntityRef) },
                        typeof(HuskBossNameRepair), postfix: nameof(AfterOverheadHealth), returnType: typeof(bool));
                _errors = 0; _lastApplied = null; _lastHidden = null; _enabled = true;
                TrialRepairLog.Write("[husk-name] installed HUD name postfix; zone=" + HuskBossNamePolicy.ZoneGuid);
                TrialRepairLog.Write("[husk-overhead] installed exact-instance overhead health visibility postfixes");
            }
            catch { _enabled = false; owner.UnpatchSelf(); throw; }
        }

        private static void AfterOverheadHealth(Frame __0, EntityRef __1, ref bool __result)
        {
            if (!_enabled || !__result || __0 == null) return;
            try
            {
                if (!InfiniteDungeonAPI.IsCrucibleRunInProgress(__0) ||
                    InfiniteDungeonAPI.GetSourcePlaylist(__0).Id.Value != TrialRepairPolicy.PlaylistGuid ||
                    !BossTraceDropRepair.IsHuskInstance(__0, __1)) return;
                __result = false;
                string entity = __1.Raw.ToString();
                if (__0.IsVerified && entity != _lastHidden)
                {
                    _lastHidden = entity;
                    TrialRepairLog.Write("[husk-overhead] HIDDEN entity=" + entity);
                }
            }
            catch (Exception e)
            {
                if (++_errors <= 3) TrialRepairLog.Write("[husk-overhead] ERROR " + e.GetType().Name + ": " + e.Message);
                if (_errors >= 3) _enabled = false;
            }
        }

        private static void AfterUpdateHud(BossStatsView __instance, Frame __0)
        {
            if (!_enabled || __instance == null || __0 == null) return;
            try
            {
                if (!InfiniteDungeonAPI.IsCrucibleRunInProgress(__0) ||
                    InfiniteDungeonAPI.GetSourcePlaylist(__0).Id.Value != TrialRepairPolicy.PlaylistGuid) return;
                var strategy = __instance.m_currentViewStrategy;
                if (strategy == null || strategy.BossBattleZoneId != HuskBossNamePolicy.ZoneGuid) return;
                var groups = __instance.ViewElementGroups;
                if (groups == null) return;
                // Reused rooms may retain a tracked view. Refresh after the native HUD update
                // instead of relying on another SetTrackedBossEntity callback.
                for (int i = 0; i < groups.Count; i++)
                {
                    var label = groups[i]?.BossName;
                    if (label == null) continue;
                    string original = label.text;
                    string corrected = HuskBossNamePolicy.Resolve(strategy.BossBattleZoneId, true, original);
                    if (corrected == original) continue;
                    label.text = corrected;
                    string applied = strategy.BossBattleZoneId + ":" + corrected;
                    if (applied != _lastApplied)
                    {
                        _lastApplied = applied;
                        TrialRepairLog.Write("[husk-name] APPLIED zone=" + strategy.BossBattleZoneId +
                            "; original=" + original + "; name=" + corrected);
                    }
                }
            }
            catch (Exception e)
            {
                if (++_errors <= 3) TrialRepairLog.Write("[husk-name] ERROR " + e.GetType().Name + ": " + e.Message);
                if (_errors >= 3) _enabled = false;
            }
        }
    }
}
