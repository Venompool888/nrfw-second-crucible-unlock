using System;
using CrucibleUnlock;

internal static class WarrickPhase2TargetPolicyTests
{
    private sealed class Inputs
    {
        internal long Action = 2591609858920008649L, Coordinator = 3137259840216040318L;
        internal bool Direct = true;
        internal long X = 9481093, Y = 2752512, Z = 6336020;
        internal long PositionX = -237045392, PositionY = 3626107, PositionZ = -99619320;
    }
    private static bool Repair(Inputs i) => WarrickPhase2TargetPolicy.ShouldRepair(i.Action, i.Coordinator, i.Direct,
        i.X, i.Y, i.Z, i.PositionX, i.PositionY, i.PositionZ);
    private static int _checks;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: phase target: " + name); ++_checks; }
    private static void Reject(string name, Action<Inputs> change)
    { var i = new Inputs(); change(i); Check(!Repair(i), name); }

    internal static void Run()
    {
        _checks = 0;
        Check(Repair(new Inputs()), "executing trial leap with wrong fixed target repaired");
        Check(Repair(new Inputs()), "repeated encounter eligible after previous execution");
        var relocated = new Inputs { PositionX = 600 * 65536, PositionY = 7 * 65536, PositionZ = 900 * 65536 };
        Check(Repair(relocated), "different scene/instance uses own position without arena restriction");
        Reject("different leap action untouched", i => ++i.Action);
        Reject("different coordinator untouched", i => ++i.Coordinator);
        Reject("entity-targeted leap untouched", i => i.Direct = false);
        Reject("other target X preserved", i => ++i.X);
        Reject("other target Y preserved", i => ++i.Y);
        Reject("other target Z preserved", i => ++i.Z);
        Reject("ordinary arena near correct original target preserved", i =>
        { i.PositionX = i.X + 10 * 65536; i.PositionY = i.Y; i.PositionZ = i.Z; });
        Reject("native in-place target preserved", i =>
        { i.X = i.PositionX; i.Y = i.PositionY; i.Z = i.PositionZ; });
        Reject("invalid extreme negative position", i => i.PositionX = long.MinValue);
        Reject("invalid extreme positive position", i => i.PositionZ = long.MaxValue);
        Reject("invalid world Y", i => i.PositionY = 1000000L * 65536 + 1);
        var edge = new Inputs { PositionX = 9481093 + 100 * 65536, PositionY = 2752512, PositionZ = 6336020 };
        Check(!Repair(edge), "100m exact boundary left alone");
        ++edge.PositionX;
        Check(Repair(edge), "100m plus one raw unit recognized without rounding");
        edge = new Inputs { PositionX = 9481093 + 60 * 65536, PositionY = 2752512, PositionZ = 6336020 + 80 * 65536 };
        Check(!Repair(edge), "3D distance at boundary left alone");
        ++edge.PositionY;
        Check(Repair(edge), "squared distance keeps one raw unit beyond boundary");
        Console.WriteLine("PASS: " + _checks + " per-execution phase target policy checks; no game assemblies loaded.");
    }
}
