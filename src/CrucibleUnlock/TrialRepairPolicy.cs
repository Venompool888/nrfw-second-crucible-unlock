namespace CrucibleUnlock
{
    public static class TrialRepairPolicy
    {
        public const long PlaylistGuid = 1509816363606571268L;
        public const long OfferingGuid = 3277070870551820826L;
        public static bool ShouldSetFloor(bool verified, bool active, long playlist, int floor, int count) =>
            verified && active && playlist == PlaylistGuid && floor >= 0 && floor < count;
        public static bool ShouldRepairRitual(long action, bool boundHero, int matchingTracks, long expected, long actual) =>
            action == OfferingGuid && boundHero && matchingTracks == 1 && expected != 0 && expected != actual;
        public static bool ShouldRestore(long installed, long current) => installed != 0 && installed == current;
    }
}
