using System;
using CrucibleUnlock;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
    }

    private static BrokenWarrickMusicPolicy.Candidate KnownBroken() => new BrokenWarrickMusicPolicy.Candidate
    {
        Phase = 1, PhaseTimelineCount = 1, TimelineName = "bossMusic", GuidMatches = true,
        Scene = "runtime scene can differ", Loop = true, RecordCount = 2,
        FirstId = 0, SecondId = 1, FirstIsMusicAnimator = true, FirstClipIsNull = true,
        FirstVolume = 0, FirstEndTime = 122.30530548095703f,
        SecondItemIsNull = true, SecondEndEvent = 2, SecondEndTarget = -1, SecondEndTime = 0
    };

    private static void MustPassThrough(string name, Action<BrokenWarrickMusicPolicy.Candidate> change)
    {
        var candidate = KnownBroken();
        change(candidate);
        Check(!BrokenWarrickMusicPolicy.ShouldSkip(candidate), name);
    }

    private static void Main()
    {
        BowgunInputPolicyTests.Run();
        Check(BrokenWarrickMusicPolicy.ShouldSkip(KnownBroken()), "confirmed GUID with known broken structure");
        var byPath = KnownBroken(); byPath.GuidMatches = false; byPath.Scene = BrokenWarrickMusicPolicy.ScenePath;
        Check(BrokenWarrickMusicPolicy.ShouldSkip(byPath), "exact asset scene path with same broken structure");
        Check(!BrokenWarrickMusicPolicy.ShouldSkip(null), "missing candidate passes through");
        MustPassThrough("same bad shape on unknown object", c => c.GuidMatches = false);
        MustPassThrough("same name in a different boss scene", c => { c.GuidMatches = false; c.Scene = BrokenWarrickMusicPolicy.ScenePath.Replace("warrickBossFight", "darakBossFight"); });
        MustPassThrough("fixed second child must play normally", c => c.SecondItemIsNull = false);
        MustPassThrough("different first child type", c => c.FirstIsMusicAnimator = false);
        MustPassThrough("phase zero", c => c.Phase = 0);
        MustPassThrough("multiple phase timelines", c => c.PhaseTimelineCount = 2);
        MustPassThrough("different timeline", c => c.TimelineName = "attack");
        MustPassThrough("extra record", c => c.RecordCount = 3);
        MustPassThrough("changed record IDs", c => c.SecondId = 4);
        MustPassThrough("non-looping timeline", c => c.Loop = false);
        MustPassThrough("different clip duration", c => c.FirstEndTime = 120f);
        MustPassThrough("real audio clip present", c => c.FirstClipIsNull = false);
        MustPassThrough("different end event", c => c.SecondEndEvent = 1);
        MustPassThrough("different end target", c => c.SecondEndTarget = 0);
        MustPassThrough("different end time", c => c.SecondEndTime = 1f);
        Check(BrokenWarrickMusicPolicy.MatchesGuid(-2091225097, 1266097816, 2090557619, -1408367959), "known hidden Warrick GUID");
        Check(!BrokenWarrickMusicPolicy.MatchesGuid(-2091225097, 1266097816, 2090557619, -1408367958), "nearby GUID rejected");
        Console.WriteLine("PASS: " + _checks + " music guard scope checks; no game assemblies loaded.");
        MotionTraceSinkTests.Run();
        Console.WriteLine("PASS: trace writer serialization, queue bounds, I/O failure and shutdown checks.");
        WarrickPhase2TargetPolicyTests.Run();
        TrialRepairPolicyTests.Run();
        RitualViewPolicyTests.Run();
        RoomReusePolicyTests.Run();
        RitualBloodRouteDataTests.Run();
        BossTraceDropTests.Run();
        BrokenVowBossPolicyTests.Run();
        EchoCapPolicyTests.Run();
    }
}
