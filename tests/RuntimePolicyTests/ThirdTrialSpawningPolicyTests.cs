using System;
using CrucibleUnlock;

internal static class ThirdTrialSpawningPolicyTests
{
    public static void Run()
    {
        const long source = -611686018427287904L;
        const long chunk = 101, bucket = 201, nugget = 202, spawner = 303;
        var known = new long[] { spawner, 304 };
        int checks = 0;
        void Check(bool valid, string description)
        { if (!valid) throw new Exception("FAIL: " + description); ++checks; }
        ThirdTrialSpawnDecision Decide(long playlist = source, long ownerChunk = chunk,
            long ownerBucket = bucket, long ownerNugget = nugget, int integration = 7, bool mapsToOwner = true,
            long spawnerGuid = spawner, bool entry = true) =>
            ThirdTrialSpawningPolicy.Decide(playlist, source, ownerChunk, chunk,
                ownerBucket, bucket, ownerNugget, nugget,
                integration, mapsToOwner, spawnerGuid, known, entry);
        Check(Decide() == ThirdTrialSpawnDecision.Replace, "selected predicted or verified combat spawner replaces NPC");
        Check(Decide(ownerBucket: 0, ownerNugget: 0) == ThirdTrialSpawnDecision.Replace,
            "chunk-owned integration uses whitelist and generated content to identify combat spawner");
        Check(Decide(playlist: source + 1) == ThirdTrialSpawnDecision.Pass, "first and second trial source untouched");
        Check(Decide(ownerChunk: 404) == ThirdTrialSpawnDecision.Pass, "reward and other chunks untouched");
        Check(Decide(ownerBucket: 405) == ThirdTrialSpawnDecision.Block, "unexpected spawn in auxiliary bucket blocked");
        Check(Decide(ownerNugget: 405) == ThirdTrialSpawnDecision.Block, "wrong combat nugget blocked");
        Check(Decide(integration: 6, mapsToOwner: false) == ThirdTrialSpawnDecision.Block, "stale integration ID blocked by native owner map");
        Check(Decide(integration: 0) == ThirdTrialSpawnDecision.Block, "unbound integration blocked");
        Check(Decide(spawnerGuid: 999) == ThirdTrialSpawnDecision.Block, "unknown combat spawner blocked");
        Check(Decide(entry: false) == ThirdTrialSpawnDecision.Block, "wrong generated nugget blocked");
        Check(ThirdTrialSpawningPolicy.SameNuggets(new long[] { 1, 2, 3 }, new long[] { 3, 1, 2 }),
            "all selected bucket nuggets match regardless of list order");
        Check(!ThirdTrialSpawningPolicy.SameNuggets(new long[] { 1, 2, 3 }, new long[] { 1, 2, 4 }),
            "different auxiliary content rejected");
        Check(!ThirdTrialSpawningPolicy.SameNuggets(new long[] { 1, 2, 2 }, new long[] { 1, 2, 3 }),
            "duplicate counts respected");
        Console.WriteLine("PASS: " + checks + " third-trial spawn scope and content checks; no game assemblies executed.");
    }
}
