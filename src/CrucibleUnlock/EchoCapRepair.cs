using System;
using System.Collections.Generic;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    internal static class EchoCapRepair
    {
        private static readonly Dictionary<ulong, EntityRef> Changed = new Dictionary<ulong, EntityRef>();
        private static bool _enabled;
        private static int _errors;

        internal static void Install()
        {
            var owner = new PatchOwner("NRFW.CrucibleUnlock.SecondTrialEchoCap");
            try
            {
                TrialRepairLog.Patch(owner, typeof(InfiniteDungeonBasePlaylistRuleDirector), "OnPlayerRequestBoonScreenRoll",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(bool) }, typeof(EchoCapRepair), prefix: nameof(BeforeRoll));
                TrialRepairLog.Patch(owner, typeof(InfiniteDungeonBasePlaylistRuleDirector), "OnPlayerRequestBoonScreenRollSingle",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(int) }, typeof(EchoCapRepair), prefix: nameof(BeforeRoll));
                TrialRepairLog.Patch(owner, typeof(InfiniteDungeonAPI), "IsCrucibleRunInProgress",
                    new[] { typeof(Frame) }, typeof(EchoCapRepair), postfix: nameof(AfterRunStatus), returnType: typeof(bool));
                TrialRepairLog.Patch(owner, typeof(InfiniteDungeonAPI), "GetSourcePlaylist",
                    new[] { typeof(Frame) }, typeof(EchoCapRepair), postfix: nameof(AfterPlaylist),
                    returnType: typeof(AssetRefInfiniteDungeonPlaylist));
                _errors = 0;
                _enabled = true;
                TrialRepairLog.Write("[echo-cap] installed per-hero shop cap=" + EchoCapPolicy.SecondTrialLimit);
            }
            catch { _enabled = false; owner.UnpatchSelf(); throw; }
        }

        private static void BeforeRoll(Frame __0, EntityRef __1)
        {
            if (!_enabled || __0 == null || !__1.IsValid) return;
            try
            {
                bool active = InfiniteDungeonAPI.IsCrucibleRunInProgress(__0);
                long playlist = active ? InfiniteDungeonAPI.GetSourcePlaylist(__0).Id.Value : 0;
                int current = BoonsAPI.GetMaxBoons(__0, __1);
                if (!EchoCapPolicy.ShouldRaise(active, playlist, current)) return;
                BoonsAPI.SetMaxBoons(__0, __1, EchoCapPolicy.SecondTrialLimit);
                int readback = BoonsAPI.GetMaxBoons(__0, __1);
                if (readback != EchoCapPolicy.SecondTrialLimit)
                    throw new InvalidOperationException("hero cap readback=" + readback);
                Changed[__1.Raw] = __1;
                if (__0.IsVerified)
                    TrialRepairLog.Write("[echo-cap] HERO_APPLIED entity=" + __1.Raw + "; " + current + " -> " + readback + "; before shop roll");
            }
            catch (Exception e) { Error("apply", e); }
        }

        private static void Restore(Frame frame)
        {
            if (Changed.Count == 0) return;
            foreach (var hero in Changed.Values)
                if (EchoCapPolicy.ShouldRestore(BoonsAPI.GetMaxBoons(frame, hero)))
                    BoonsAPI.SetMaxBoons(frame, hero, EchoCapPolicy.NativeDefault);
            Changed.Clear();
            TrialRepairLog.Write("[echo-cap] restored per-hero cap outside second trial");
        }

        private static void AfterRunStatus(Frame __0, bool __result)
        {
            if (!_enabled || __0 == null || !__0.IsVerified || __result || Changed.Count == 0) return;
            try { Restore(__0); }
            catch (Exception e) { Error("restore/run", e); }
        }

        private static void AfterPlaylist(Frame __0, AssetRefInfiniteDungeonPlaylist __result)
        {
            if (!_enabled || __0 == null || !__0.IsVerified ||
                __result.Id.Value == TrialRepairPolicy.PlaylistGuid || Changed.Count == 0) return;
            try { Restore(__0); }
            catch (Exception e) { Error("restore/playlist", e); }
        }

        private static void Error(string stage, Exception e)
        {
            if (++_errors <= 3) TrialRepairLog.Write("[echo-cap] " + stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (_errors >= 3) { _enabled = false; TrialRepairLog.Write("[echo-cap] disabled after errors; restart required"); }
        }
    }
}
