using System;
using CrucibleUnlock;

internal static class BossTraceDropTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL: " + message);
            checks++;
        }
        Check(BossTraceDropApi.DefaultAmount == 300 && BossTraceDropApi.Amount == 300, "approved default is 300 traces");
        Check(BossTraceDropPolicy.ShouldOverride(true, TrialRepairPolicy.PlaylistGuid, 0, true), "second trial boss traces override");
        Check(!BossTraceDropPolicy.ShouldOverride(false, TrialRepairPolicy.PlaylistGuid, 0, true), "inactive run passes through");
        Check(!BossTraceDropPolicy.ShouldOverride(true, 123, 0, true), "other playlists pass through");
        foreach (int resource in new[] { 1, 2, 3, -1, 4 })
            Check(!BossTraceDropPolicy.ShouldOverride(true, TrialRepairPolicy.PlaylistGuid, resource, true), "only traces may change");
        Check(!BossTraceDropPolicy.ShouldOverride(true, TrialRepairPolicy.PlaylistGuid, 0, false), "ordinary enemies pass through");
        BossTraceDropApi.SetAmount(75);
        Check(BossTraceDropApi.Amount == 75, "external API affects next calculation");
        BossTraceDropApi.SetAmount(0);
        Check(BossTraceDropApi.Amount == 0, "zero disables boss traces");
        BossTraceDropApi.SetAmount(BossTraceDropApi.MaximumAmount);
        Check(BossTraceDropApi.Amount == BossTraceDropApi.MaximumAmount, "maximum accepted");
        foreach (int invalid in new[] { -1, int.MinValue, BossTraceDropApi.MaximumAmount + 1, int.MaxValue })
        {
            bool rejected = false;
            try { BossTraceDropApi.SetAmount(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && BossTraceDropApi.Amount == BossTraceDropApi.MaximumAmount, "invalid input rejected without losing prior value");
        }
        BossTraceDropApi.ResetToDefault();
        Check(BossTraceDropApi.Amount == 300, "external reset restores approved default");
        Check(BossTraceDropPolicy.ToRawAmount(300) == 19660800L, "300 uses exact Quantum fixed point units");
        Check(BossTraceDropPolicy.ToRawAmount(0) == 0, "zero fixed point amount");
        Console.WriteLine("PASS: " + checks + " boss trace scope, public API and fixed point checks.");
    }
}
