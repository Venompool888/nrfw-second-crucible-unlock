using System;
using System.Collections.Generic;
using System.Reflection;
using CrucibleUnlock;
using Il2CppQuantum;

namespace HarmonyLib
{
    class Harmony
    {
        internal string Id;
        internal static int Removed;
        internal Harmony(string id) { Id=id; }
        internal void UnpatchSelf() { ++Removed; }
    }
}
namespace Il2CppQuantum
{
    class Frame { public bool IsVerified; public long Selected; public bool AssetsReady; }
    struct AssetGuid { public long Value; }
    struct AssetRefGameQuestStepData { public AssetGuid Id; }
    class InfiniteDungeonPlaylist { public AssetGuid Guid; public int PlaylistType; }
    static class QuestAPI { public static bool IsQuestStepComplete(Frame f,AssetRefGameQuestStepData s)=>false; }
    class QuantumConditionQuestStepState
    { public AssetRefGameQuestStepData Step; public int State; public bool Invert; public bool ConditionPassedInternal(Frame f,EntityRef e)=>false; }
    struct EntityRef { }
    class InfiniteDungeonDirector { public InfiniteDungeonPlaylist SelectNextPlaylist(Frame f)=>null; }
    static class InfiniteDungeonAPI { public static AssetRefGameQuestStepData GetSelectedPlaylistKey(Frame f)=>new AssetRefGameQuestStepData { Id=new AssetGuid { Value=f.Selected } }; }
}
namespace CrucibleUnlock
{
    static class TrialRepairLog
    {
        internal static int Patched,FailAt;
        public static void Patch(HarmonyLib.Harmony h,Type type,string name,Type[] args,Type patchType,string prefix=null,string postfix=null,Type returnType=null)
        {
            var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance,null,args,null);
            if(method==null||method.ReturnType!=returnType) throw new Exception("signature");
            if(++Patched==FailAt) throw new Exception("patch registration failed");
        }
    }
}
static class Program
{
    static int checks,errors;
    static bool views;
    static InfiniteDungeonPlaylist own;
    static void Check(bool value,string why) { ++checks; if(!value) throw new Exception(why); }
    static object Invoke(string name,params object[] args)=>typeof(ThirdTrialEntry).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static InfiniteDungeonPlaylist Playlist(long guid,int type=1)=>new InfiniteDungeonPlaylist { Guid=new AssetGuid { Value=guid },PlaylistType=type };
    static AssetRefGameQuestStepData Step(long guid)=>new AssetRefGameQuestStepData { Id=new AssetGuid { Value=guid } };
    static void Install()
    {
        ThirdTrialEntry.Install(f=>f.AssetsReady?own:null,()=>views,_=>{},_=>++errors);
    }
    static bool Read(Frame f,long step,out bool result)
    {
        object[] args={f,Step(step),false};
        bool original=(bool)Invoke("QuestPrefix",args); result=(bool)args[2]; return original;
    }
    static InfiniteDungeonPlaylist Select(Frame f,InfiniteDungeonPlaylist before)
    { object[] args={f,before}; Invoke("AfterSelectNextPlaylist",args); return (InfiniteDungeonPlaylist)args[1]; }
    static void Main()
    {
        own=Playlist(ThirdTrialEntry.PrivatePlaylistGuid); views=true;
        var verified=new Frame { IsVerified=true,Selected=ThirdTrialEntry.TargetStepGuid,AssetsReady=true };
        var predicted=new Frame { IsVerified=false,Selected=ThirdTrialEntry.TargetStepGuid,AssetsReady=true };
        var original=Playlist(ThirdTrialEntry.OriginalPlaylistGuid,3);
        Install(); Check(TrialRepairLog.Patched==3,"three precise hooks");
        Check(!Read(verified,ThirdTrialEntry.TargetStepGuid,out var result)&&result,"third verified read override");
        Check(!Read(predicted,ThirdTrialEntry.TargetStepGuid,out result)&&result,"prediction same readiness");
        Check(Read(verified,346285554720904463L,out result)&&!result,"second original read untouched");
        Check(Read(verified,0,out result),"foreign quest untouched");
        Check(ReferenceEquals(Select(verified,original),own),"verified remaps own source");
        Check(ReferenceEquals(Select(predicted,original),own),"prediction remaps own source");
        verified.Selected=346285554720904463L;
        Check(ReferenceEquals(Select(verified,original),original),"selected second protected");
        verified.Selected=ThirdTrialEntry.TargetStepGuid;
        var foreign=Playlist(123);
        Check(ReferenceEquals(Select(verified,foreign),foreign),"other mod source protected");
        Check(Select(verified,null)==null,"absent original not invented");
        views=false;
        Check(!Read(verified,ThirdTrialEntry.TargetStepGuid,out result)&&!result,"views not ready completed save cannot unlock");
        Check(Select(verified,original)==null,"views not ready stripped Horde blocked");
        views=true; verified.AssetsReady=false;
        Check(!Read(verified,ThirdTrialEntry.TargetStepGuid,out result)&&!result,"exact Frame assets unavailable");
        Check(Select(verified,original)==null,"Frame assets unavailable Horde blocked");
        Check(!Read(predicted,ThirdTrialEntry.TargetStepGuid,out result),"ready other Frame cannot make unavailable Frame ready");
        verified.AssetsReady=true;
        for(int state=0;state<=2;state++) foreach(bool invert in new[]{false,true})
        {
            var condition=new QuantumConditionQuestStepState { Step=Step(ThirdTrialEntry.TargetStepGuid),State=state,Invert=invert };
            object[] args={condition,verified,false};
            bool runOriginal=(bool)Invoke("ConditionPrefix",args);
            Check(state==2 ? !runOriginal&&(bool)args[2]==!invert : runOriginal,"exact completion state scope/invert semantics");
        }
        own=Playlist(123);
        Check(Select(verified,original)==null&&!ThirdTrialEntry.Enabled,"invalid readiness disables and blocks stripped source");
        Check(!Read(verified,ThirdTrialEntry.TargetStepGuid,out result)&&!result&&Select(predicted,original)==null,"failure retains installed blocker");
        Check(errors==0,"simulation deferred error logging"); ThirdTrialEntry.Flush(); Check(errors==1,"loader flush error");
        ThirdTrialEntry.Dispose(); Check(HarmonyLib.Harmony.Removed==1,"unpatch own owner");
        own=Playlist(ThirdTrialEntry.PrivatePlaylistGuid,3); Install();
        Check(!Read(verified,ThirdTrialEntry.TargetStepGuid,out result)&&!result&&!ThirdTrialEntry.Enabled,"wrong type rejected and source completed save blocked");
        ThirdTrialEntry.Dispose(); own=Playlist(ThirdTrialEntry.PrivatePlaylistGuid); Install();
        ThirdTrialEntry.StopWithoutUnityReads();
        Check(!ThirdTrialEntry.CurrentViewsReady&&Read(verified,ThirdTrialEntry.TargetStepGuid,out result),"shutdown no entry reads");
        ThirdTrialEntry.Dispose();
        TrialRepairLog.FailAt=TrialRepairLog.Patched+2;
        bool failed=false; int removed=HarmonyLib.Harmony.Removed;
        try { Install(); } catch(Exception) { failed=true; }
        Check(failed&&!ThirdTrialEntry.Enabled&&HarmonyLib.Harmony.Removed==removed+1,"partial patch install rolled back");
        Console.WriteLine("Third trial entry checks passed: "+checks+"; managed fake Frame/Harmony; no game assemblies loaded");
    }
}
