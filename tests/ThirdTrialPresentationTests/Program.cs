using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrucibleUnlock;
static class Program
{
    static int checks;
    static void Check(bool value,string message) { ++checks; if(!value) throw new Exception(message); }
    static void Main()
    {
        bool Third(long action=TrialRepairPolicy.OfferingGuid,long bowl=RitualViewPolicy.BowlDataGuid,
            string name="thirdStatueBowlInteraction",bool missing=true,bool bound=true,int count=1,bool scene=true,bool enabled=true) =>
            RitualViewPolicy.ShouldCreateThird(action,bowl,name,missing,bound,count,scene,enabled);
        Check(Third(),"exact third offering");
        Check(!Third(enabled:false),"entry disabled");
        Check(!Third(name:"secondStatueBowlInteraction"),"third cannot consume second");
        Check(!Third(name:"firstStatueBowlInteraction"),"first protected");
        Check(!Third(action:0)&&!Third(bowl:0),"foreign actions/assets");
        Check(!Third(missing:false)&&!Third(bound:false)&&!Third(count:0)&&!Third(count:2)&&!Third(scene:false),"clone ownership prerequisites");
        Check(!RitualViewPolicy.ShouldCreate(TrialRepairPolicy.OfferingGuid,RitualViewPolicy.BowlDataGuid,"thirdStatueBowlInteraction",true,true,1,true),"legacy second scope unchanged");
        Check(RitualViewPolicy.ShouldCreate(TrialRepairPolicy.OfferingGuid,RitualViewPolicy.BowlDataGuid,"secondStatueBowlInteraction",true,true,1,true),"legacy second retained");
        var second=RitualBloodRouteData.ReadLayout();
        var third=RitualBloodRouteData.ReadLayout(RitualBloodRouteData.ThirdPrefix);
        Check(!second.Position.SequenceEqual(third.Position),"independent target geometry");
        Check(Math.Abs(third.Position[0]+.7832940855)<1e-6&&Math.Abs(third.Position[1]+.6372217049)<1e-6&&Math.Abs(third.Position[2]+.8354930262)<1e-6,"third static transform");
        foreach(var layout in new[]{second,third})
        {
            string prefix=ReferenceEquals(layout,third)?RitualBloodRouteData.ThirdPrefix:RitualBloodRouteData.Prefix;
            foreach(var map in layout.Textures)
            {
                var bytes=RitualBloodRouteData.ReadPixels(prefix,map);
                Check(bytes.Length==4194304,"map checksum/length verified");
                Check(bytes.Any(x=>x!=0),"nonempty native-derived map");
            }
        }
        foreach(var map in third.Textures)
        {
            bool rejected=false;
            try { RitualBloodRouteData.ReadPixels(map); } catch(InvalidDataException) { rejected=true; }
            Check(rejected,"wrong prefix cannot accept another bowl's same-name payload");
        }
        bool badHash=false;
        var wrong=new RitualBloodRouteData.Map { Resource=third.Textures[0].Resource,Size=1024,RawSha256=new string('0',64) };
        try { RitualBloodRouteData.ReadPixels(RitualBloodRouteData.ThirdPrefix,wrong); } catch(InvalidDataException) { badHash=true; }
        Check(badHash,"hash corruption fails closed");
        Console.WriteLine("Third trial presentation checks passed: "+checks+"; no game/Unity assemblies loaded");
    }
}
