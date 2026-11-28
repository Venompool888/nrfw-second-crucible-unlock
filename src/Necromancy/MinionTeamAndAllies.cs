using System;
using System.Collections.Generic;
using System.Threading;
using Il2CppQuantum;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // ---- minion team (T-006) ------------------------------------------------------------
        // Raised minions join the hero team (teamDataPlayers, HasFriendlyFire=true), so their hits on the
        // hero and the hero's on them go through friendly fire. teamDataPlayersNoFF is the shipped player
        // team without friendly fire. Only the minion's TeamComponent.Data is changed; damage code is untouched.

        private static readonly HashSet<ulong> TeamChecked = new();   // sim thread only
        private static string _teamMode = NecromancyPolicy.TeamModePlayersNoFF;

        private static unsafe long ReadTeam(Frame f, EntityRef e)
        {
            if (!e.IsValid || !EntityRefExtensions.Has<TeamComponent>(e, f)) return 0;
            var t = EntityRefExtensions.Get<TeamComponent>(e, f);
            return t == null ? 0 : t->Data.Id.Value;
        }

        private static unsafe void SetMinionTeam(Frame f, EntityRef owner, EntityRef minion)
        {
            long minionTeam = ReadTeam(f, minion), ownerTeam = ReadTeam(f, owner);
            long target = NecromancyPolicy.MinionTeamTarget(_teamMode, minionTeam);
            if (target == 0)
            {
                Write("minion-team keep entity=" + minion.Raw + " team=" + NecromancyPolicy.TeamName(minionTeam) + " owner=" + NecromancyPolicy.TeamName(ownerTeam) + " mode=" + _teamMode + " npc=" + MinionNpcText(f, minion));
                return;
            }
            var t = EntityRefExtensions.Get<TeamComponent>(minion, f);
            t->Data = new AssetRefTeamData { Id = new AssetGuid { Value = target } };
            Write("minion-team SET entity=" + minion.Raw + " " + NecromancyPolicy.TeamName(minionTeam) + " -> " + NecromancyPolicy.TeamName(ReadTeam(f, minion)) +
                  " (owner " + NecromancyPolicy.TeamName(ownerTeam) + ") npc=" + MinionNpcText(f, minion));
        }

        // ---- minion friendly fire ----------------------------------------------------------
        // Raised minions and heroes (or two minions) count as allies for damage. Hero-vs-hero keeps the
        // native result, so co-op/PvP friendly fire between players is untouched.

        private static int _allyLogs, _nativeAllyLogs;

        private static void InstallMinionFriendlyFire()
        {
            var ffHarmony = new HarmonyLib.Harmony("NRFW.Necromancy.MinionFriendlyFire");
            try
            {
                Patch(ffHarmony, typeof(TeamUtils), "AreAllies", new[] { typeof(Frame), typeof(EntityRef), typeof(EntityRef) },
                    nameof(AfterAreAllies), typeof(bool));
                Write("minion friendly-fire hook installed");
            }
            catch (Exception e) { ffHarmony.UnpatchSelf(); Write("minion friendly-fire hook FAILED " + e.GetType().Name + ": " + e.Message); }
            // 0.11.0 gated IgnoreDamageRequest(Frame, ref Damage) and GetFriendlyFireCoefficient (FP return); in game that broke
            // ALL player damage (no plugin log lines, so the detours themselves were at fault). Removed in 0.11.1; see T-006.
        }

        private static void AfterAreAllies(Frame __0, EntityRef __1, EntityRef __2, ref bool __result)
        {
            if (__result)
            {
                if (_nativeAllyLogs < 10 && __0 != null && __0.IsVerified && (Minions.ContainsKey(__1.Raw) || Minions.ContainsKey(__2.Raw)) &&
                    Interlocked.Increment(ref _nativeAllyLogs) <= 10)
                    Write("AreAllies native=true hit=" + __1.Raw + (Minions.ContainsKey(__1.Raw) ? "(minion)" : "") + " source=" + __2.Raw + (Minions.ContainsKey(__2.Raw) ? "(minion)" : ""));
                return;
            }
            if (!_enabled || __0 == null || !__1.IsValid || !__2.IsValid) return;
            try
            {
                if (!EntityRefExtensions.Has<RaisedMinionComponent>(__1, __0) &&
                    !EntityRefExtensions.Has<RaisedMinionComponent>(__2, __0)) return;
                var hit = ClassifyDamageActor(__0, __1);
                var source = ClassifyDamageActor(__0, __2);
                bool aMinion = hit.RaisedByPlayer, bMinion = source.RaisedByPlayer;
                bool aHero = hit.Player, bHero = source.Player;
                if (!NecromancyPolicy.MinionPairIsAllied(aMinion, aHero, bMinion, bHero)) return;
                __result = true;
                if (__0.IsVerified && Interlocked.Increment(ref _allyLogs) <= 20)
                    Write("minion allies override hit=" + __1.Raw + (aMinion ? "(minion)" : aHero ? "(hero)" : "") +
                          " source=" + __2.Raw + (bMinion ? "(minion)" : bHero ? "(hero)" : ""));
            }
            catch (Exception e) { Error("friendly-fire", e); }
        }
    }
}
