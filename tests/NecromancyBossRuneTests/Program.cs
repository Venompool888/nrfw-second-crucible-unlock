using System;
using System.Linq;
using Necromancy;

internal static class Program
{
    private static int _tests, _failures;
    private const string Valid = "{\"key\":\"one\",\"slot\":0,\"npc\":1}";

    private static int Main()
    {
        var defaults = BossRunePolicy.Parse(BossRunePolicy.DefaultJson, out var errors, out var fallback);
        Check(!fallback && errors.Length == 0 && defaults.Length == 2 &&
              defaults[0] == new BossRuneRule("warrick", 0, 2680182020363611965, true, 1, "warrick.png",
                  "沃里克召唤符文", "Rune of Warrick", "在 8 米内的尸体上召出沃里克为你作战（同时最多 1 个）。",
                  "Summon Warrick from a corpse within 8 meters to fight for you (maximum 1 active).") &&
              (defaults[1] with { Entrance = null }) == new BossRuneRule("echo_knight", 1, 790324362694538264, true, 1, "echo_knight.png",
                  "回声骑士召唤符文", "Rune of the Echo Knight", "在 8 米内的尸体上召出回声骑士为你作战（同时最多 1 个）。",
                  "Summon the Echo Knight from a corpse within 8 meters to fight for you (maximum 1 active)."), "two default Boss runes");
        Check((defaults[0].ItemGuid, defaults[0].ActionGuid, defaults[0].ItemDataGuid) ==
              (unchecked((long)0xC000543031350001UL), unchecked((long)0xC000543031350002UL), unchecked((long)0x8000543031350003UL)), "Warrick I/A/L");
        Check((defaults[1].ItemGuid, defaults[1].ActionGuid, defaults[1].ItemDataGuid) ==
              (unchecked((long)0xC000543031350011UL), unchecked((long)0xC000543031350012UL), unchecked((long)0x8000543031350013UL)), "Echo Knight I/A/L");
        foreach (var (field, value) in new[] { ("key", "\"one\""), ("slot", "0"), ("key", "\"UPPER\""), ("key", "\"a-b\""), ("key", "\"\""),
                     ("key", "\"\\uD800\""), ("slot", "-1"), ("slot", "256"), ("slot", "1.5"), ("slot", "null"), ("npc", "0"), ("npc", "-1"), ("npc", "1.5"),
                     ("max_active", "0"), ("max_active", "6"), ("max_active", "false"), ("enabled", "7"),
                     ("icon", "\"../unsafe.png\""), ("icon", "\"folder\\\\image.png\""), ("icon", "\"wrong.jpg\"") })
        {
            string row = "{\"key\":" + (field == "key" ? value : "\"two\"") + ",\"slot\":" + (field == "slot" ? value : "1") +
                         ",\"npc\":" + (field == "npc" ? value : "2") +
                         (field == "key" || field == "slot" || field == "npc" ? "" : ",\"" + field + "\":" + value) + "}";
            var rules = BossRunePolicy.Parse("[" + Valid + "," + row + ", {\"key\":\"three\",\"slot\":255,\"npc\":3}]", out errors, out fallback);
            Check(!fallback && errors.Length == 1 && errors[0].Contains(field) && rules.Select(r => r.Key).SequenceEqual(new[] { "one", "three" }),
                "reject row: " + field + "=" + value);
        }
        foreach (string invalid in new[] { "[", "{}", "null" })
        {
            var rules = BossRunePolicy.Parse(invalid, out errors, out fallback);
            Check(fallback && errors.Length == 1 && rules.Length == defaults.Length && rules.Zip(defaults).All(pair =>
                  (pair.First with { Entrance = pair.Second.Entrance }) == pair.Second && SameEntrance(pair.First.Entrance, pair.Second.Entrance)),
                  "whole file fallback: " + invalid);
        }
        var custom = BossRunePolicy.Parse("/* table */[{\"key\":\"a_2\",\"slot\":255,\"npc\":\"42\",\"max_active\":5,\"enabled\":false,\"icon\":null,}," + Valid + ",]", out errors, out fallback);
        Check(!fallback && errors.Length == 0 && custom.Length == 2 && !custom[0].Enabled && custom[0].MaxActive == 5 && custom[0].Npc == 42 &&
              custom[0].Icon == "" && custom[1].Enabled && custom[1].MaxActive == 1, "comments, trailing commas, defaults and overrides");
        string ledger = BossRunePolicy.AddGrant("", "Hero A", "warrick");
        Check(ledger == "Hero A:warrick" && BossRunePolicy.AddGrant(ledger, "hero a", "WARRICK") == ledger, "grant add, no duplicate");
        Check(BossRunePolicy.HasGrant(ledger, " HERO A ", "Warrick") && !BossRunePolicy.HasGrant(ledger, "Hero B", "warrick") &&
              !BossRunePolicy.HasGrant(ledger, "Hero A", "echo_knight") && !BossRunePolicy.HasGrant("Hero A", "Hero A", "warrick"), "grant lookup exact, case-insensitive");
        var healthDefaults = BossRunePolicy.Parse(BossRunePolicy.DefaultJson, out _, out _);
        Check(healthDefaults.All(r => r.HealthScale == 0.05 && r.HealthRaw == 3277), "default Boss health scale 0.05 (raw 3277), below shipped 0.1");
        Check(BossRunePolicy.Parse("[{\"key\":\"a\",\"slot\":3,\"npc\":\"1\"}]", out _, out _).Single().HealthScale == 0.05, "missing health_scale uses the default");
        Check(BossRunePolicy.Parse("[{\"key\":\"a\",\"slot\":3,\"npc\":\"1\",\"health_scale\":0.2}]", out _, out _).Single().HealthRaw == 13107, "explicit health_scale 0.2 -> raw 13107");
        foreach (string bad in new[] { "0", "0.005", "1.5", "-0.1", "\"0.05\"", "true" })
            Check(BossRunePolicy.Parse("[{\"key\":\"a\",\"slot\":3,\"npc\":\"1\",\"health_scale\":" + bad + "}]", out var badErrors, out _).Length == 0 && badErrors.Length == 1, "health_scale " + bad + " rejected");
        string tomes = BossRunePolicy.AddGrant(ledger, "Hero A", BossRunePolicy.TomeGrantKey);
        Check(BossRunePolicy.HasGrant(tomes, "hero a", BossRunePolicy.TomeGrantKey) && !BossRunePolicy.HasGrant(ledger, "Hero A", BossRunePolicy.TomeGrantKey) &&
              !BossRunePolicy.HasGrant(tomes, "Hero B", BossRunePolicy.TomeGrantKey), "tome grant recorded per character, separate from rune grants");
        Check(BossRunePolicy.Parse("[{\"key\":\"@tome\",\"slot\":9,\"npc\":\"1\"}]", out _, out _).Length == 0, "tome grant key is not a valid rule key");
        ledger = BossRunePolicy.AddGrant(ledger, "Hero A", "echo_knight");
        Check(BossRunePolicy.RemoveGrant(ledger, "hero a", "ECHO_KNIGHT") == "Hero A:warrick" &&
              BossRunePolicy.RemoveGrant(ledger, "Hero B", "echo_knight") == ledger, "grant removal exact, case-insensitive");
        Check(NecromancyPolicy.DefaultMinionNpc == "native" && NecromancyPolicy.MinionNpcGuid(NecromancyPolicy.DefaultMinionNpc) == 0 &&
              NecromancyPolicy.MinionNpcGuid("warrick_boss") != 0, "native minion default, explicit boss still available");
        const long echo = 790324362694538264, warrick = 2680182020363611965, second = 1L << 16, quarter = second / 4;
        var entrance = BossEntrancePolicy.DefaultFor(echo);
        var expected = new BossEntrance(true, 203502697017737707, 2 * second, new[] { 2918908058815642059, 2653262915339319819 }, true, true);
        var automatic = BossRunePolicy.Parse($"[{{\"key\":\"one\",\"slot\":0,\"npc\":{echo}}},{{\"key\":\"two\",\"slot\":1,\"npc\":{warrick}}}]", out errors, out fallback);
        Check(!fallback && errors.Length == 0 && SameEntrance(automatic[0].Entrance, expected) && automatic[1].Entrance == null,
            "missing entrance defaults only for Echo Knight");
        Check(SameEntrance(defaults[1].Entrance, entrance) && defaults[0].Entrance == null, "shipped entrance matches DefaultFor");
        var disabled = BossRunePolicy.Parse($"[{{\"key\":\"one\",\"slot\":0,\"npc\":{echo},\"entrance\":{{\"enabled\":false}}}}]", out errors, out fallback);
        Check(!fallback && errors.Length == 0 && disabled.Single().Entrance is { Enabled: false }, "enabled:false overrides default without minion_actions");
        var overrides = BossRunePolicy.Parse("[{\"key\":\"one\",\"slot\":0,\"npc\":1,\"entrance\":{\"player_action\":\"0\",\"player_action_seconds\":0.1,\"minion_actions\":[\"2\",3],\"invincible\":false,\"pause_ai\":false}}]", out errors, out fallback);
        Check(!fallback && errors.Length == 0 && SameEntrance(overrides.Single().Entrance,
            new BossEntrance(true, 0, 6554, new long[] { 2, 3 }, false, false)), "entrance overrides, mixed GUID forms and 16.16 seconds");
        foreach (var (field, json) in new[]
        {
            ("entrance", "null"), ("extra", "{\"enabled\":false,\"extra\":true}"),
            ("player_action", "{\"minion_actions\":[1],\"player_action\":\"bad\"}"),
            ("minion_actions", "{\"minion_actions\":[0]}"), ("minion_actions", "{\"minion_actions\":[]}"),
            ("minion_actions", "{\"minion_actions\":[1,2,3,4,5]}"), ("minion_actions", "{}"),
            ("player_action_seconds", "{\"minion_actions\":[1],\"player_action_seconds\":0}"),
            ("player_action_seconds", "{\"minion_actions\":[1],\"player_action_seconds\":11}"),
            ("player_action_seconds", "{\"minion_actions\":[1],\"player_action_seconds\":\"2\"}"),
            ("enabled", "{\"enabled\":1,\"minion_actions\":[1]}"),
            ("invincible", "{\"enabled\":false,\"invincible\":null}"), ("pause_ai", "{\"enabled\":false,\"pause_ai\":0}")
        })
        {
            var rules = BossRunePolicy.Parse("[" + Valid[..^1] + ",\"entrance\":" + json + "}]", out errors, out fallback);
            Check(!fallback && rules.Length == 1 && rules[0].Key == "one" &&
                rules[0].Entrance == null && errors.Length == 1 && errors[0].StartsWith("#1 entrance: ") &&
                errors[0]["#1 entrance: ".Length..].Contains(field), "reject entrance only: " + json);
        }

        const long start = 100 * second;
        EntranceRun Started(bool running = true)
        {
            var run = new EntranceRun(entrance, start);
            run.Start();
            run.Executed(start, running);
            return run;
        }
        var normal = new EntranceRun(entrance, start);
        Check(normal.Start() is (EntranceCommand.Execute, 0, _), "entrance starts with step 0");
        normal.Executed(start, true);
        Check(normal.Next(start + quarter - 1, true, false).Command == EntranceCommand.Wait, "entrance waits at least 0.25 seconds");
        Check(normal.Next(start + quarter, true, true).Command == EntranceCommand.Wait, "entrance waits while action is running");
        Check(normal.Next(start + second, true, false) is (EntranceCommand.Execute, 1, _), "entrance executes step 1 after step 0 stops");
        normal.Executed(start + second, true);
        Check(normal.Next(start + second + quarter, true, false) is (EntranceCommand.Finish, _, "done"), "entrance finishes after step 1 stops");
        Check(normal.Next(start, false, true) is (EntranceCommand.Finish, _, "done") && normal.Start().Command == EntranceCommand.Finish,
            "entrance finish is terminal");
        Check(Started(false).Next(start, true, false) is (EntranceCommand.Finish, _, "action 1 did not start"), "entrance handles failed start immediately");
        var timeout = Started();
        Check(timeout.Next(start + 20 * second, true, true).Command == EntranceCommand.Wait &&
            timeout.Next(start + 20 * second + 1, true, true) is (EntranceCommand.Finish, _, "action 1 timed out"), "entrance times out after 20 seconds");
        Check(Started().Next(start + 1, false, true) is (EntranceCommand.Finish, _, "minion gone"), "entrance handles minion loss");
        Check(Started().Next(start - 1, true, true) is (EntranceCommand.Finish, _, "clock went backwards"), "entrance handles clock reversal");
        Check(!normal.PlayerCutDue(start + 2 * second - 1) && normal.PlayerCutDue(start + 2 * second) && !normal.PlayerCutDue(start + 3 * second),
            "player cut is independent of finish and due exactly once");
        Check(!new EntranceRun(entrance with { PlayerAction = 0 }, start).PlayerCutDue(start + 10 * second), "no cut with no player action");
        Console.WriteLine($"{_tests - _failures}/{_tests} boss rune checks passed.");
        return _failures == 0 ? 0 : 1;
    }

    private static bool SameEntrance(BossEntrance a, BossEntrance b) => a == null || b == null ? a == b :
        (a with { MinionActions = b.MinionActions }) == b && a.MinionActions.SequenceEqual(b.MinionActions);

    private static void Check(bool ok, string label)
    {
        ++_tests;
        if (!ok) ++_failures;
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + label);
    }
}
