namespace CrucibleUnlock
{
    // Pure scope/identity decisions. No game types or persistent data are involved.
    internal static class RoomReusePolicy
    {
        // Native IsDirectorActive#52958 accepts Locked=0 and Open=2. The enum name
        // Locked does not mean an engaged run has stopped (see live-first-run.md).
        public static bool IsNativeActiveState(int dungeonState) => dungeonState == 0 || dungeonState == 2;

        public static bool ShouldMaintainEntry(int cachedIndex, int nativeCurrentIndex, bool playerInActiveRun) =>
            playerInActiveRun && cachedIndex >= 0 && cachedIndex == nativeCurrentIndex;

        public static int SelectEntry(bool verified, bool active, long playlist, int index, int count,
            long previousChunk, long selectedChunk)
        {
            if (!verified || !active || playlist != TrialRepairPolicy.PlaylistGuid ||
                index <= 0 || index >= count || selectedChunk == 0 || previousChunk != selectedChunk) return -1;
            return index;
        }

        public static int SelectNugget(long bucket, long[] candidateBuckets)
        {
            if (bucket == 0 || candidateBuckets == null) return -1;
            int found = -1;
            for (int i = 0; i < candidateBuckets.Length; ++i)
            {
                if (candidateBuckets[i] != bucket) continue;
                if (found >= 0) return -1; // Ambiguous generated content must not be guessed.
                found = i;
            }
            return found;
        }

        public static bool ShouldRebind(bool repeatedRoom, bool integratedAndReady,
            ulong boundEntity, int boundIntegration, ulong currentEntity, int currentIntegration)
        {
            if (!repeatedRoom || !integratedAndReady || currentEntity == 0 || currentIntegration < 0) return false;
            // A still-unresolved state on the correct entity belongs to native ResolveCurrentFloorIntegrationId.
            return boundEntity != currentEntity || (boundIntegration >= 0 && boundIntegration != currentIntegration);
        }
    }
}
