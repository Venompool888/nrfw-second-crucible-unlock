using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object {
  private static int next=1; public IntPtr Pointer {get;}=new IntPtr(next++); public bool Destroyed;
  public int GetInstanceID()=>Pointer.ToInt32(); public string name;
  public T Cast<T>() where T:class => this as T;
  public static Object Instantiate(Object source) { var m=(Il2CppMoon.Forsaken.LocalizedMessage)source; return m.Clone(); }
  public static void Destroy(Object source) { source.Destroyed=true; DestroyedObjects.Add(source); }
  public static readonly List<Object> DestroyedObjects=new();
 }
 public class Sprite:Object { }
}
namespace UnityEngine.UI { public class Image:UnityEngine.Object { public bool enabled=true; public UnityEngine.Sprite sprite=new(); } }
namespace Il2CppTMPro { public class TMP_Text:UnityEngine.Object { public string text; } }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
 public class Il2CppReferenceArray<T> where T:class {
  static int next=5000; readonly T[] entries; public IntPtr Pointer {get;}=new IntPtr(next++);
  public Il2CppReferenceArray(int count)=>entries=new T[count]; public int Length=>entries.Length;
  public T this[int i] {get=>entries[i];set=>entries[i]=value;}
 }
}
namespace Il2CppQuantum {
 public struct EntityRef { public ulong Raw; public bool IsValid=>Raw!=0; }
 public struct PlayerRef { public int Raw; }
 public struct RuntimeCharacterReference { public int Raw; public bool IsValid=>Raw!=0; }
 public struct AssetGuid { public long Value; } public struct AssetRefInfiniteDungeonPlaylist { public AssetGuid Id; }
 public struct FP { public long RawValue; }
 public class Storage { public IntPtr Pointer=new IntPtr(700); }
 public class Frame {
  public long Source=CrucibleUnlock.ThirdTrialPolicy.PlaylistGuid, Start=100; public int Completed,Skipped,Generated=13;
  public RuntimeCharacterReference Character=new(){Raw=42}; public RuntimeCharacterReference ObservedCharacter;
  public bool Active=true, ThrowSource, ThrowCompleted; public Storage InstanceDataStorage=new();
 }
 public class GeneratedList { public int Count; }
 public static class InfiniteDungeonAPI {
  public static AssetRefInfiniteDungeonPlaylist GetSourcePlaylist(Frame f) {if(f.ThrowSource)throw new InvalidOperationException("scope read");return new(){Id=new(){Value=f.Source}};}
  public static bool IsPlayerInActiveRun(Frame f,RuntimeCharacterReference p) {f.ObservedCharacter=p;return f.Active;}
  public static int GetCompletedFloorsForPlayer(Frame f,RuntimeCharacterReference p) {f.ObservedCharacter=p;if(f.ThrowCompleted)throw new InvalidOperationException("completed read");return f.Completed;}
  public static int GetSkippedFloors(Frame f)=>f.Skipped;
  public static GeneratedList GetRuntimePlaylist(Frame f)=>new(){Count=f.Generated};
  public static FP GetRunStartTime(Frame f)=>new(){RawValue=f.Start};
 }
 public static class RuntimeCharacterDataAPI {
  public static bool TryGetCharacterRef(Frame f,EntityRef e,out RuntimeCharacterReference c) {c=f.Character;return e.IsValid&&c.IsValid;}
 }
}
namespace Il2CppMoon.TheLoop {
 public class Players {
  public bool Bound=true, Local=true, Hero=true; public Il2CppQuantum.EntityRef Entity=new(){Raw=123}; public Il2CppQuantum.Frame Frame;
  public bool TryGetLocalPlayerRef(out Il2CppQuantum.PlayerRef p) {p=new(){Raw=2};return Local;}
  public bool TryGetHeroView(Il2CppQuantum.PlayerRef p,out Il2CppMoon.Forsaken.HeroView h) {h=Hero?new():null;return Hero;}
  public bool TryGetBoundHeroEntity(out Il2CppQuantum.Frame f,out Il2CppQuantum.EntityRef e) {f=Frame;e=Entity;return Bound;}
 }
 public class ViewFrame {
  public static ViewFrame Active; public bool IsDisposed; public Il2CppQuantum.Frame Frame;
  public (object,Players) Players; public Il2CppQuantum.Frame GetQtn()=>Frame;
 }
}
namespace Il2CppMoon.Forsaken {
 public class HeroView:UnityEngine.Object { }
 public class LocalizedMessage:UnityEngine.Object {
  public string Id,English,French,Italian,German,Spanish,BrazilianPortuguese,TraditionalChinese,SimplifiedChinese,Korean,Russian,Japanese,Polish;
  public LocalizedMessage Clone()=>new(){Id=Id,English=English,French=French,Italian=Italian,German=German,Spanish=Spanish,BrazilianPortuguese=BrazilianPortuguese,TraditionalChinese=TraditionalChinese,SimplifiedChinese=SimplifiedChinese,Korean=Korean,Russian=Russian,Japanese=Japanese,Polish=Polish};
 }
 public class PlayerCrucibleHUD:UnityEngine.Object {
  public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<LocalizedMessage> floorMessages;
  public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Sprite> FloorImages=new(11);
  public UnityEngine.UI.Image FloorImage=new(); public Il2CppTMPro.TMP_Text BoonTierText=new(),BoonTierShadowText=new();
  public string Text=>BoonTierText.text; public string Shadow=>BoonTierShadowText.text; public int NativeCalls,RewardCalls,m_currentFloor=-1;
  // Control reproduces official ordering: message lookup occurs before reward branch.
  public void NativeUpdate(Il2CppQuantum.Frame f,bool reward=false) {NativeCalls++;int index=checked(f.Completed+f.Skipped);if(m_currentFloor==index)return;var m=floorMessages[index];BoonTierText.text=BoonTierShadowText.text=m.English;m_currentFloor=index;if(reward)RewardCalls++;}
 }
}
namespace HarmonyLib { public class Harmony { public Harmony(string id){} public void UnpatchSelf(){} } }
namespace CrucibleUnlock {
 internal static class ThirdTrialPolicy {internal const long PlaylistGuid=-611686018427287904L;}
 internal static class TrialRepairLog {
  internal static List<string> Lines=new();
  internal static void Write(string s)=>Lines.Add(s);
  internal static void Patch(HarmonyLib.Harmony h,Type t,string n,Type[] args,Type patch,string prefix=null,string postfix=null,Type returnType=null){}
 }
}
