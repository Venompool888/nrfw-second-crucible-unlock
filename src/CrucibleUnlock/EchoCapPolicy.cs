namespace CrucibleUnlock
{
    public static class EchoCapPolicy
    {
        public const int NativeDefault = 16;
        public const int SecondTrialLimit = 2000;

        public static bool ShouldRaise(bool active, long playlist, int current) =>
            active && playlist == TrialRepairPolicy.PlaylistGuid && current == NativeDefault;

        public static bool ShouldRestore(int current) => current == SecondTrialLimit;
    }
}
