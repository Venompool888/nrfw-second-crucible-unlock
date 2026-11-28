using System;
using Necromancy;

// Pure-C# regression tests for NecromancyPolicy (zero vendor/game DLLs).
internal static class Program
{
    private static int _tests, _failures;

    private static int Main()
    {
        // ---- version & constants ----
        Run("version and expected Steam build ID", () =>
        {
            Assert(NecromancyPolicy.Version == "0.19.1", "version expected 0.19.1, got " + NecromancyPolicy.Version);
            Assert(NecromancyPolicy.ExpectedSteamBuildId == "22928553", "buildid expected 22928553");
        });

        // ---- BuildId manifest parser ----
        Run("manifest parser accepts valid multiline Steam appmanifest", () =>
        {
            const string validAcf =
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1371980\"\n\t\"Universe\"\t\t\"1\"\n\t\"buildid\"\t\t\"22928553\"\n}\n";
            Assert(NecromancyPolicy.TryParseManifestBuildId(validAcf, out string id) && id == "22928553", "parsed buildid");
            Assert(NecromancyPolicy.IsExpectedSteamBuild(validAcf), "expected build accepted");
        });

        Run("manifest parser accepts valid single-line appmanifest", () =>
        {
            const string singleLine = "\"AppState\" { \"appid\" \"1371980\" \"buildid\" \"22928553\" }";
            Assert(NecromancyPolicy.TryParseManifestBuildId(singleLine, out string id) && id == "22928553", "single-line parsed");
            Assert(NecromancyPolicy.IsExpectedSteamBuild(singleLine), "single-line accepted");
        });

        Run("manifest parser handles escaped paths and ignores nested PrivateDepots buildid", () =>
        {
            const string realisticAcf =
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1371980\"\n\t\"LauncherPath\"\t\t\"C:\\\\Program Files\\\\Steam\\\\steam.exe\"\n\t\"buildid\"\t\t\"22928553\"\n\t// comment with \"buildid\" \"999\"\n\t\"PrivateDepots\"\n\t{\n\t\t\"branches\"\n\t\t{\n\t\t\t\"private_beta\"\n\t\t\t{\n\t\t\t\t\"buildid\"\t\t\"26053765\"\n\t\t\t}\n\t\t}\n\t}\n}\n";
            Assert(NecromancyPolicy.TryParseManifestBuildId(realisticAcf, out string id) && id == "22928553", "top-level buildid captured");
            Assert(NecromancyPolicy.IsExpectedSteamBuild(realisticAcf), "realistic ACF accepted");
        });

        Run("manifest parser rejects nested-only buildid, wrong buildid, and unbalanced braces", () =>
        {
            const string nestedOnly =
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1371980\"\n\t\"PrivateDepots\"\n\t{\n\t\t\"branches\"\n\t\t{\n\t\t\t\"private_beta\"\n\t\t\t{\n\t\t\t\t\"buildid\"\t\t\"22928553\"\n\t\t\t}\n\t\t}\n\t}\n}\n";
            Assert(!NecromancyPolicy.TryParseManifestBuildId(nestedOnly, out string nestedId) && nestedId == null, "nested-only buildid rejected");
            Assert(!NecromancyPolicy.IsExpectedSteamBuild(nestedOnly), "nested-only must fail expected check");

            foreach (string wrong in new[] { "22928554", "22928552", "0", "99999999" })
            {
                string acf = "\"AppState\"\n{\n\t\"buildid\"\t\t\"" + wrong + "\"\n}\n";
                Assert(NecromancyPolicy.TryParseManifestBuildId(acf, out string id) && id == wrong, "extracts wrong id " + wrong);
                Assert(!NecromancyPolicy.IsExpectedSteamBuild(acf), "must fail expected build check for " + wrong);
            }
            Assert(!NecromancyPolicy.IsExpectedSteamBuild("\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553\"\n}\n", "999"), "mismatched expected parameter fails");
            Assert(!NecromancyPolicy.IsExpectedSteamBuild("\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553\"\n}\n", ""), "empty expected parameter fails");

            foreach (string unbalanced in new[]
            {
                "\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553\"",
                "\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553\"\n}\n}",
                "\"AppState\" } \"buildid\" \"22928553\" {",
                "\"AppState\"\n{\n\t\"buildid\" { \"inner\" \"22928553\" }\n}",
            })
            {
                Assert(!NecromancyPolicy.TryParseManifestBuildId(unbalanced, out _), "unbalanced/block buildid rejected");
                Assert(!NecromancyPolicy.IsExpectedSteamBuild(unbalanced), "unbalanced/block buildid fails expected check");
            }
        });

        Run("manifest parser rejects missing field and malformed manifests", () =>
        {
            foreach (var (name, bad) in new[]
            {
                ("null", (string)null),
                ("empty", ""),
                ("whitespace", "   \r\n\t "),
                ("missing buildid", "\"AppState\"\n{\n\t\"appid\"\t\t\"1371980\"\n}\n"),
                ("empty buildid", "\"AppState\"\n{\n\t\"buildid\"\t\t\"\"\n}\n"),
                ("alphabetic buildid", "\"AppState\"\n{\n\t\"buildid\"\t\t\"abc\"\n}\n"),
                ("alphanumeric buildid", "\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553beta\"\n}\n"),
                ("negative buildid", "\"AppState\"\n{\n\t\"buildid\"\t\t\"-22928553\"\n}\n"),
                ("unclosed quote", "\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553\n}\n"),
                ("prefixed key", "\"AppState\"\n{\n\t\"not_buildid\"\t\t\"22928553\"\n}\n"),
                ("duplicate buildid", "\"AppState\"\n{\n\t\"buildid\"\t\t\"22928553\"\n\t\"buildid\"\t\t\"22928553\"\n}\n"),
                ("plain text", "buildid 22928553"),
            })
            {
                Assert(!NecromancyPolicy.TryParseManifestBuildId(bad, out string parsed) && parsed == null,
                    "TryParseManifestBuildId must fail for " + name);
                Assert(!NecromancyPolicy.IsExpectedSteamBuild(bad),
                    "IsExpectedSteamBuild must fail for " + name);
            }
        });

        // ---- ShouldInspectDamage fast-path ----
        Run("ShouldInspectDamage skips only when all three trackers are empty", () =>
        {
            Assert(!NecromancyPolicy.ShouldInspectDamage(false, false, false), "empty session skips inspection");
            Assert(NecromancyPolicy.ShouldInspectDamage(true, false, false), "active minion inspects");
            Assert(NecromancyPolicy.ShouldInspectDamage(false, true, false), "retired damage owner inspects");
            Assert(NecromancyPolicy.ShouldInspectDamage(false, false, true), "teleport minion inspects");
            Assert(NecromancyPolicy.ShouldInspectDamage(true, true, false), "minion + owner inspects");
            Assert(NecromancyPolicy.ShouldInspectDamage(true, false, true), "minion + teleport inspects");
            Assert(NecromancyPolicy.ShouldInspectDamage(false, true, true), "owner + teleport inspects");
            Assert(NecromancyPolicy.ShouldInspectDamage(true, true, true), "all active inspects");
        });

        // ---- Feature fault isolation ----
        Run("FeatureFaultGuard disables only after maxErrors and resets cleanly", () =>
        {
            var diag = new NecromancyPolicy.FeatureFaultGuard(5);
            bool coreEnabled = true;

            for (int i = 1; i <= 4; ++i)
            {
                bool log = diag.RecordError(out bool justDisabled);
                Assert(log && !justDisabled && !diag.IsDisabled && diag.ErrorCount == i, "error #" + i + " keeps feature enabled");
                Assert(coreEnabled, "core remains enabled");
            }

            bool log5 = diag.RecordError(out bool justDisabled5);
            Assert(log5 && justDisabled5 && diag.IsDisabled && diag.ErrorCount == 5, "error #5 trips feature guard");
            Assert(coreEnabled, "tripping feature guard never disables core");

            bool log6 = diag.RecordError(out bool justDisabled6);
            Assert(!log6 && !justDisabled6 && diag.IsDisabled && diag.ErrorCount == 6, "error #6 stays disabled and suppresses log spam");

            diag.Reset();
            Assert(!diag.IsDisabled && diag.ErrorCount == 0, "reset restores feature guard");
        });

        Run("FeatureFaultGuard supports immediate trip and rejects invalid maxErrors", () =>
        {
            var teleportHold = new NecromancyPolicy.FeatureFaultGuard(1);
            Assert(teleportHold.RecordError(out bool tripped) && tripped && teleportHold.IsDisabled, "maxErrors=1 trips on first error");
            Assert(!teleportHold.RecordError(out bool second) && !second, "subsequent error stays disabled");

            foreach (int invalid in new[] { 0, -1, -100 })
            {
                bool threw = false;
                try { _ = new NecromancyPolicy.FeatureFaultGuard(invalid); }
                catch (ArgumentOutOfRangeException) { threw = true; }
                Assert(threw, "maxErrors=" + invalid + " must throw ArgumentOutOfRangeException");
            }
        });

        // ---- Existing NecromancyPolicy rules ----
        Run("minion team target only moves Players -> PlayersNoFF in players_noff mode", () =>
        {
            Assert(NecromancyPolicy.MinionTeamTarget("players_noff", NecromancyPolicy.TeamPlayers) == NecromancyPolicy.TeamPlayersNoFF, "moves Players");
            Assert(NecromancyPolicy.MinionTeamTarget("players_noff", NecromancyPolicy.TeamPlayersNoFF) == 0, "already PlayersNoFF");
            Assert(NecromancyPolicy.MinionTeamTarget("players_noff", NecromancyPolicy.TeamEnemies) == 0, "enemy team untouched");
            Assert(NecromancyPolicy.MinionTeamTarget("players_noff", 0) == 0, "zero team untouched");
            Assert(NecromancyPolicy.MinionTeamTarget("native", NecromancyPolicy.TeamPlayers) == 0, "native mode untouched");
        });

        Run("rune compatibility accepts only TomeOfHealing/classless with necromancy runes", () =>
        {
            foreach (long r in new[] { NecromancyPolicy.RaiseMinionRune, NecromancyPolicy.ReviveRune, NecromancyPolicy.HealMinionsRune })
            {
                Assert(NecromancyPolicy.IsNecromancyRune(r), "necro rune " + r);
                Assert(NecromancyPolicy.TomeAccepts(NecromancyPolicy.TomeOfHealing, r), "tome accepts " + r);
                Assert(NecromancyPolicy.ClasslessAccepts(0, r), "classless accepts " + r);
                Assert(!NecromancyPolicy.TomeAccepts(NecromancyPolicy.ReleasedHealRune, r), "non-tome rejected");
                Assert(!NecromancyPolicy.ClasslessAccepts(25, r), "classed weapon rejected");
            }
            Assert(!NecromancyPolicy.IsNecromancyRune(NecromancyPolicy.ReleasedHealRune), "released heal not in necro family");
            Assert(!NecromancyPolicy.TomeAccepts(NecromancyPolicy.TomeOfHealing, NecromancyPolicy.ReleasedHealRune), "tome rejects unrelated rune");
            Assert(!NecromancyPolicy.ClasslessAccepts(0, 0), "zero rune rejected");
        });

        Run("slot layout guard validates sizes, bounds, alignment and distinct field offsets", () =>
        {
            Assert(NecromancyPolicy.SlotLayoutOk(16, 16, 72, 12, 8, 4, 0, 8, 12), "valid 4-slot layout");
            Assert(!NecromancyPolicy.SlotLayoutOk(12, 12, 72, 12, 8, 4, 0, 4, 8), "bad slotSize rejected");
            Assert(!NecromancyPolicy.SlotLayoutOk(16, 20, 72, 12, 8, 4, 0, 8, 12), "managedSlot mismatch rejected");
            Assert(!NecromancyPolicy.SlotLayoutOk(16, 16, 71, 12, 8, 4, 0, 8, 12), "buffer overflow rejected");
            Assert(!NecromancyPolicy.SlotLayoutOk(16, 16, 72, 12, 8, 0, 0, 8, 12), "zero count rejected");
            Assert(!NecromancyPolicy.SlotLayoutOk(16, 16, 72, 12, 8, 4, 0, 0, 12), "overlapping offsets rejected");
            Assert(!NecromancyPolicy.SlotLayoutOk(16, 16, 72, 12, 8, 4, 1, 8, 12), "unaligned offset rejected");
        });

        Run("minion lifetime 16.16 fixed point and boundary checks", () =>
        {
            Assert(!NecromancyPolicy.LifetimeExpired(0, (120L << 16) - 1, 120), "1 tick before expiry");
            Assert(NecromancyPolicy.LifetimeExpired(0, 120L << 16, 120), "exact expiry");
            Assert(NecromancyPolicy.LifetimeExpired(10L << 16, 130L << 16, 120), "offset birth expiry");
            Assert(!NecromancyPolicy.LifetimeExpired(0, 1000L << 16, 0), "0 disables lifetime");
            Assert(!NecromancyPolicy.LifetimeExpired(0, 1000L << 16, -5), "negative disables lifetime");
            Assert(!NecromancyPolicy.LifetimeExpired(50L << 16, 10L << 16, 120), "backwards clock never expires");
        });

        Run("teleport distance scaling and stuck-teleport hold rules", () =>
        {
            Assert(NecromancyPolicy.ScaledTeleportRaw(20L << 16, 3f) == 60L << 16, "3x scaling");
            Assert(NecromancyPolicy.ScaledTeleportRaw(20L << 16, 0f) == 20L << 16, "0 keeps native");
            Assert(NecromancyPolicy.ScaledTeleportRaw(20L << 16, float.NaN) == 20L << 16, "NaN keeps native");
            Assert(NecromancyPolicy.ScaledTeleportRaw(20L << 16, 100f) == 1000L << 16, "capped at 50x");

            Assert(NecromancyPolicy.InHoldRange(60, 8, 60, 8), "exact range boundary passes");
            Assert(!NecromancyPolicy.InHoldRange(60.1, 0, 60, 8), "horiz over maxDist rejected");
            Assert(!NecromancyPolicy.InHoldRange(10, -8.1, 60, 8), "dy over maxDy rejected");
            Assert(NecromancyPolicy.InHoldRange(10, 50, 60, 0), "maxDy=0 uncaps height");

            Assert(NecromancyPolicy.ShouldHoldStuckTeleport(true, false, true, 99L << 16, 10, 100L << 16, null), "near always holds");
            Assert(!NecromancyPolicy.ShouldHoldStuckTeleport(false, false, false, 0, 0, 20L << 16, 20L << 16), "out of range never holds");
            Assert(NecromancyPolicy.ShouldHoldStuckTeleport(true, true, false, 9L << 16, 10, 20L << 16, null), "following under cap holds");
            Assert(!NecromancyPolicy.ShouldHoldStuckTeleport(true, true, false, 10L << 16, 10, 20L << 16, 20L << 16), "following past cap releases even with hit");
            Assert(NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 15L << 16, 10, 20L << 16, 15L << 16), "off-follow recent hit extends hold");
            Assert(!NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 15L << 16, 10, 20L << 16, 14L << 16), "off-follow stale hit releases");
        });

        Console.WriteLine(_failures == 0
            ? $"PASS: {_tests}/{_tests} core policy checks passed."
            : $"FAIL: {_failures} of {_tests} core policy checks failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action body)
    {
        ++_tests;
        try { body(); Console.WriteLine("PASS: " + name); }
        catch (Exception e) { ++_failures; Console.WriteLine("FAIL: " + name + " -- " + e.Message); }
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception(what);
    }
}
