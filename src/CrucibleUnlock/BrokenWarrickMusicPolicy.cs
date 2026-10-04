using System;

namespace CrucibleUnlock
{
    // Pure fingerprint policy: no Unity, IL2CPP, Harmony, or game state access.
    public static class BrokenWarrickMusicPolicy
    {
        public const string ScenePath = "Assets/worlds/infiniteDungeon/bosses/infiniteDungeonBossC/bossArena/warrickBossFight/interactives/interactives.unity";
        public static bool MatchesGuid(int a, int b, int c, int d) =>
            a == -2091225097 && b == 1266097816 && c == 2090557619 && d == -1408367959;

        public sealed class Candidate
        {
            public int Phase;
            public int PhaseTimelineCount;
            public string TimelineName;
            public bool GuidMatches;
            public string Scene;
            public bool Loop;
            public int RecordCount;
            public int FirstId;
            public int SecondId;
            public bool FirstIsMusicAnimator;
            public bool FirstClipIsNull;
            public float FirstVolume;
            public float FirstEndTime;
            public bool SecondItemIsNull;
            public int SecondEndEvent;
            public int SecondEndTarget;
            public float SecondEndTime;
        }

        public static bool HasIdentity(Candidate c) => c != null &&
            (c.GuidMatches || string.Equals(c.Scene, ScenePath, StringComparison.Ordinal));

        public static bool HasBrokenStructure(Candidate c) => c != null &&
            c.Phase >= 1 && c.PhaseTimelineCount == 1 &&
            string.Equals(c.TimelineName, "bossMusic", StringComparison.Ordinal) && c.Loop &&
            c.RecordCount == 2 && c.FirstId == 0 && c.SecondId == 1 &&
            c.FirstIsMusicAnimator && c.FirstClipIsNull && c.FirstVolume == 0f &&
            Math.Abs(c.FirstEndTime - 122.30530548095703f) < 0.001f &&
            c.SecondItemIsNull && c.SecondEndEvent == 2 && c.SecondEndTarget == -1 &&
            c.SecondEndTime == 0f;

        public static bool ShouldSkip(Candidate c) => HasIdentity(c) && HasBrokenStructure(c);
    }
}
