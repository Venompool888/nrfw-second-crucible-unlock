using System;
using CrucibleUnlock;

internal static class RoomReusePolicyTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); ++checks; }
        long source = TrialRepairPolicy.PlaylistGuid;
        Check(RoomReusePolicy.IsNativeActiveState(0), "active run may retain native Locked state");
        Check(!RoomReusePolicy.IsNativeActiveState(1), "Generating is not active");
        Check(RoomReusePolicy.IsNativeActiveState(2), "Open is active");
        Check(!RoomReusePolicy.IsNativeActiveState(3), "Resetting is not active");
        Check(!RoomReusePolicy.IsNativeActiveState(4), "Finished is not active");
        Check(!RoomReusePolicy.IsNativeActiveState(-1), "unknown state is not active");
        Check(RoomReusePolicy.ShouldMaintainEntry(2, 2, true), "current active second room keeps its payload");
        Check(!RoomReusePolicy.ShouldMaintainEntry(3, 1, true), "restart cannot reuse cached floor three for native floor one");
        Check(!RoomReusePolicy.ShouldMaintainEntry(2, 3, true), "next transition cannot reapply previous floor payload");
        Check(!RoomReusePolicy.ShouldMaintainEntry(3, 3, false), "return to entrance cannot maintain old room payload");
        Check(!RoomReusePolicy.ShouldMaintainEntry(-1, -1, true), "unbound index cannot maintain payload");
        long[] rooms = { 10, 20, 20, 20, 20, 20, 20, 20, 30, 40 };
        long[] bossNuggets = { 0, 111, 222, 333, 444, 555, 666, 777, 0, 0 };
        for (int index = 2; index <= 7; ++index)
        {
            int selected = RoomReusePolicy.SelectEntry(true, true, source, index, rooms.Length, rooms[index - 1], rooms[index]);
            Check(selected == index && bossNuggets[selected] != bossNuggets[index - 1], "reused BossC selects its own generated entry " + index);
        }
        Check(RoomReusePolicy.SelectEntry(true, true, source, 1, 10, 10, 20) == -1, "first boss uses normal different-chunk path");
        Check(RoomReusePolicy.SelectEntry(true, true, source, 8, 10, 20, 30) == -1, "reward room uses native path");
        Check(RoomReusePolicy.SelectEntry(true, true, source, 10, 10, 20, 20) == -1, "completed run cannot index beyond list");
        Check(RoomReusePolicy.SelectEntry(true, true, source, -1, 10, 20, 20) == -1, "entrance/exit index rejected");
        Check(RoomReusePolicy.SelectEntry(false, true, source, 2, 10, 20, 20) == -1, "prediction frame unchanged");
        Check(RoomReusePolicy.SelectEntry(true, false, source, 2, 10, 20, 20) == -1, "inactive run unchanged");
        Check(RoomReusePolicy.SelectEntry(true, true, source + 1, 2, 10, 20, 20) == -1, "other playlists unchanged");
        Check(RoomReusePolicy.SelectEntry(true, true, source, 2, 10, 0, 0) == -1, "missing chunks rejected");
        Check(RoomReusePolicy.SelectNugget(50, new long[] { 60, 50, 70 }) == 1, "nugget chosen by resolved bucket, not list position");
        Check(RoomReusePolicy.SelectNugget(50, new long[] { 60, 70 }) == -1, "missing bucket content untouched");
        Check(RoomReusePolicy.SelectNugget(50, new long[] { 50, 50 }) == -1, "ambiguous bucket content untouched");
        Check(RoomReusePolicy.SelectNugget(0, new long[] { 0 }) == -1, "unresolved bucket rejected");

        // Regression sequence: old ID 9 survives floor-start, then actual reintegration completes with ID 10.
        Check(!RoomReusePolicy.ShouldRebind(true, false, 100, 9, 100, 9), "pending old integration does not reset state");
        Check(!RoomReusePolicy.ShouldRebind(true, false, 100, 9, 100, 10), "new ID must be integrated before rebinding");
        Check(RoomReusePolicy.ShouldRebind(true, true, 100, 9, 100, 10), "same streaming entity with new integration must rebind");
        Check(!RoomReusePolicy.ShouldRebind(true, true, 100, 10, 100, 10), "stable combat updates preserve peak count and clear timer");
        Check(!RoomReusePolicy.ShouldRebind(true, true, 100, -1, 100, 10), "native first resolution is not reset every frame");
        Check(RoomReusePolicy.ShouldRebind(true, true, 100, 10, 101, 10), "new streaming entity requires binding even with same ID");
        Check(!RoomReusePolicy.ShouldRebind(false, true, 100, 9, 100, 10), "unrelated room cannot be reset");
        Check(!RoomReusePolicy.ShouldRebind(true, true, 100, 9, 0, 10), "missing node cannot be bound");
        Check(!RoomReusePolicy.ShouldRebind(true, true, 100, 9, 100, -1), "unresolved integration cannot be bound");
        Console.WriteLine("PASS: " + checks + " room reuse selection/integration sequence checks; no game assemblies executed.");
    }
}
