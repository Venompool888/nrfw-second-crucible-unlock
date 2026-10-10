using System;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Timeline;
using Il2CppQuantum;
using UnityEngine;
using UO=UnityEngine.Object;

internal static class Program
{
 private static int checks;
 private static void Check(bool value,string text){if(!value)throw new Exception("FAIL "+text);++checks;}
 private static void Throws(Action action,string text){try{action();}catch(InvalidOperationException){++checks;return;}catch(AggregateException){++checks;return;}throw new Exception("Expected failure "+text);}
 private sealed class Fixture
 {
  public RuntimeAssetResolutionContext Ctx=new RuntimeAssetResolutionContext();
  public EnemyRestorationCatalog Catalog=new EnemyRestorationCatalog{Profiles=Array.Empty<EnemyRestorationProfile>()};
  public TimelineActionView Template;public HumanoidEnemyView Owner;public ActionData Action=new ActionData{Guid=new AssetGuid{Value=EnemyRestorationReactionViews.ParryGuid}};
  public Fixture(string key="keeper_sword_shield_left")
  {
   EnemyRestorationReactionViews.StopWithoutUnityReads();
   UO.FailDestroy=UO.FailClone=UO.ShareTimeline=UO.ForeignTrack=UO.FailActivate=false;
   ActionView.FailInitialize=ActionView.FailReturn=ActionView.WrongRegistration=false;
   MoonReference<MoonAnimator>.BadCopy=MoonReference<MoonAnimator>.BadResolve=false;
   MoonReference<Il2CppMoon.Wwise.SoundHost>.BadCopy=MoonReference<Il2CppMoon.Wwise.SoundHost>.BadResolve=false;
   EnemyRestorationOwnerScope.Profile=new EnemyRestorationProfile{Key=key};EnemyRestorationOwnerScope.Allowed=true;
   Core.AssetCtx=Ctx;
   var definition=new MoonAnimatorDefinition{name="playerMoonAnimatorDefinition"};
   var sourceAnimator=new MoonAnimator{Definition=definition};
   var sourceGo=new GameObject("parriedAttackHitReact");
   Template=new TimelineActionView{gameObject=sourceGo,Timeline=new MoonTimeline(),MainAnimatorRef=new MoonReference<MoonAnimator>(sourceAnimator)};sourceGo.Component=Template;
   var asset=new HitReactionDataAsset{AssetGuid=Action.Guid,View=Template};Template.QuantumAsset=asset;Ctx.Source=asset;
   for(int i=0;i<13;++i)
   {
    Component item=i==0?new ForsakenAnimationPlayer{Animation=new ClipAnimation{name="parriedHitReact",AnimatorDefinition=definition},Animator=new MoonReference<MoonAnimator>(sourceAnimator)}:
      i==1?new WeaponPreviewAnimator{WeaponRig=new GameObject().transform}:
      i==12?new WwiseSoundAnimator{MoonSoundHost=new MoonReference<Il2CppMoon.Wwise.SoundHost>(new Il2CppMoon.Wwise.SoundHost())}:new Component();
    item.gameObject=new GameObject();item.gameObject.Component=item;item.transform.SetParent(sourceGo.transform,false,false);
    Template.Timeline.EntityRecords.Add(new TimelineRecord{TimelineItem=item});
   }
   Owner=new HumanoidEnemyView{EntityRef=new EntityRef{Raw=45},CharacterAnimator=new MoonAnimator{Definition=definition},MainWeaponView=new EnemyWeaponView{gameObject=new GameObject()}};
   Owner.gameObject=new GameObject();Owner.gameObject.Component=Owner;
  }
  public bool Ensure()=>EnemyRestorationReactionViews.Ensure(Ctx,Catalog);
  public ActionView Repair()=>EnemyRestorationReactionViews.TryRepair(Owner,Action,Catalog);
 }
 public static void Main()
 {
  foreach(string key in new[]{"keeper_sword_shield_left","keeper_sword_shield_right","dead_tide_great_curved_sword"})
  {
   var f=new Fixture(key);Check(f.Ensure(),key+" template ready");
   var sourceAnim=(ForsakenAnimationPlayer)f.Template.Timeline.EntityRecords[0].TimelineItem;var sourceSound=(WwiseSoundAnimator)f.Template.Timeline.EntityRecords[12].TimelineItem;
   var sourceAnimRef=sourceAnim.Animator;var sourceSoundRef=sourceSound.MoonSoundHost;
   TrialRepairLog.FailReadyLog=true;
   var copy=(TimelineActionView)f.Repair();Check(copy!=null && copy.Pointer!=f.Template.Pointer,"independent reaction view survives diagnostic failure");
   TrialRepairLog.FailReadyLog=false;
   Check(copy.Timeline.Pointer!=f.Template.Timeline.Pointer,"independent mutable timeline");
   Check(f.Owner.Registered==copy && copy.EntityOwner==f.Owner,"official registration owns exact clone");
   var animation=(ForsakenAnimationPlayer)copy.Timeline.EntityRecords[0].TimelineItem;var audio=(WwiseSoundAnimator)copy.Timeline.EntityRecords[12].TimelineItem;
   Check(animation.Animator.Pointer!=sourceAnimRef.Pointer && animation.Animator.SafeResolve(null)==f.Owner.CharacterAnimator,"private animator binding");
   Check(audio.MoonSoundHost.Pointer!=sourceSoundRef.Pointer && audio.MoonSoundHost.SafeResolve(null)==f.Owner.MainWeaponView.AudioCompanion.WeaponSoundHost,"private weapon sound binding");
   Check(((WeaponPreviewAnimator)copy.Timeline.EntityRecords[1].TimelineItem).WeaponRig==f.Owner.MainWeaponView.transform,"preview target belongs to current weapon");
   Check(sourceAnim.Animator==sourceAnimRef && sourceSound.MoonSoundHost==sourceSoundRef && sourceAnimRef.SafeResolve(null)!=f.Owner.CharacterAnimator,"source references unchanged");
   Check(f.Repair()==copy,"idempotent owner copy");
   EnemyRestorationReactionViews.StopWithoutUnityReads();Check(f.Repair()==null,"stop prevents Unity repair");
   int destroyed=UO.Destroyed;EnemyRestorationReactionViews.Release(f.Owner);
   Check(copy.Returned && f.Owner.Registered==null && UO.Destroyed==destroyed+1,"release unregisters cleans context destroys own root");
   EnemyRestorationReactionViews.Release(f.Owner);Check(UO.Destroyed==destroyed+1,"repeat release no-op");
  }
  foreach(string skip in new[]{"sourcePending","foreignProfile","foreignScope","wrongAction","staleContext","weaponPending","soundPending"})
  {
   var f=new Fixture();if(skip=="sourcePending")f.Ctx.Source=null;
   Check(skip=="sourcePending"?!f.Ensure():f.Ensure(),"skip readiness "+skip);
   switch(skip){case "foreignProfile":EnemyRestorationOwnerScope.Profile.Key="inquisitor_greatsword";break;case "foreignScope":EnemyRestorationOwnerScope.Allowed=false;break;case "wrongAction":f.Action.Guid=new AssetGuid{Value=1};break;case "staleContext":Core.AssetCtx=new RuntimeAssetResolutionContext();break;case "weaponPending":f.Owner.MainWeaponView=null;break;case "soundPending":f.Owner.MainWeaponView.AudioCompanion.WeaponSoundHost=null;break;}
   int clones=UO.Instantiated;Check(f.Repair()==null && UO.Instantiated==clones,"skip creates no private hierarchy "+skip);
  }
  foreach(string fail in new[]{"shape","sourceGuid","clip","boundTemplate","cloneFailure","sharedTimeline","foreignTrack","rig","sharedAnimatorRef","unresolvedAnimator","sharedSoundRef","unresolvedSound","initialize","activate"})
  {
   var f=new Fixture();
   if(fail=="shape")f.Template.Timeline.EntityRecords.RemoveAt(0);
   if(fail=="sourceGuid")f.Ctx.Source.AssetGuid=new AssetGuid{Value=1};
   if(fail=="clip")((ForsakenAnimationPlayer)f.Template.Timeline.EntityRecords[0].TimelineItem).Animation.Clip=null;
   if(fail=="boundTemplate")f.Template.EntityOwner=f.Owner;
   if(fail=="shape"||fail=="sourceGuid"||fail=="clip"||fail=="boundTemplate"){Throws(()=>f.Ensure(),fail);continue;}
   Check(f.Ensure(),"failure fixture ready "+fail);
   switch(fail){case "cloneFailure":UO.FailClone=true;break;case "sharedTimeline":UO.ShareTimeline=true;break;case "foreignTrack":UO.ForeignTrack=true;break;case "rig":f.Owner.CharacterAnimator.Definition=new MoonAnimatorDefinition();break;case "sharedAnimatorRef":MoonReference<MoonAnimator>.BadCopy=true;break;case "unresolvedAnimator":MoonReference<MoonAnimator>.BadResolve=true;break;case "sharedSoundRef":MoonReference<Il2CppMoon.Wwise.SoundHost>.BadCopy=true;break;case "unresolvedSound":MoonReference<Il2CppMoon.Wwise.SoundHost>.BadResolve=true;break;case "initialize":ActionView.FailInitialize=true;break;case "activate":UO.FailActivate=true;break;}
   int destroyed=UO.Destroyed;Throws(()=>f.Repair(),fail);Check(UO.Destroyed==destroyed+1 && f.Owner.Registered==null,"failure destroys private root and unregisters "+fail);
  }
  foreach(string ownership in new[]{"sourcePointer","sourceView","sourceGuid","entity","weapon","animator","sound","missingMapEntry","foreignMapEntry"})
  {
   var f=new Fixture();Check(f.Ensure(),"ownership fixture ready");var copy=f.Repair();
   switch(ownership)
   {
    case "sourcePointer":f.Ctx.Source=new HitReactionDataAsset{AssetGuid=f.Action.Guid,View=f.Template};break;
    case "sourceView":((HitReactionDataAsset)f.Ctx.Source).View=new TimelineActionView();break;
    case "sourceGuid":f.Ctx.Source.AssetGuid=new AssetGuid{Value=9};break;
    case "entity":f.Owner.EntityRef=new EntityRef{Raw=99};break;
    case "weapon":f.Owner.MainWeaponView=new EnemyWeaponView{gameObject=new GameObject()};break;
    case "animator":f.Owner.CharacterAnimator=new MoonAnimator{Definition=f.Owner.CharacterAnimator.Definition};break;
    case "sound":f.Owner.MainWeaponView.AudioCompanion.WeaponSoundHost=new Il2CppMoon.Wwise.SoundHost();break;
    case "missingMapEntry":f.Owner.Registered=null;break;
    case "foreignMapEntry":f.Owner.Registered=new ActionView();break;
   }
   int count=UO.Instantiated;Throws(()=>f.Repair(),"ownership mutation "+ownership);Check(UO.Instantiated==count,"ownership mismatch cannot create second clone");
   EnemyRestorationReactionViews.Release(f.Owner);
   if(ownership=="foreignMapEntry")Check(f.Owner.Registered!=null && f.Owner.Registered!=copy,"cleanup preserves foreign action replacement");
  }
  int profilesLogged=0;
  foreach(var line in TrialRepairLog.Lines)if(line.Contains("SWORD PARRY READY profile="))++profilesLogged;
  Check(profilesLogged==3,"successful clone logs bounded to three scoped profiles");
  {
   var f=new Fixture();Check(f.Ensure(),"foreign-map fixture ready");var foreign=new ActionView();f.Owner.Registered=foreign;int clones=UO.Instantiated;
   Throws(()=>f.Repair(),"foreign map collision");Check(f.Owner.Registered==foreign && UO.Instantiated==clones,"foreign action preserved without cloning");
  }
  {
   var f=new Fixture();Check(f.Ensure(),"wrong-registration fixture ready");ActionView.WrongRegistration=true;int destroyed=UO.Destroyed;
   Throws(()=>f.Repair(),"official registration did not return exact clone");Check(f.Owner.Registered!=null && f.Owner.Registered!=UO.LastClone.Component && UO.Destroyed==destroyed+1,"wrong registration preserves foreign map and destroys only copy");
  }
  foreach(string cleanup in new[]{"unregister","bothUnregister","context","destroy"})
  {
   var f=new Fixture();Check(f.Ensure(),"cleanup fixture ready");var copy=f.Repair();int destroyed=UO.Destroyed;
   if(cleanup=="unregister"||cleanup=="bothUnregister")f.Owner.FailUnregister=true;if(cleanup=="bothUnregister")f.Owner.FailGuidUnregister=true;if(cleanup=="context")ActionView.FailReturn=true;if(cleanup=="destroy")UO.FailDestroy=true;
   Throws(()=>EnemyRestorationReactionViews.Release(f.Owner),"cleanup failure "+cleanup);
   Check(UO.Destroyed==destroyed+(cleanup=="bothUnregister"?0:1),"destroy requires proven detached map "+cleanup);
   f.Owner.FailUnregister=f.Owner.FailGuidUnregister=false;ActionView.FailReturn=false;UO.FailDestroy=false;
   if(cleanup=="destroy"||cleanup=="bothUnregister"){Throws(()=>f.Repair(),"failed release cannot return copy as ready");EnemyRestorationReactionViews.Release(f.Owner);Check(f.Owner.Registered==null && UO.Destroyed==destroyed+(cleanup=="destroy"?2:1),"retained cleanup retry succeeds "+cleanup);}
  }
  {
   var f=new Fixture();Check(f.Ensure(),"failed-create-destroy fixture ready");UO.FailDestroy=UO.FailClone=true;int destroyed=UO.Destroyed;
   Throws(()=>f.Repair(),"creation destroy failure");UO.FailDestroy=UO.FailClone=false;
   Throws(()=>f.Repair(),"failed creation root retained inactive");EnemyRestorationReactionViews.Release(f.Owner);Check(UO.Destroyed==destroyed+2,"failed creation root retried from registry");
  }
  {
   var f=new Fixture();Check(f.Ensure(),"failed-initialize-map fixture ready");ActionView.FailInitialize=true;f.Owner.FailUnregister=f.Owner.FailGuidUnregister=true;int destroyed=UO.Destroyed;
   Throws(()=>f.Repair(),"creation registered then cleanup removal failed");Check(UO.Destroyed==destroyed && !UO.LastClone.transform.parent.gameObject.Active,"creation uncertain-map copy retained inactive");
   f.Owner.FailUnregister=f.Owner.FailGuidUnregister=ActionView.FailInitialize=false;
   Throws(()=>f.Repair(),"uncertain creation copy cannot become ready");EnemyRestorationReactionViews.Release(f.Owner);Check(f.Owner.Registered==null && UO.Destroyed==destroyed+1,"creation uncertain-map registry permits retry");
  }
  {
   var f=new Fixture();Check(f.Ensure(),"null-map cleanup fixture ready");f.Repair();var map=f.Owner.m_actionViewMap;f.Owner.m_actionViewMap=null;int destroyed=UO.Destroyed;
   Throws(()=>EnemyRestorationReactionViews.Release(f.Owner),"missing map cannot prove detached");Check(UO.Destroyed==destroyed,"missing map keeps copy for retry");
   f.Owner.m_actionViewMap=map;EnemyRestorationReactionViews.Release(f.Owner);Check(f.Owner.Registered==null && UO.Destroyed==destroyed+1,"map restoration cleanup succeeds");
  }
  Console.WriteLine("EnemyRestorationReactionViews pure orchestration tests: "+checks+" passed.");
 }
}
