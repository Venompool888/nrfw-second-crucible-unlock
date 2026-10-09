using System;
using System.Collections.Generic;
using System.Linq;
using CrucibleUnlock;
using MelonLoader;

internal static class Program
{
    private static int _failures;
    private static int _tests;
    private static readonly string[] ExpectedInstall =
    {
        "verify-build", "music-guard", "runtime-unlock", "phase2-repair",
        "progression-repair", "trace-drop", "broken-vow", "trace-pickup",
        "husk-name", "echo-cap", "bowgun-input", "ritual-animator", "ritual-view",
    };

    private static int Main()
    {
        Run("no configuration activates all accepted repairs with 300 traces", () =>
        {
            var mod = Initialize();
            mod.OnLateInitializeMelon();
            AssertRuntime(mod, 300);
        });
        Run("only boss trace quantity is exposed to player preferences", () =>
        {
            Initialize();
            Equal("boss_trace_drop_amount", string.Join(",", MelonPreferences.Registered));
        });
        foreach (string mode in new[] { "probe", "runtime-unlock", "unknown", "unlock", "unlock-dry", "" })
            Run("legacy mode '" + mode + "' and disabled repairs cannot block release", () =>
            {
                SeedLegacy(mode);
                var original = new Dictionary<string, object>(MelonPreferences.Existing);
                var mod = Initialize();
                mod.OnLateInitializeMelon();
                AssertRuntime(mod, 300);
                Assert(original.All(pair => Equals(MelonPreferences.Existing[pair.Key], pair.Value)), "legacy values were modified");
            });
        foreach (var test in new[]
        {
            (Input: 77, Expected: 77), (Input: 0, Expected: 0), (Input: 1000, Expected: 1000),
            (Input: -1, Expected: 300), (Input: 1001, Expected: 300),
            (Input: int.MinValue, Expected: 300), (Input: int.MaxValue, Expected: 300),
        })
            Run("trace amount " + test.Input + " resolves to " + test.Expected, () =>
            {
                SeedLegacy("runtime-unlock");
                MelonPreferences.Existing["boss_trace_drop_amount"] = test.Input;
                // A bad setting must reset a previously configured value, not retain it.
                BossTraceDropApi.SetAmount(12);
                var mod = Initialize();
                mod.OnLateInitializeMelon();
                AssertRuntime(mod, test.Expected);
                Equal(test.Input, MelonPreferences.Existing["boss_trace_drop_amount"]);
                Equal(test.Input < 0 || test.Input > 1000 ? 1 : 0, mod.LoggerInstance.Warnings.Count);
            });
        Run("build guard rejection installs no hooks even with legacy probe config", () =>
        {
            SeedLegacy("probe");
            Boundary.RejectBuild = true;
            var mod = Initialize();
            mod.OnLateInitializeMelon();
            Equal("verify-build", string.Join(",", Boundary.Calls));
            Assert(mod.LoggerInstance.Errors.Any(message => message.Contains("fixture build mismatch")), "build failure not reported");
            mod.OnLateUpdate();
            Assert(!Boundary.Calls.Contains("phase2-tick"), "phase2 repair activated after guard rejection");
        });
        Run("runtime installation failure rolls back music guard and skips later repairs", () =>
        {
            SeedLegacy("probe");
            Boundary.RejectRuntime = true;
            var mod = Initialize();
            mod.OnLateInitializeMelon();
            Equal("verify-build,music-guard,runtime-unlock,music-rollback", string.Join(",", Boundary.Calls));
            Assert(mod.LoggerInstance.Errors.Any(message => message.Contains("fixture runtime installation failed")), "install failure not reported");
        });
        Console.WriteLine("Player configuration tests: " + (_tests - _failures) + "/" + _tests + " passed (managed entry point only; no game DLL execution)");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        _tests++;
        Boundary.Reset();
        MelonPreferences.Existing.Clear();
        MelonPreferences.Registered.Clear();
        BossTraceDropApi.ResetToDefault();
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static ModMain Initialize()
    {
        var mod = new ModMain();
        mod.OnInitializeMelon();
        return mod;
    }
    private static void SeedLegacy(string mode)
    {
        MelonPreferences.Existing["mode"] = mode;
        foreach (string flag in new[] { "guard_broken_warrick_music", "repair_warrick_phase2_target", "repair_bossrush_progression", "repair_ritual_animator" })
            MelonPreferences.Existing[flag] = false;
        MelonPreferences.Existing["trace_boss_motion"] = true;
        MelonPreferences.Existing["motion_trace_directory"] = "invalid legacy directory";
        MelonPreferences.Existing["bowgun_weapon_class"] = -123;
        MelonPreferences.Existing["AnotherMod.setting"] = "keep me";
    }
    private static void AssertRuntime(ModMain mod, int traces)
    {
        Equal(string.Join(",", ExpectedInstall), string.Join(",", Boundary.Calls));
        Equal(30, Boundary.ConfiguredWeaponClass);
        Equal(traces, BossTraceDropApi.Amount);
        Equal(0, mod.LoggerInstance.Errors.Count);
        mod.OnLateUpdate();
        Assert(Boundary.Calls.Contains("phase2-tick"), "phase2 repair not active");
        Assert(!Boundary.Calls.Contains("motion-sample"), "detailed motion sampling unexpectedly active");
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { Assert(EqualityComparer<T>.Default.Equals(expected, actual), "expected " + expected + "; actual " + actual); }
}
