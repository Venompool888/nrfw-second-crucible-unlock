using System;
using CrucibleUnlock;

internal static class BrokenVowBossPolicyTests
{
    internal static void Run()
    {
        long playlist = TrialRepairPolicy.PlaylistGuid;
        if (!BrokenVowBossPolicy.Matches(true, playlist, BrokenVowBossPolicy.InstanceGuid) ||
            BrokenVowBossPolicy.Matches(false, playlist, BrokenVowBossPolicy.InstanceGuid) ||
            BrokenVowBossPolicy.Matches(true, playlist + 1, BrokenVowBossPolicy.InstanceGuid) ||
            BrokenVowBossPolicy.Matches(true, playlist, BrokenVowBossPolicy.InstanceGuid + 1) ||
            !BrokenVowBossPolicy.MatchesDirector(true, playlist, BrokenVowBossPolicy.DirectorStrategyGuid) ||
            BrokenVowBossPolicy.MatchesDirector(true, playlist, BrokenVowBossPolicy.DirectorStrategyGuid + 1))
            throw new Exception("Broken Vow scope mismatch");
        if (BrokenVowBossPolicy.ScaleHealthRaw(100L * 65536) != 150L * 65536 ||
            BrokenVowBossPolicy.ScaleHealthRaw(0) != 0 ||
            BrokenVowBossPolicy.ScaleHealthRaw(long.MaxValue) != long.MaxValue)
            throw new Exception("Broken Vow health scaling mismatch");
        Console.WriteLine("PASS: Broken Vow exact run/instance/director scope and fixed-point health scaling.");
    }
}
