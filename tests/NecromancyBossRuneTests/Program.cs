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
              defaults[1] == new BossRuneRule("echo_knight", 1, 790324362694538264, true, 1, "echo_knight.png",
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
            Check(fallback && errors.Length == 1 && rules.SequenceEqual(defaults), "whole file fallback: " + invalid);
        }
        var custom = BossRunePolicy.Parse("/* table */[{\"key\":\"a_2\",\"slot\":255,\"npc\":\"42\",\"max_active\":5,\"enabled\":false,\"icon\":null,}," + Valid + ",]", out errors, out fallback);
        Check(!fallback && errors.Length == 0 && custom.Length == 2 && !custom[0].Enabled && custom[0].MaxActive == 5 && custom[0].Npc == 42 &&
              custom[0].Icon == "" && custom[1].Enabled && custom[1].MaxActive == 1, "comments, trailing commas, defaults and overrides");
        string ledger = BossRunePolicy.AddGrant("", "Hero A", "warrick");
        Check(ledger == "Hero A:warrick" && BossRunePolicy.AddGrant(ledger, "hero a", "WARRICK") == ledger, "grant add, no duplicate");
        Check(BossRunePolicy.HasGrant(ledger, " HERO A ", "Warrick") && !BossRunePolicy.HasGrant(ledger, "Hero B", "warrick") &&
              !BossRunePolicy.HasGrant(ledger, "Hero A", "echo_knight") && !BossRunePolicy.HasGrant("Hero A", "Hero A", "warrick"), "grant lookup exact, case-insensitive");
        string tomes = BossRunePolicy.AddGrant(ledger, "Hero A", BossRunePolicy.TomeGrantKey);
        Check(BossRunePolicy.HasGrant(tomes, "hero a", BossRunePolicy.TomeGrantKey) && !BossRunePolicy.HasGrant(ledger, "Hero A", BossRunePolicy.TomeGrantKey) &&
              !BossRunePolicy.HasGrant(tomes, "Hero B", BossRunePolicy.TomeGrantKey), "tome grant recorded per character, separate from rune grants");
        Check(BossRunePolicy.Parse("[{\"key\":\"@tome\",\"slot\":9,\"npc\":\"1\"}]", out _, out _).Length == 0, "tome grant key is not a valid rule key");
        ledger = BossRunePolicy.AddGrant(ledger, "Hero A", "echo_knight");
        Check(BossRunePolicy.RemoveGrant(ledger, "hero a", "ECHO_KNIGHT") == "Hero A:warrick" &&
              BossRunePolicy.RemoveGrant(ledger, "Hero B", "echo_knight") == ledger, "grant removal exact, case-insensitive");
        Check(NecromancyPolicy.DefaultMinionNpc == "native" && NecromancyPolicy.MinionNpcGuid(NecromancyPolicy.DefaultMinionNpc) == 0 &&
              NecromancyPolicy.MinionNpcGuid("warrick_boss") != 0, "native minion default, explicit boss still available");
        Console.WriteLine($"{_tests - _failures}/{_tests} boss rune checks passed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string label)
    {
        ++_tests;
        if (!ok) ++_failures;
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + label);
    }
}
