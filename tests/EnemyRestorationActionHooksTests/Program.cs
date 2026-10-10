using System.Reflection;
using System.Text.Json;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;
using UnityEngine;

var failures = new List<string>(); int checks = 0;
void Check(bool pass, string name) { ++checks; if (!pass) failures.Add(name); }
object Call(string name, object[] args) => typeof(EnemyRestorationActionHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,args);
var owner = new HarmonyLib.Harmony(); EnemyRestorationActionHooks.Install(owner);
Check(TrialRepairLog.Hooks.Count == 5, "Five exact hooks installed");
Check(TrialRepairLog.Hooks.All(h=>ReferenceEquals(h.Owner,owner)),"All hooks use module transaction owner");
Check(TrialRepairLog.Hooks[0].Type==typeof(HumanoidEnemyView) && TrialRepairLog.Hooks[0].Args.SequenceEqual(new[]{typeof(EnemyItemDataBaseAsset),typeof(bool)}),"Exact native item target ABI");
Check(TrialRepairLog.Hooks[1].Result==typeof(ActionView) && TrialRepairLog.Hooks[1].Args.SequenceEqual(new[]{typeof(ActionData),typeof(bool).MakeByRefType()}),"Exact native GetActionView ABI");
Check(TrialRepairLog.Hooks[2].Type==typeof(HumanoidEnemyView)&&TrialRepairLog.Hooks[2].Method=="OnReturnedToPool", "Cleanup before humanoid equipment release");
Check(TrialRepairLog.Hooks[3].Type==typeof(EntityView)&&TrialRepairLog.Hooks[3].Method=="OnDestroy", "Nonpool destruction cleanup hook");
Check(TrialRepairLog.Hooks.Count>4 && TrialRepairLog.Hooks[4].Type==typeof(HumanoidEnemyView) &&
    TrialRepairLog.Hooks[4].Method=="OnItemViewInstantiated" &&
    TrialRepairLog.Hooks[4].Args.SequenceEqual(new[]{typeof(GameObject),typeof(EnemyItemDataBaseAsset),typeof(bool)}),
    "Audio readback uses exact native item-instantiated target ABI");
var humanoid = new HumanoidEnemyView(); var item = new EnemyItemDataBaseAsset { AssetGuid = new(){Value=EnemyRestorationKeeperWeaponViews.WeaponGuid} };
bool Equip(HumanoidEnemyView view,EnemyItemDataBaseAsset asset,bool mainHand) => (bool)Call("BeforeItemAssetResolved",new object[]{view,asset,mainHand});
Check(Equip(humanoid,item,true) && EnemyRestorationKeeperWeaponViews.Calls==0,"Disabled module preserves original equipment without helper");
EnemyRestorationRuntime.CurrentViewsReady=true;
Check(Equip(humanoid,item,false) && EnemyRestorationKeeperWeaponViews.Calls==0,"Offhand unchanged");
Check(Equip(null,item,true) && EnemyRestorationKeeperWeaponViews.Calls==0,"Null owner unchanged");
Check(Equip(humanoid,null,true) && EnemyRestorationKeeperWeaponViews.Calls==0,"Null item unchanged");
Check(Equip(humanoid,new(){AssetGuid=new(){Value=42}},true) && EnemyRestorationKeeperWeaponViews.Calls==0,"Unrelated weapon unchanged");
Check(Equip(humanoid,item,true) && EnemyRestorationKeeperWeaponViews.Calls==1,"Owner rejected by real scope keeps original callback");
EnemyRestorationKeeperWeaponViews.Handled=true;
Check(!Equip(humanoid,item,true) && EnemyRestorationKeeperWeaponViews.Calls==2,"Exact restored keeper handled skips NoPool callback");
Check(ReferenceEquals(EnemyRestorationKeeperWeaponViews.LastCatalog,EnemyRestorationRuntime.Catalog),"Equipment receives module catalog");
EnemyRestorationKeeperWeaponViews.Throw=true;
Check(Equip(humanoid,item,true),"Exception before owner proof retains original callback");
Equip(humanoid,item,true);
Check(TrialRepairLog.Logs.Count(x=>x.Contains("keeper scope/equipment error"))==1,"Equipment errors logged once");
var parry = new ActionData { Guid=new(){Value=EnemyRestorationReactionViews.ParryGuid} }; var repaired = new ActionView();
ActionView Repair(EntityView view,ActionData action,ActionView existing=null) { object[] args={view,action,existing}; Call("AfterGetActionView",args); return (ActionView)args[2]; }
EnemyRestorationRuntime.CurrentViewsReady=false;
Check(Repair(humanoid,parry)==null && EnemyRestorationReactionViews.Calls==0,"Disabled module cannot add reaction");
EnemyRestorationRuntime.CurrentViewsReady=true;
Check(Repair(null,parry)==null&&EnemyRestorationReactionViews.Calls==0,"Null reaction owner ignored");
Check(Repair(humanoid,null)==null&&EnemyRestorationReactionViews.Calls==0,"Null action ignored");
Check(Repair(humanoid,new(){Guid=new(){Value=42}})==null&&EnemyRestorationReactionViews.Calls==0,"Unrelated action ignored");
Check(ReferenceEquals(Repair(humanoid,parry,repaired),repaired)&&EnemyRestorationReactionViews.Calls==0,"Official existing ActionView preserved");
Check(Repair(humanoid,parry)==null&&EnemyRestorationReactionViews.Calls==1,"Rejected owner keeps missing result");
EnemyRestorationReactionViews.Result=repaired;
Check(ReferenceEquals(Repair(humanoid,parry),repaired),"Exact missing result takes owned reaction view");
var blocked = new ActionData { Guid=new(){Value=EnemyRestorationReactionViews.BlockedGuid} };
Check(ReferenceEquals(Repair(humanoid,blocked),repaired),"Canonical blocked sword recoil can request its separate owned view");
var existingBlocked = new ActionView();
Check(ReferenceEquals(Repair(humanoid,blocked,existingBlocked),existingBlocked),
    "Existing blocked sword reaction remains preserved");
Check(ReferenceEquals(EnemyRestorationReactionViews.LastCatalog,EnemyRestorationRuntime.Catalog),"Reaction receives module catalog");
EnemyRestorationReactionViews.Throw=true;
Check(Repair(humanoid,parry)==null,"Repair exception stays missing"); Repair(humanoid,parry);
Check(TrialRepairLog.Logs.Count(x=>x.Contains("sword parry error"))==1,"Reaction errors logged once");
EnemyRestorationRuntime.CurrentViewsReady=false;
Call("BeforeHumanoidReturned",new object[]{humanoid}); Call("BeforeEntityDestroyed",new object[]{humanoid});
Check(EnemyRestorationReactionViews.Releases==2,"Both cleanup paths remain active after module stop");
Check(EnemyRestorationKeeperWeaponViews.Releases==2,"Failed keeper clones retry both native cleanup paths after stop");
EnemyRestorationReactionViews.ThrowCleanup=true;
Call("BeforeHumanoidReturned",new object[]{humanoid}); Call("BeforeEntityDestroyed",new object[]{humanoid});
Check(EnemyRestorationReactionViews.Releases==4,"Cleanup errors do not block native lifecycle");
Check(EnemyRestorationKeeperWeaponViews.Releases==4,"Reaction cleanup failure cannot skip keeper cleanup");
Check(TrialRepairLog.Logs.Count(x=>x.Contains("sword parry cleanup error"))==1,"Cleanup errors logged once");
EnemyRestorationKeeperWeaponViews.ThrowCleanup=true;
Call("BeforeHumanoidReturned",new object[]{humanoid}); Call("BeforeEntityDestroyed",new object[]{humanoid});
Check(EnemyRestorationReactionViews.Releases==6&&EnemyRestorationKeeperWeaponViews.Releases==6,"Both failing helpers still attempted independently");
Check(TrialRepairLog.Logs.Count(x=>x.Contains("keeper cleanup error"))==1,"Keeper cleanup errors logged once");
var clone = new GameObject();
void Audio(HumanoidEnemyView view,GameObject obj,EnemyItemDataBaseAsset asset,bool mainHand)
{
    var callback=typeof(EnemyRestorationActionHooks).GetMethod("AfterItemViewInstantiated",BindingFlags.Static|BindingFlags.NonPublic);
    if(callback!=null) callback.Invoke(null,new object[]{view,obj,asset,mainHand});
}
Audio(humanoid,clone,item,true);
Check(EnemyRestorationAudioDiagnostics.Calls==0,"Disabled module performs no audio readback");
EnemyRestorationRuntime.CurrentViewsReady=true;
Audio(humanoid,clone,item,false); Audio(null,clone,item,true); Audio(humanoid,null,item,true); Audio(humanoid,clone,null,true);
Check(EnemyRestorationAudioDiagnostics.Calls==0,"Offhand/null inputs perform no audio readback");
Audio(humanoid,clone,item,true);
Check(EnemyRestorationAudioDiagnostics.Calls==1 && ReferenceEquals(EnemyRestorationAudioDiagnostics.LastCatalog,EnemyRestorationRuntime.Catalog),
    "Native callback readback passes current module catalog once");
EnemyRestorationAudioDiagnostics.Throw=true;
Audio(humanoid,clone,item,true); Audio(humanoid,clone,item,true);
Check(TrialRepairLog.Logs.Count(x=>x.Contains("audio wiring diagnostic error"))==1,
    "Diagnostic exceptions are bounded and cannot alter native equipment callback");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Il2Cpp")||a.GetName().Name.StartsWith("Unity")),"No game DLL loaded");
if(args.Length>0) File.WriteAllText(args[0],JsonSerializer.Serialize(new {checks,failures,passed=failures.Count==0,actual_source_compiled=true,managed_fakes_only=true},new JsonSerializerOptions{WriteIndented=true}));
foreach(var failure in failures) System.Console.WriteLine("FAIL: "+failure);
System.Console.WriteLine($"Animation hooks actual-source managed tests: {checks} checks; {failures.Count} failures; no game DLLs.");
return failures.Count==0?0:1;

namespace HarmonyLib { class Harmony { } }
namespace Il2CppQuantum { struct AssetGuid { public long Value; } class ActionData { public AssetGuid Guid; } }
namespace Il2Cpp { class EnemyItemDataBaseAsset { public Il2CppQuantum.AssetGuid AssetGuid; } }
namespace UnityEngine { class GameObject { } }
namespace Il2CppMoon.Forsaken
{
 class ActionView { }
 class EntityView { public ActionView GetActionView(ActionData data,ref bool interactable)=>null; public void OnDestroy(){} }
 class HumanoidEnemyView:EntityView { public void OnItemAssetResolved(EnemyItemDataBaseAsset data,bool mainHand){} public void OnReturnedToPool(){} public void OnItemViewInstantiated(GameObject clone,EnemyItemDataBaseAsset data,bool mainHand){} }
}
namespace CrucibleUnlock
{
 class EnemyRestorationCatalog { }
 static class EnemyRestorationRuntime { public static bool CurrentViewsReady; public static EnemyRestorationCatalog Catalog=new(); }
 static class EnemyRestorationKeeperWeaponViews
 {
  public const long WeaponGuid=4494428972466418711; public static bool Handled,Throw,ThrowCleanup; public static int Calls,Releases; public static EnemyRestorationCatalog LastCatalog;
  public static bool TryEquip(HumanoidEnemyView owner,EnemyItemDataBaseAsset item,bool mainHand,EnemyRestorationCatalog catalog) { ++Calls;LastCatalog=catalog;if(Throw)throw new InvalidOperationException("injected scope error");return Handled; }
  public static void Release(EntityView owner) { ++Releases;if(ThrowCleanup)throw new InvalidOperationException("injected keeper cleanup failure"); }
 }
 static class EnemyRestorationReactionViews
 {
  public const long ParryGuid=2122162585542657617;public const long BlockedGuid=1496781608746759672;public static int Calls,Releases; public static bool Throw,ThrowCleanup;public static ActionView Result;public static EnemyRestorationCatalog LastCatalog;
  public static ActionView TryRepair(EntityView owner,ActionData action,EnemyRestorationCatalog catalog) { ++Calls;LastCatalog=catalog;if(Throw)throw new InvalidOperationException("injected clone failure");return Result; }
  public static void Release(EntityView owner) { ++Releases;if(ThrowCleanup)throw new InvalidOperationException("injected cleanup failure"); }
 }
 static class EnemyRestorationAudioDiagnostics
 {
  public static int Calls; public static bool Throw; public static EnemyRestorationCatalog LastCatalog;
  public static void ObserveEquipment(HumanoidEnemyView owner,GameObject clone,EnemyItemDataBaseAsset item,bool mainHand,EnemyRestorationCatalog catalog)
  { ++Calls;LastCatalog=catalog;if(Throw)throw new InvalidOperationException("injected audio diagnostic failure"); }
 }
 static class TrialRepairLog
 {
  public record Hook(HarmonyLib.Harmony Owner,Type Type,string Method,Type[] Args,Type Result);
  public static List<Hook> Hooks=new();public static List<string> Logs=new();public static void Write(string text)=>Logs.Add(text);
  public static void Patch(HarmonyLib.Harmony owner,Type type,string method,Type[] args,Type callback,string prefix=null,string postfix=null,Type returnType=null)
  {
   var target=type.GetMethod(method,BindingFlags.Public|BindingFlags.Instance,null,args,null);
   if(target==null||target.ReturnType!=(returnType??typeof(void))) throw new InvalidOperationException("Target ABI mismatch");
   if(callback.GetMethod(prefix??postfix,BindingFlags.NonPublic|BindingFlags.Static)==null)throw new InvalidOperationException("Missing callback");
   Hooks.Add(new(owner,type,method,args,returnType));
  }
 }
}
