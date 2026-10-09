using System;
using CrucibleUnlock;

internal static class EchoCapPolicyTests
{
    public static void Run()
    {
        long target = TrialRepairPolicy.PlaylistGuid;
        if (!EchoCapPolicy.ShouldRaise(true, target, 16) ||
            EchoCapPolicy.ShouldRaise(false, target, 16) ||
            EchoCapPolicy.ShouldRaise(true, target + 1, 16) ||
            EchoCapPolicy.ShouldRaise(true, target, 20) ||
            EchoCapPolicy.ShouldRaise(true, target, 2000) ||
            !EchoCapPolicy.ShouldRestore(2000) || EchoCapPolicy.ShouldRestore(16))
            throw new Exception("FAIL second-trial echo cap scope");
        Console.WriteLine("PASS: second-trial echo cap scope and restoration policy.");
    }
}
