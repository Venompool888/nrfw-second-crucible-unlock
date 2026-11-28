using System;
using System.Collections.Generic;
using System.Linq;
using Necromancy;

// T-013 layer-1 tests: profile parsing, tier/override selection, default stuck mode, per-minion stuck hold.
// No game assemblies, so they also run where vendor/generated is missing. NecromancyHookAudit repeats the
// parser/selection checks next to the interop metadata checks.
internal static class Program
{
    private static int _failures, _tests;
    private const long S = 1L << 16;   // one second / one meter in 16.16 fixed point

    private static int Main()
    {
        // ---- parser ----
        Run("all fields parse with invariant decimals and any key case", () =>
        {
            var errors = new List<string>();
            var p = NecromancyPolicy.ParseMinionProfile(" Teleport=60,follow_start=8,follow_stop=2.5, formation_radius=3,enemy_search_radius=15," +
                "follow_speed=1.5,avoid_radius=1,avoid_speed=0.75,stuck=OFFFOLLOW ", errors);
            Equal(new MinionProfile(60, 8, 2.5, 3, 15, 1.5, 1, 0.75, StuckHoldMode.OffFollow), p);
            Equal(0, errors.Count);
        });
        Run("unknown keys are ignored without an error (forward compatible)", () =>
        {
            var errors = new List<string>();
            Equal(new MinionProfile(FollowStart: 4), NecromancyPolicy.ParseMinionProfile("future_key=1,follow_start=4", errors));
            Equal(0, errors.Count);
        });
        Run("malformed values are reported once each and leave the field unset", () =>
        {
            var errors = new List<string>();
            var p = NecromancyPolicy.ParseMinionProfile("teleport=NaN,follow_start=-1,follow_stop=,stuck=bad,avoid_speed=1e100,missing_equals", errors);
            Equal(new MinionProfile(), p);
            Equal(6, errors.Count);
            Assert(errors.Any(e => e.Contains("stuck=bad")), "bad stuck mode named in the error");
        });
        Run("a later valid value wins; a later invalid value keeps the earlier one", () =>
        {
            var errors = new List<string>();
            Equal(new MinionProfile(TeleportDistance: 50, StuckHold: StuckHoldMode.Never),
                NecromancyPolicy.ParseMinionProfile("teleport=40,teleport=50,teleport=-3,stuck=vanilla,stuck=never", errors));
            Equal(1, errors.Count);
        });
        Run("empty and null text give an empty profile", () =>
        {
            var errors = new List<string>();
            Equal(new MinionProfile(), NecromancyPolicy.ParseMinionProfile("", errors));
            Equal(new MinionProfile(), NecromancyPolicy.ParseMinionProfile(null, errors));
            Equal(0, errors.Count);
        });

        // ---- overrides ----
        Run("overrides resolve decimal GUIDs, last entry wins, bad entries are reported", () =>
        {
            var errors = new List<string>();
            var o = NecromancyPolicy.ParseMinionProfileOverrides(
                "42:stuck=never; bad-guid:teleport=99; missing_colon; 0:teleport=1; 77:follow_speed=x,follow_stop=3; 42:teleport=50,stuck=never", errors);
            Equal(2, o.Count);
            Equal(new MinionProfile(TeleportDistance: 50, StuckHold: StuckHoldMode.Never), o[42]);
            Equal(new MinionProfile(FollowStop: 3), o[77]);
            Equal(4, errors.Count);
            Assert(errors.Any(e => e.Contains("npc:77") && e.Contains("follow_speed")), "field error names its NPC");
        });

        // ---- selection ----
        var boss = new MinionProfile(TeleportDistance: 60, StuckHold: StuckHoldMode.Never);
        var tiers = new Dictionary<string, MinionProfile>
        {
            ["boss"] = boss, ["giant"] = new(TeleportDistance: 30), ["elite"] = new(FollowStart: 6), ["normal"] = new(StuckHold: StuckHoldMode.Vanilla),
        };
        var overrides = new Dictionary<long, MinionProfile> { [NecromancyPolicy.WarrickBossNpc] = new(FollowSpeed: 2) };
        Run("an NPC override beats its tier and is named npc:<guid>", () =>
        {
            var (name, p) = NecromancyPolicy.SelectMinionProfile(NecromancyPolicy.WarrickBossNpc, 3, tiers, overrides);
            Equal("npc:" + NecromancyPolicy.WarrickBossNpc, name);
            Equal(new MinionProfile(FollowSpeed: 2), p);
        });
        Run("EnemyType Boss/Giant/Elite pick their tier", () =>
        {
            Equal(("boss", boss), NecromancyPolicy.SelectMinionProfile(NecromancyPolicy.WarrickEnemyNpc, 3, tiers, overrides));
            Equal(("giant", tiers["giant"]), NecromancyPolicy.SelectMinionProfile(NecromancyPolicy.WarrickEnemyNpc, 1, tiers, overrides));
            Equal(("elite", tiers["elite"]), NecromancyPolicy.SelectMinionProfile(5, 2, tiers, overrides));
        });
        Run("Normal, Critters, PlotEnemy and unknown types use normal", () =>
        {
            foreach (int t in new[] { 0, 4, 5, 99, -1 })
                Equal(("normal", tiers["normal"]), NecromancyPolicy.SelectMinionProfile(5, t, tiers, overrides));
        });
        Run("unresolved NpcData falls back to the normal profile, named default", () =>
        {
            Equal(("default", tiers["normal"]), NecromancyPolicy.SelectMinionProfile(null, null, tiers, overrides));
        });

        // ---- default stuck mode (fix on top of 190990c) ----
        Run("a profile without stuck= keeps the documented default offfollow, not vanilla", () =>
        {
            Equal(StuckHoldMode.OffFollow, NecromancyPolicy.StuckModeOf(new MinionProfile(TeleportDistance: 50)));
            Equal(StuckHoldMode.OffFollow, NecromancyPolicy.StuckModeOf(new MinionProfile()));
            Equal(StuckHoldMode.OffFollow, NecromancyPolicy.StuckModeOf(null));
            var errors = new List<string>();
            var o = NecromancyPolicy.ParseMinionProfileOverrides("123:teleport=50", errors);
            Equal(StuckHoldMode.OffFollow, NecromancyPolicy.StuckModeOf(o[123]));
        });
        Run("an explicit stuck= is kept", () =>
        {
            foreach (var m in new[] { StuckHoldMode.Vanilla, StuckHoldMode.OffFollow, StuckHoldMode.Never })
                Equal(m, NecromancyPolicy.StuckModeOf(new MinionProfile(StuckHold: m)));
        });

        // ---- per-minion stuck hold ----
        Run("offfollow is exactly the 0.16.1 rule", () =>
        {
            foreach (bool following in new[] { false, true })
            foreach (bool inRange in new[] { false, true })
            foreach (bool near in new[] { false, true })
            foreach (long held in new[] { 0L, 9 * S, 11 * S })
            foreach (int max in new[] { 0, 10 })
            foreach (long? hit in new long?[] { null, 19 * S, 10 * S })
                Equal(NecromancyPolicy.ShouldHoldStuckTeleport(following, inRange, near, held, max, 20 * S, hit),
                      NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.OffFollow, following, inRange, near, held, max, 20 * S, hit));
        });
        Run("never holds a following minion like an off-follow one (a recent hit extends past the cap)", () =>
        {
            Assert(!NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.OffFollow, true, true, false, 11 * S, 10, 20 * S, 19 * S), "offfollow: following capped at 10 s");
            Assert(NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.Never, true, true, false, 11 * S, 10, 20 * S, 19 * S), "never: recent hit extends");
            Assert(!NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.Never, true, true, false, 11 * S, 10, 20 * S, null), "never: no hit, past cap");
            Assert(NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.Never, true, true, false, 11 * S, 0, 20 * S, null), "never: 0 = unlimited off-follow rule");
            Assert(!NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.Never, true, false, false, 0, 0, 20 * S, 20 * S), "never: out of range is not held");
        });
        Run("vanilla never holds, even near the owner", () =>
        {
            foreach (bool following in new[] { false, true })
            foreach (bool near in new[] { false, true })
                Assert(!NecromancyPolicy.ShouldHoldStuck(StuckHoldMode.Vanilla, following, true, near, 0, 0, 20 * S, 20 * S), "vanilla held");
        });

        Console.WriteLine(_tests - _failures + "/" + _tests + " passed");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        _tests++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { _failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }

    private static void Assert(bool ok, string what) { if (!ok) throw new Exception(what); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual);
    }
}
