using System;

namespace CrucibleUnlock
{
    internal enum ThirdTrialSpawnDecision { Pass, Replace, Block }

    // Pure scope gate. The frame-facing hook gathers these values from the spawner's
    // own integration group, rather than the player's current floor (which can lag
    // behind the room currently being preloaded).
    internal static class ThirdTrialSpawningPolicy
    {
        internal static ThirdTrialSpawnDecision Decide(long sourcePlaylist, long privatePlaylist,
            long chunk, long expectedChunk, long bucket, long expectedBucket,
            long nugget, long expectedNugget, int integrationId, bool integrationMapsToOwner,
            long spawner, long[] allowedSpawners, bool generatedEntryMatches)
        {
            if (sourcePlaylist != privatePlaylist || chunk != expectedChunk)
                return ThirdTrialSpawnDecision.Pass;
            if ((bucket != 0 && bucket != expectedBucket) ||
                (nugget != 0 && nugget != expectedNugget) ||
                integrationId <= 0 || !integrationMapsToOwner ||
                !generatedEntryMatches || allowedSpawners == null ||
                Array.IndexOf(allowedSpawners, spawner) < 0)
                return ThirdTrialSpawnDecision.Block;
            return ThirdTrialSpawnDecision.Replace;
        }

        internal static bool SameNuggets(long[] expected, long[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length || expected.Length == 0)
                return false;
            var consumed = new bool[actual.Length];
            foreach (long wanted in expected)
            {
                bool found = false;
                for (int i = 0; i < actual.Length; ++i)
                    if (!consumed[i] && actual[i] == wanted) { consumed[i] = true; found = true; break; }
                if (!found) return false;
            }
            return true;
        }
    }
}
