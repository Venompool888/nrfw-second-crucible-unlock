namespace CrucibleUnlock
{
    public static class RitualViewPolicy
    {
        public const long BowlDataGuid = 2951888897539463429L;
        public static bool ShouldCreate(long action, long bowlData, string bowl, bool missingView, bool bound, int templates, bool sameScene) =>
            action == TrialRepairPolicy.OfferingGuid && bowlData == BowlDataGuid &&
            bowl == "secondStatueBowlInteraction" && missingView && bound && templates == 1 && sameScene;
    }
}
