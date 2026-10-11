using System;
using System.Collections.Generic;
using Il2CppMoon;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Timeline;
using Il2Cpp;

public class FakeNative
{
 private static long next;public IntPtr Pointer {get;}=new IntPtr(++next);
 public T TryCast<T>() where T:class=>this as T;
}
namespace UnityEngine
{
 public class Object:FakeNative
 {
  public string name;public static int Destroyed,Instantiated;public static GameObject LastClone;
  public static bool FailDestroy,FailClone,ShareTimeline,ForeignTrack,FailActivate;
  public static void Destroy(Object value){++Destroyed;if(FailDestroy)throw new InvalidOperationException("destroy");}
  public static GameObject Instantiate(GameObject source,Transform parent,bool world)
  {
   ++Instantiated;if(FailClone)throw new InvalidOperationException("clone");
   var original=(TimelineActionView)source.Component;
   var go=new GameObject(source.name);go.transform.SetParent(parent,false,false);
   var copy=new TimelineActionView {gameObject=go,QuantumAsset=original.QuantumAsset,MainAnimatorRef=original.MainAnimatorRef};go.Component=copy;
   copy.Timeline=ShareTimeline?original.Timeline:new MoonTimeline();
   if(!ShareTimeline)
    foreach(var r in original.Timeline.EntityRecords)
    {
     Component c=r.TimelineItem;
     Component n=c is ForsakenAnimationPlayer a?new ForsakenAnimationPlayer{Animation=a.Animation,Animator=a.Animator}:
       c is WeaponPreviewAnimator p?new WeaponPreviewAnimator{WeaponRig=p.WeaponRig}:
       c is WwiseSoundAnimator s?new WwiseSoundAnimator{MoonSoundHost=s.MoonSoundHost}:
       c is WwiseWeaponAnimator w?new WwiseWeaponAnimator{MoonSoundHost=w.MoonSoundHost,UseWeaponSoundHost=w.UseWeaponSoundHost}:
       c is WwiseFoleyAnimator?new WwiseFoleyAnimator():c is WwiseVoiceAnimator?new WwiseVoiceAnimator():
       c is WwiseFootstepAnimator?new WwiseFootstepAnimator():c is QuantumTimeScaleAnimator?new QuantumTimeScaleAnimator():new Component();
     var child=new GameObject();n.gameObject=child;child.Component=n;child.transform.SetParent(ForeignTrack?source.transform:go.transform,false,false);
     copy.Timeline.EntityRecords.Add(new TimelineRecord{TimelineItem=n});
    }
   LastClone=go;return go;
  }
 }
 public class GameObject:Object
 {
  public Transform transform;public Component Component;public bool Active;
  public GameObject(string value=null){name=value;transform=new Transform{gameObject=this};}
  public Component GetComponent(Type t)=>t.IsInstanceOfType(Component)?Component:null;
  public void SetActive(bool value){if(value && FailActivate)throw new InvalidOperationException("activate");Active=value;}
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;}
 public class Transform:Component
 {
  public Transform parent;public void SetParent(Transform p,bool world,bool rigid){parent=p;}
  public bool IsChildOf(Transform p){for(var n=this;n!=null;n=n.parent)if(n.Pointer==p.Pointer)return true;return false;}
 }
 public class AnimationClip:Object {}
}
namespace Il2CppInterop.Runtime {public static class Il2CppType {public static Type Of<T>()=>typeof(T);}}
namespace Il2CppQuantum
{
 public struct AssetGuid {public long Value;}
 public struct EntityRef {public ulong Raw;}
 public class ActionData:FakeNative {public AssetGuid Guid;}
}
namespace Il2Cpp
{
 public class AssetBase:FakeNative {public Il2CppQuantum.AssetGuid AssetGuid;}
 public class HitReactionDataAsset:AssetBase {public ActionView View;}
 public class RuntimeAssetResolutionContext:FakeNative
 {
  public AssetBase Source,BlockedSource;
  public bool TryFindWrapperImpl(Il2CppQuantum.AssetGuid g,out AssetBase value,bool load){value=g.Value==1496781608746759672L?BlockedSource:Source;return value!=null;}
 }
 public struct AssetRef {public Il2CppQuantum.AssetGuid Id;}
 public class EnemyWeaponView:UnityEngine.Component {public AssetRef WeaponData;public WwiseWeaponCompanion AudioCompanion=new WwiseWeaponCompanion();}
 public class WwiseWeaponCompanion:FakeNative {public Il2CppMoon.Wwise.SoundHost WeaponSoundHost=new Il2CppMoon.Wwise.SoundHost();}
}
namespace Il2CppMoon
{
 public class MoonReference<T>:FakeNative where T:FakeNative
 {
  private T value;public static bool BadCopy,BadResolve;
  public MoonReference(T v){value=v;}
  public MoonReference<T> Copy()=>BadCopy?this:new MoonReference<T>(value);
  public void SetStaticValue(T v){value=v;}
  public T SafeResolve(object ctx)=>BadResolve?null:value;
 }
 public class MoonAnimatorDefinition:UnityEngine.Object {}
 public class MoonAnimator:FakeNative {public MoonAnimatorDefinition Definition;}
 public class ClipAnimation:UnityEngine.Object {public UnityEngine.AnimationClip Clip=new UnityEngine.AnimationClip();public MoonAnimatorDefinition AnimatorDefinition;}
}
namespace Il2CppMoon.Wwise {public class SoundHost:FakeNative{}}
namespace Il2CppMoon.Timeline
{
 public class TimelineRecord {public UnityEngine.Component TimelineItem;}
 public class MoonTimeline:FakeNative {public List<TimelineRecord> EntityRecords=new List<TimelineRecord>();}
 public class WwiseSoundAnimator:UnityEngine.Component {public MoonReference<Il2CppMoon.Wwise.SoundHost> MoonSoundHost;}
 public class WwiseWeaponAnimator:UnityEngine.Component
 {
  public MoonReference<Il2CppMoon.Wwise.SoundHost> MoonSoundHost;public bool UseWeaponSoundHost;
  public Il2Cpp.WwiseWeaponCompanion m_cachedWeaponCompanion;public Il2CppMoon.Wwise.SoundHost m_cachedSoundHost;
  public static bool BadCompanion,BadHost;
  public Il2Cpp.WwiseWeaponCompanion EffectiveWeaponCompanion=>BadCompanion?null:m_cachedWeaponCompanion;
  public Il2CppMoon.Wwise.SoundHost EffectiveSoundHost=>BadHost?null:m_cachedSoundHost??(m_cachedSoundHost=UseWeaponSoundHost?EffectiveWeaponCompanion?.WeaponSoundHost:MoonSoundHost?.SafeResolve(null));
 }
 public class WwiseFoleyAnimator:UnityEngine.Component {}
 public class WwiseVoiceAnimator:UnityEngine.Component {}
 public class WwiseFootstepAnimator:UnityEngine.Component {}
}
namespace Il2CppMoon.Forsaken
{
 public static class Core {public static RuntimeAssetResolutionContext AssetCtx;}
 public class EntityView:UnityEngine.Component
 {
  public Il2CppQuantum.EntityRef EntityRef;public bool FailUnregister,FailGuidUnregister;
  public Dictionary<Il2CppQuantum.AssetGuid,ActionView> m_actionViewMap=new Dictionary<Il2CppQuantum.AssetGuid,ActionView>();
  public ActionView Registered {get{m_actionViewMap.TryGetValue(new Il2CppQuantum.AssetGuid{Value=CrucibleUnlock.EnemyRestorationReactionViews.ParryGuid},out var value);return value;}set{var key=new Il2CppQuantum.AssetGuid{Value=CrucibleUnlock.EnemyRestorationReactionViews.ParryGuid};if(value==null)m_actionViewMap.Remove(key);else m_actionViewMap[key]=value;}}
  public void UnregisterActionView(ActionView view){if(FailUnregister)throw new InvalidOperationException("unregister");if(m_actionViewMap.TryGetValue(view.QuantumAsset.AssetGuid,out var current)&&current==view)m_actionViewMap.Remove(view.QuantumAsset.AssetGuid);}
  public void UnregisterActionView(Il2CppQuantum.AssetGuid guid){if(FailGuidUnregister)throw new InvalidOperationException("unregister guid");m_actionViewMap.Remove(guid);}
 }
 public class HumanoidEnemyView:EntityView
 {
  public EnemyWeaponView MainWeaponView;public MoonAnimator CharacterAnimator;
 }
 public class ActionView:UnityEngine.Component
 {
  public AssetBase QuantumAsset;public MoonReference<MoonAnimator> MainAnimatorRef;public EntityView EntityOwner,OwningEntityView,CharacterView;public bool Returned;
  public static bool FailInitialize,FailReturn,WrongRegistration;
  public virtual void OnInstantiatedFromPool()
  {
   var p=transform.parent;while(p!=null && !(p.gameObject.Component is EntityView))p=p.parent;
   EntityOwner=(EntityView)p.gameObject.Component;EntityOwner.m_actionViewMap[QuantumAsset.AssetGuid]=WrongRegistration?new ActionView():this;
   if(this is TimelineActionView timeline)foreach(var record in timeline.Timeline.EntityRecords)if(record.TimelineItem is WwiseWeaponAnimator audio){audio.m_cachedWeaponCompanion=null;audio.m_cachedSoundHost=null;}
   if(FailInitialize)throw new InvalidOperationException("initialize");
  }
  public virtual void OnReturnedToPool(){Returned=true;if(FailReturn)throw new InvalidOperationException("return");}
 }
 public class TimelineActionView:ActionView {public MoonTimeline Timeline;public MoonTimeline EffectiveTimeline=>Timeline;}
 public class ForsakenAnimationPlayer:UnityEngine.Component {public MoonReference<MoonAnimator> Animator;public ClipAnimation Animation;public ClipAnimation EffectiveAnimation=>Animation;}
 public class WeaponPreviewAnimator:UnityEngine.Component {public UnityEngine.Transform WeaponRig;}
 public class QuantumTimeScaleAnimator:UnityEngine.Component {}
}
namespace CrucibleUnlock
{
 internal static class TrialRepairLog {public static bool FailReadyLog;public static List<string> Lines=new List<string>();public static void Write(string text){Lines.Add(text);if(FailReadyLog && text.Contains("SWORD PARRY READY profile="))throw new InvalidOperationException("log");}}
 // Scope is tested independently against native attachment/storage layouts.
 // This seam injects the verified profile to test only reaction orchestration.
 internal static class EnemyRestorationOwnerScope
 {
  public static EnemyRestorationProfile Profile;public static bool Allowed=true;
  public static bool TryGetProfile(EntityView owner,EnemyRestorationCatalog catalog,out EnemyRestorationProfile p){p=Profile;return Allowed && p!=null;}
 }
}
