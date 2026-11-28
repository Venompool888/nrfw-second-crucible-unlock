using System;
using System.Linq;
using Necromancy;

// T-014 layer-1 tests: reaction-table parsing, matching priority, cooldown and pick. No game assemblies.
internal static class Program
{
    private static int _failures, _tests;

    private const long Bestial = 2126111680215882670;   // bestialPowerGestureData
    private const long Fisticuffs = 1770795828947168763; // fisticuffsGestureData
    private const long DelayedAoe = 4386907888524794362;
    private const long OverheadAoe = 1578638975510328898;
    private const long JumpSlam = 2630019745959013014;
    private const long Boss = NecromancyPolicy.WarrickBossNpc, Giant = NecromancyPolicy.WarrickEnemyNpc, Wildling = NecromancyPolicy.WildlingSwordNpc;

    private static int Main()
    {
        // ---- default table keeps T-012 ----
        Run("default JSON parses to exactly T-012's Warrick rule", () =>
        {
            var t = Parse(GestureReactions.DefaultJson(Bestial, DelayedAoe));
            Equal(0, t.Errors.Length);
            Equal(1, t.Rules.Length);
            var r = t.Rules[0];
            Assert(r.Npcs.SequenceEqual(new[] { Boss, Giant }), "who should be both Warrick GUIDs");
            Equal(Bestial, r.Gesture);
            Assert(r.Actions.SequenceEqual(new[] { DelayedAoe }), "action");
            Equal(ReactionPick.First, r.Pick);
            Equal(3, r.CooldownSeconds);
            Assert(!r.Any && r.Tiers.Length == 0 && r.Profiles.Length == 0, "no wider who keys");
        });
        Run("default JSON and built-in default behave the same for every minion and gesture", () =>
        {
            var file = Parse(GestureReactions.DefaultJson(Bestial, DelayedAoe));
            var builtIn = GestureReactions.Default(Bestial, DelayedAoe);
            foreach (long g in new[] { Bestial, Fisticuffs, 0L })
                foreach (long npc in new[] { Boss, Giant, Wildling, 0L })
                    foreach (string tier in new[] { "Boss", "Giant", "Normal", "" })
                    {
                        var a = GestureReactions.Match(file, g, npc, null, tier);
                        var b = GestureReactions.Match(builtIn, g, npc, null, tier);
                        Assert((a == null) == (b == null) && (a == null || a.Actions.SequenceEqual(b.Actions)), $"g={g} npc={npc} tier={tier}");
                    }
        });
        Run("default rule: Warrick reacts to bestial power only; the wildling never does", () =>
        {
            var t = GestureReactions.Default(Bestial, DelayedAoe);
            Assert(GestureReactions.Match(t, Bestial, Boss, null, "Boss") != null, "boss Warrick");
            Assert(GestureReactions.Match(t, Bestial, Giant, null, "Giant") != null, "giant Warrick");
            Assert(GestureReactions.Match(t, Bestial, Wildling, null, "Boss") == null, "wildling with a boss tier still has no rule");
            Assert(GestureReactions.Match(t, Fisticuffs, Boss, null, "Boss") == null, "other gesture");
            Assert(GestureReactions.Match(t, 0, Boss, null, "Boss") == null, "zero gesture");
            Assert(t.HasGesture(Bestial) && !t.HasGesture(Fisticuffs) && !t.HasGesture(0), "HasGesture");
        });
        Run("seed GUIDs come from roar_gesture/roar_action, keywords and junk fall back to T-012 defaults", () =>
        {
            Equal(123L, GestureReactions.SeedGuid(" 123 ", NecromancyPolicy.DefaultRoarGesture));
            Equal(Bestial, GestureReactions.SeedGuid("roar", NecromancyPolicy.DefaultRoarGesture));
            Equal(Bestial, GestureReactions.SeedGuid("", NecromancyPolicy.DefaultRoarGesture));
            Equal(DelayedAoe, GestureReactions.SeedGuid("-5", NecromancyPolicy.DefaultRoarAction));
            Equal(DelayedAoe, GestureReactions.SeedGuid(null, NecromancyPolicy.DefaultRoarAction));
        });

        // ---- matching priority ----
        const string Layered = @"{ ""rules"": [
            { ""who"": ""*"",              ""gesture"": ""2126111680215882670"", ""action"": ""1"" },
            { ""who"": ""tier:boss"",      ""gesture"": ""2126111680215882670"", ""action"": ""2"" },
            { ""who"": ""profile:warrick"", ""gesture"": ""2126111680215882670"", ""action"": ""3"" },
            { ""who"": ""2680182020363611965"", ""gesture"": ""2126111680215882670"", ""action"": ""4"" },
            { ""who"": ""2598229157892424643"", ""gesture"": ""1770795828947168763"", ""action"": ""5"" }
        ] }";
        Run("most specific key wins regardless of file order: NPC GUID > profile > tier > *", () =>
        {
            var t = Parse(Layered);
            Equal(4L, GestureReactions.Match(t, Bestial, Boss, "warrick", "Boss").Actions[0]);
            Equal(3L, GestureReactions.Match(t, Bestial, Giant, "warrick", "Boss").Actions[0]);
            Equal(2L, GestureReactions.Match(t, Bestial, Giant, null, "BOSS").Actions[0]);
            Equal(1L, GestureReactions.Match(t, Bestial, Giant, null, "Giant").Actions[0]);
            Equal(1L, GestureReactions.Match(t, Bestial, 0, null, "").Actions[0]);
        });
        Run("different gestures pick different rules for the same minion", () =>
        {
            var t = Parse(Layered);
            Equal(5L, GestureReactions.Match(t, Fisticuffs, Wildling, null, "Normal").Actions[0]);
            Equal(1L, GestureReactions.Match(t, Bestial, Wildling, null, "Normal").Actions[0]);
            Assert(GestureReactions.Match(t, Fisticuffs, Boss, null, "Boss") == null, "fisticuffs only for the wildling");
        });
        Run("equal specificity: the earlier rule wins", () =>
        {
            var t = Parse(@"{ ""rules"": [
                { ""who"": ""tier:boss"", ""gesture"": 2126111680215882670, ""action"": ""7"" },
                { ""who"": ""tier:boss"", ""gesture"": 2126111680215882670, ""action"": ""8"" } ] }");
            Equal(7L, GestureReactions.Match(t, Bestial, Boss, null, "Boss").Actions[0]);
        });
        Run("a who list mixes keys; each minion uses its most specific one", () =>
        {
            var t = Parse(@"{ ""rules"": [
                { ""who"": [""tier:elite"", ""2598229157892424643""], ""gesture"": ""2126111680215882670"", ""action"": ""9"" },
                { ""who"": ""tier:normal"", ""gesture"": ""2126111680215882670"", ""action"": ""10"" } ] }");
            Equal(9L, GestureReactions.Match(t, Bestial, Wildling, null, "Normal").Actions[0]);   // GUID beats the tier rule
            Equal(9L, GestureReactions.Match(t, Bestial, 42, null, "Elite").Actions[0]);
            Equal(10L, GestureReactions.Match(t, Bestial, 42, null, "Normal").Actions[0]);
            Assert(GestureReactions.Match(t, Bestial, 42, null, "Giant") == null, "no rule for a giant");
        });
        Run("profile rules match nothing while the minion has no profile (T-013 not merged)", () =>
        {
            var t = Parse(@"{ ""rules"": [ { ""who"": ""profile:warrick"", ""gesture"": ""2126111680215882670"", ""action"": ""3"" } ] }");
            Assert(t.UsesProfiles, "UsesProfiles");
            Assert(GestureReactions.Match(t, Bestial, Boss, null, "Boss") == null, "null profile");
            Assert(GestureReactions.Match(t, Bestial, Boss, "", "Boss") == null, "empty profile");
            Assert(GestureReactions.Match(t, Bestial, Boss, "WARRICK", "Boss") != null, "case-insensitive profile");
        });

        // ---- cooldown ----
        Run("cooldown is per rule seconds in 16.16 sim time; 0 never blocks; a reset clock never blocks", () =>
        {
            Assert(!GestureReactions.CooldownElapsed(0, (3L << 16) - 1, 3), "just before 3 s");
            Assert(GestureReactions.CooldownElapsed(0, 3L << 16, 3), "exactly 3 s");
            Assert(GestureReactions.CooldownElapsed(10L << 16, 10L << 16, 0), "0 s");
            Assert(GestureReactions.CooldownElapsed(50L << 16, 1L << 16, 3), "clock went backwards");
            Assert(!GestureReactions.CooldownElapsed(0, 599L << 16, 600), "600 s cap still counts");
        });

        // ---- pick ----
        Run("pick first always returns 0; single action always 0", () =>
        {
            for (int i = 0; i < 20; ++i)
            {
                Equal(0, GestureReactions.PickIndex(ReactionPick.First, 3, (ulong)i, i * 1000L, i));
                Equal(0, GestureReactions.PickIndex(ReactionPick.Random, 1, (ulong)i, i * 1000L, i));
                Equal(0, GestureReactions.PickIndex(ReactionPick.Cycle, 1, (ulong)i, i * 1000L, i));
            }
        });
        Run("pick cycle walks the list in order and wraps", () =>
        {
            var got = Enumerable.Range(0, 7).Select(n => GestureReactions.PickIndex(ReactionPick.Cycle, 3, 99, 0, n)).ToArray();
            Assert(got.SequenceEqual(new[] { 0, 1, 2, 0, 1, 2, 0 }), string.Join(",", got));
        });
        Run("pick random stays in range, is repeatable for the same input and uses every action", () =>
        {
            var seen = new bool[3];
            for (long t = 0; t < 200; ++t)
            {
                int i = GestureReactions.PickIndex(ReactionPick.Random, 3, 9223372084099418004UL, t << 16, 0);
                Assert(i >= 0 && i < 3, "range");
                Equal(i, GestureReactions.PickIndex(ReactionPick.Random, 3, 9223372084099418004UL, t << 16, 5));
                seen[i] = true;
            }
            Assert(seen.All(x => x), "every action picked at least once in 200 presses");
        });
        Run("pick, cooldown, mode and name parse", () =>
        {
            var t = Parse(@"{ ""version"": 1, ""rules"": [ { ""name"": "" boss-mix "", ""who"": ""tier:boss"", ""gesture"": ""2126111680215882670"",
                ""action"": [""4386907888524794362"", 1578638975510328898, ""2630019745959013014""], ""pick"": ""Cycle"", ""cooldown"": 0, ""mode"": ""force"" }, ] }");
            Equal(0, t.Errors.Length);
            var r = t.Rules[0];
            Equal("boss-mix", r.Name);
            Equal("#1(boss-mix)", r.Label);
            Assert(r.Actions.SequenceEqual(new[] { DelayedAoe, OverheadAoe, JumpSlam }), "three actions");
            Equal(ReactionPick.Cycle, r.Pick);
            Equal(0, r.CooldownSeconds);
        });

        // ---- inputs that must be rejected ----
        foreach (var (name, rule, why) in new[]
        {
            ("missing who", @"{ ""gesture"": ""1"", ""action"": ""2"" }", "missing who"),
            ("empty who list", @"{ ""who"": [], ""gesture"": ""1"", ""action"": ""2"" }", "who is empty"),
            ("unknown who form", @"{ ""who"": ""warrick"", ""gesture"": ""1"", ""action"": ""2"" }", "who \"warrick\""),
            ("negative NPC GUID", @"{ ""who"": ""-5"", ""gesture"": ""1"", ""action"": ""2"" }", "who \"-5\""),
            ("empty tier", @"{ ""who"": ""tier:"", ""gesture"": ""1"", ""action"": ""2"" }", "empty tier"),
            ("empty profile", @"{ ""who"": ""profile: "", ""gesture"": ""1"", ""action"": ""2"" }", "empty profile"),
            ("who is an object", @"{ ""who"": {}, ""gesture"": ""1"", ""action"": ""2"" }", "who:"),
            ("missing gesture", @"{ ""who"": ""*"", ""action"": ""2"" }", "missing gesture"),
            ("zero gesture", @"{ ""who"": ""*"", ""gesture"": ""0"", ""action"": ""2"" }", "gesture must"),
            ("keyword gesture", @"{ ""who"": ""*"", ""gesture"": ""roar"", ""action"": ""2"" }", "gesture must"),
            ("fractional gesture", @"{ ""who"": ""*"", ""gesture"": 1.5, ""action"": ""2"" }", "gesture must"),
            ("missing action", @"{ ""who"": ""*"", ""gesture"": ""1"" }", "missing action"),
            ("empty action list", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": [] }", "action is empty"),
            ("bad action in list", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": [""2"", ""x""] }", "action \"x\""),
            ("too many actions", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": [" + string.Join(",", Enumerable.Range(1, 17)) + "] }", "more than 16"),
            ("unknown pick", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""pick"": ""best"" }", "pick must"),
            ("negative cooldown", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""cooldown"": -1 }", "cooldown must"),
            ("fractional cooldown", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""cooldown"": 1.5 }", "cooldown must"),
            ("cooldown over cap", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""cooldown"": 601 }", "cooldown must"),
            ("cooldown as string", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""cooldown"": ""3"" }", "cooldown must"),
            ("prefer not implemented", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""mode"": ""prefer"" }", "prefer is not implemented"),
            ("unknown mode", @"{ ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"", ""mode"": ""gentle"" }", "mode must"),
            ("typo key", @"{ ""who"": ""*"", ""gestrue"": ""1"", ""action"": ""2"" }", "unknown key \"gestrue\""),
            ("non-string name", @"{ ""name"": 5, ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"" }", "name must"),
            ("rule is not an object", @"""x""", "must be an object"),
            // Review of PR #21: an unpaired surrogate escape parses as JSON but GetString() throws, which used to drop the whole file.
            ("unpaired surrogate in name", @"{ ""name"": ""\uD800"", ""who"": ""*"", ""gesture"": ""1"", ""action"": ""2"" }", "invalid text"),
            ("unpaired surrogate in who", @"{ ""who"": ""\uDC00"", ""gesture"": ""1"", ""action"": ""2"" }", "invalid text"),
        })
            Run("rejects " + name + ", keeps the valid rule next to it", () =>
            {
                var t = Parse(@"{ ""rules"": [ " + rule + @", { ""who"": ""*"", ""gesture"": ""9"", ""action"": ""9"" } ] }");
                Equal(1, t.Errors.Length);
                Assert(t.Errors[0].StartsWith("rule #1: ") && t.Errors[0].Contains(why), "error text: " + t.Errors[0]);
                Equal(1, t.Rules.Length);
                Equal(2, t.Rules[0].Index);
                Assert(!t.HasGesture(1), "rejected rule must not react");
            });
        foreach (var (name, json, why) in new[]
        {
            ("broken JSON", @"{ ""rules"": [ ", "not valid JSON"),
            ("top-level array", @"[ ]", "top level"),
            ("no rules", @"{ ""version"": 1 }", "missing \"rules\""),
            ("rules not an array", @"{ ""rules"": {} }", "missing \"rules\""),
            ("version 2", @"{ ""version"": 2, ""rules"": [] }", "unsupported version"),
            ("version as string", @"{ ""version"": ""1"", ""rules"": [] }", "unsupported version"),
            ("empty file", "", "not valid JSON"),
        })
            Run("whole file rejected: " + name, () =>
            {
                var t = GestureReactions.Parse(json, out string error);
                Assert(t == null, "table should be null");
                Assert(error != null && error.Contains(why), "file error: " + error);
            });
        Run("comments and trailing commas are allowed", () =>
        {
            var t = Parse("// header\n{ /* x */ \"rules\": [ { \"who\": \"*\", \"gesture\": \"1\", \"action\": \"2\", }, ], }");
            Equal(1, t.Rules.Length);
        });
        Run("an empty rules array loads and reacts to nothing", () =>
        {
            var t = Parse(@"{ ""rules"": [] }");
            Equal(0, t.Rules.Length);
            Assert(!t.HasGesture(Bestial) && GestureReactions.Match(t, Bestial, Boss, null, "Boss") == null, "nothing reacts");
        });
        Run("GUID text: plain positive decimal only", () =>
        {
            Assert(GestureReactions.TryGuid(" 2126111680215882670 ", out long g) && g == Bestial, "trimmed");
            Assert(!GestureReactions.TryGuid("+5", out _) && !GestureReactions.TryGuid("0", out _) && !GestureReactions.TryGuid("1e5", out _) &&
                   !GestureReactions.TryGuid("0x10", out _) && !GestureReactions.TryGuid("99999999999999999999", out _) && !GestureReactions.TryGuid(null, out _),
                   "rejects sign, zero, exponent, hex, overflow, null");
        });

        Console.WriteLine(_failures == 0 ? $"PASS: {_tests}/{_tests} reaction tests" : $"FAIL: {_failures} of {_tests} reaction tests");
        return _failures == 0 ? 0 : 1;
    }

    private static ReactionTable Parse(string json)
    {
        var t = GestureReactions.Parse(json, out string error);
        if (t == null) throw new Exception("file rejected: " + error);
        return t;
    }

    private static void Run(string name, Action body)
    {
        ++_tests;
        try { body(); Console.WriteLine("PASS: " + name); }
        catch (Exception e) { ++_failures; Console.WriteLine("FAIL: " + name + " -- " + e.Message); }
    }

    private static void Assert(bool ok, string what) { if (!ok) throw new Exception(what); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual);
    }
}
