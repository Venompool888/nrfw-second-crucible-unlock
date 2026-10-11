namespace CrucibleUnlock
{
    public static class ThirdTrialFailurePolicy
    {
        public static bool AllowRuleUpdate(long source, bool available) => source != ThirdTrialPolicy.PlaylistGuid || available;
    }
}
