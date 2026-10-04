using System;
using CrucibleUnlock;

internal static class TrialRepairPolicyTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); ++checks; }
        long p = TrialRepairPolicy.PlaylistGuid, a = TrialRepairPolicy.OfferingGuid;
        Check(TrialRepairPolicy.ShouldSetFloor(true, true, p, 0, 8), "first BossRush floor receives native setup");
        Check(TrialRepairPolicy.ShouldSetFloor(true, true, p, 1, 8), "next repeated BossC receives fresh setup");
        Check(!TrialRepairPolicy.ShouldSetFloor(false, true, p, 0, 8), "prediction must not reset verified clear state");
        Check(!TrialRepairPolicy.ShouldSetFloor(true, false, p, 0, 8), "inactive director ignored");
        Check(!TrialRepairPolicy.ShouldSetFloor(true, true, 1485587607739743779L, 0, 10), "first trial unchanged");
        Check(!TrialRepairPolicy.ShouldSetFloor(true, true, p, -1, 8), "entrance index ignored");
        Check(!TrialRepairPolicy.ShouldSetFloor(true, true, p, 8, 8), "past last floor ignored");
        Check(!TrialRepairPolicy.ShouldSetFloor(true, true, p, 0, 0), "empty runtime playlist ignored");
        Check(TrialRepairPolicy.ShouldRepairRitual(a, true, 1, 42, 0), "missing sacrifice animator binds to owning hero");
        Check(TrialRepairPolicy.ShouldRepairRitual(a, true, 1, 42, 43), "wrong actor reference binds to owning hero");
        Check(!TrialRepairPolicy.ShouldRepairRitual(a, true, 1, 42, 42), "working sacrifice reference unchanged");
        Check(!TrialRepairPolicy.ShouldRepairRitual(a + 1, true, 1, 42, 0), "other action unchanged");
        Check(!TrialRepairPolicy.ShouldRepairRitual(a, false, 1, 42, 0), "unbound owner ignored");
        Check(!TrialRepairPolicy.ShouldRepairRitual(a, true, 2, 42, 0), "ambiguous track ignored");
        Check(!TrialRepairPolicy.ShouldRepairRitual(a, true, 0, 42, 0), "missing track is not fabricated");
        Check(!TrialRepairPolicy.ShouldRepairRitual(a, true, 1, 0, 0), "missing hero animator ignored");
        Check(TrialRepairPolicy.ShouldRestore(42, 42), "own temporary reference restored");
        Check(!TrialRepairPolicy.ShouldRestore(42, 43), "concurrent replacement is preserved");
        Check(!TrialRepairPolicy.ShouldRestore(0, 0), "absent ownership cannot restore");
        Console.WriteLine("PASS: " + checks + " trial repair scope/restore checks; no game assemblies executed.");
    }
}
