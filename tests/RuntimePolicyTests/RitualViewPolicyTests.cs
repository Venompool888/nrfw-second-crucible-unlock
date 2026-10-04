using System;
using CrucibleUnlock;
internal static class RitualViewPolicyTests
{
    public static void Run()
    {
        int n = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); ++n; }
        long a = TrialRepairPolicy.OfferingGuid, b = RitualViewPolicy.BowlDataGuid;
        Check(RitualViewPolicy.ShouldCreate(a,b,"secondStatueBowlInteraction",true,true,1,true), "missing second bowl offering view receives exact first-bowl template");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"firstStatueBowlInteraction",true,true,1,true), "first bowl never altered");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"thirdStatueBowlInteraction",true,true,1,true), "other locked modes unaffected");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"secondStatueBowlInteraction",false,true,1,true), "existing working view preserved");
        Check(!RitualViewPolicy.ShouldCreate(a+1,b,"secondStatueBowlInteraction",true,true,1,true), "other action unaffected");
        Check(!RitualViewPolicy.ShouldCreate(a,b+1,"secondStatueBowlInteraction",true,true,1,true), "name alone insufficient");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"secondStatueBowlInteraction",true,false,1,true), "unbound object ignored");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"secondStatueBowlInteraction",true,true,2,true), "ambiguous templates rejected");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"secondStatueBowlInteraction",true,true,0,true), "missing template rejected");
        Check(!RitualViewPolicy.ShouldCreate(a,b,"secondStatueBowlInteraction",true,true,1,false), "cross-scene template rejected");
        Console.WriteLine("PASS: " + n + " ritual view creation scope checks.");
    }
}
