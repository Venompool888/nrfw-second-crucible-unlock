namespace CrucibleUnlock
{
    internal static class BrokenVowBossPolicy
    {
        internal const long NpcGuid = 1859337488004007071L;
        internal const long InstanceGuid = -1736484318L;
        internal const long DirectorStrategyGuid = 3763531183571783452L;
        internal const int DirectorZoneGuid = 1129824205;
        internal const long HealthMultiplierNumerator = 3;
        internal const long HealthMultiplierDenominator = 2;

        internal static bool Matches(bool runActive, long playlistGuid, long instanceGuid) =>
            runActive && playlistGuid == TrialRepairPolicy.PlaylistGuid && instanceGuid == InstanceGuid;

        internal static bool MatchesDirector(bool runActive, long playlistGuid, long strategyGuid) =>
            runActive && playlistGuid == TrialRepairPolicy.PlaylistGuid && strategyGuid == DirectorStrategyGuid;

        internal static long ScaleHealthRaw(long raw)
        {
            if (raw <= 0 || raw > long.MaxValue / HealthMultiplierNumerator) return raw;
            return raw * HealthMultiplierNumerator / HealthMultiplierDenominator;
        }
    }
}
