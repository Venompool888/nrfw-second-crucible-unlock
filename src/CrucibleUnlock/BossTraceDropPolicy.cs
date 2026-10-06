namespace CrucibleUnlock
{
    internal static class BossTraceDropPolicy
    {
        // ViewSidePickupIntegerContentsType.CrucibleTrace = 0 in this guarded build.
        internal static bool ShouldOverride(bool active, long playlist, int resource, bool boss) =>
            active && playlist == TrialRepairPolicy.PlaylistGuid && resource == 0 && boss;

        internal static long ToRawAmount(int amount) => (long)amount * 65536L;
    }
}
