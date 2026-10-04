namespace CrucibleUnlock
{
    // Simulation coordinates only: no view, scene, floor, spawner or once-only cache.
    public static class WarrickPhase2TargetPolicy
    {
        public const long CoordinatorGuid = 3137259840216040318L;
        public const long LeapGuid = 2591609858920008649L;
        public const long OriginalX = 9481093L, OriginalY = 2752512L, OriginalZ = 6336020L;
        private const long MaximumCoordinate = 1000000L * 65536;
        private const long MinimumDistance = 100L * 65536;

        public static bool ShouldRepair(long actionGuid, long coordinatorGuid, bool directTarget,
            long targetX, long targetY, long targetZ, long positionX, long positionY, long positionZ)
        {
            if (actionGuid != LeapGuid || coordinatorGuid != CoordinatorGuid || !directTarget ||
                targetX != OriginalX || targetY != OriginalY || targetZ != OriginalZ ||
                !Valid(positionX) || !Valid(positionY) || !Valid(positionZ)) return false;

            // Exact arithmetic avoids rounding at the boundary and Int64 square overflow.
            decimal dx = (decimal)positionX - targetX;
            decimal dy = (decimal)positionY - targetY;
            decimal dz = (decimal)positionZ - targetZ;
            return dx * dx + dy * dy + dz * dz > (decimal)MinimumDistance * MinimumDistance;
        }

        private static bool Valid(long value) => value >= -MaximumCoordinate && value <= MaximumCoordinate;
    }
}
