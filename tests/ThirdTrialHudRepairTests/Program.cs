using System.Reflection;
using System.Text.Json;
using CrucibleUnlock;
using Il2CppMoon.Forsaken;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
static class Program {
 static int pass,fail;
 static readonly string[] Romans={"I","II","III","IV","V","VI","VII","VIII","IX","X","XI"};
 static readonly JsonDocument Shipped=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ShippedHudMessages.json")));
 static void Check(bool value,string text) {if(!value)throw new Exception(text);}
 static bool Prefix(PlayerCrucibleHUD h)=>(bool)typeof(ThirdTrialHudRepair).GetMethod("BeforeUpdateCurrentFloor",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{h});
 static void Release(PlayerCrucibleHUD h)=>typeof(ThirdTrialHudRepair).GetMethod("BeforeReleaseHud",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{h});
 static (PlayerCrucibleHUD,Frame) Setup(int index=11) {
  ThirdTrialHudRepair.Dispose();ThirdTrialHudRepair.Install();TrialRepairLog.Lines.Clear();UnityEngine.Object.DestroyedObjects.Clear();
  var f=new Frame{Completed=index};var p=new Players{Frame=f};ViewFrame.Active=new(){Frame=f,Players=(null,p)};
  var entries=Shipped.RootElement.GetProperty("floorMessages");Check(entries.GetArrayLength()==11,"shipped evidence must contain eleven entries");
  var h=new PlayerCrucibleHUD{floorMessages=new(entries.GetArrayLength())};
  for(int i=0;i<entries.GetArrayLength();i++){
   var tree=entries[i].GetProperty("object").GetProperty("tree");var message=new LocalizedMessage();
   foreach(var field in typeof(LocalizedMessage).GetFields())if(field.FieldType==typeof(string)&&tree.TryGetProperty(field.Name,out var value))field.SetValue(message,value.GetString());
   h.floorMessages[i]=message;
  }return(h,f);
 }
 static void Run(PlayerCrucibleHUD h,Frame f,bool reward=false) {if(Prefix(h))h.NativeUpdate(f,reward);}
 static void Test(string n,Action a) {try{a();pass++;Console.WriteLine("PASS "+n);}catch(Exception e){fail++;Console.WriteLine("FAIL "+n+": "+(e.InnerException??e).Message);}}
 static void Main() {
  Test("11-entry shipped HUD index11 displays XII",()=>{var(h,f)=Setup();Run(h,f);Check(h.Text=="XII"&&h.Shadow=="XII","must repair real native array lookup");});
  Test("index12 displays XIII including reward lookup ordering",()=>{var(h,f)=Setup(12);Run(h,f,true);Check(h.Text=="XIII"&&h.RewardCalls==1,"reward follows safe message lookup");});
  Test("existing eleven entries and assets stay unchanged",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);Check(h.floorMessages.Pointer!=old.Pointer,"per-instance copy");for(int i=0;i<11;i++)Check(h.floorMessages[i].Pointer==old[i].Pointer&&old[i].English==Romans[i],"unchanged entry "+i);Check(old.Length==11,"original length");});
  Test("correct added labels in all twelve languages",()=>{var(h,f)=Setup();Run(h,f);var m=h.floorMessages[11];foreach(var n in new[]{"English","French","Italian","German","Spanish","BrazilianPortuguese","TraditionalChinese","SimplifiedChinese","Korean","Russian","Japanese","Polish"})Check((string)typeof(LocalizedMessage).GetField(n).GetValue(m)=="XII",n);});
  Test("skip offset contributes to actual display index",()=>{var(h,f)=Setup(10);f.Skipped=2;Run(h,f);Check(h.Text=="XIII"&&f.Completed==10&&f.Skipped==2,"no progress rewrite");});
  Test("exact bound RuntimeCharacterReference used",()=>{var(h,f)=Setup();Run(h,f);Check(f.ObservedCharacter.Raw==42,"bound character, not entity/player");});
  Test("ordinary first and second source untouched",()=>{foreach(long source in new[]{1L,2L}){var(h,f)=Setup(3);f.Source=source;var old=h.floorMessages;Run(h,f);Check(h.Text=="IV"&&h.floorMessages.Pointer==old.Pointer,"ordinary unchanged");}});
  Test("negative private index suppresses only current-floor call",()=>{var(h,f)=Setup(-1);Run(h,f);Check(h.NativeCalls==0,"invalid suppressed");});
  Test("corrupt large completed count cannot allocate",()=>{var(h,f)=Setup(int.MaxValue);var old=h.floorMessages;Run(h,f);Check(h.NativeCalls==0&&h.floorMessages.Pointer==old.Pointer,"bounded allocation");});
  Test("checked skipped overflow bounded",()=>{var(h,f)=Setup();f.Skipped=int.MaxValue;Run(h,f);Check(h.NativeCalls==0,"overflow suppressed");});
  Test("generated playlist count governs bound, not source11",()=>{var(h,f)=Setup(12);f.Generated=13;Run(h,f);Check(h.Text=="XIII","generated count");f.Generated=10;Run(h,f);Check(h.NativeCalls==1,"inconsistent generated suppressed");});
  Test("null array in established private run suppressed",()=>{var(h,f)=Setup();h.floorMessages=null;Run(h,f);Check(h.NativeCalls==0,"null array suppression");});
  Test("scope read failure passes through",()=>{var(h,f)=Setup(3);f.ThrowSource=true;Check(Prefix(h),"unknown scope cannot freeze ordinary");});
  Test("scope read failure releases previous owned private UI",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);f.ThrowSource=true;Check(Prefix(h)&&h.floorMessages.Pointer==old.Pointer&&h.FloorImage.enabled,"unknown scope cannot retain private override");});
  Test("null frame passes through without retained frame",()=>{var(h,f)=Setup();ViewFrame.Active.Frame=null;Check(Prefix(h)&&h.floorMessages.Length==11,"no frame retention");});
  Test("missing player binding passes through",()=>{var(h,f)=Setup();ViewFrame.Active.Players.Item2.Bound=false;Check(Prefix(h)&&h.floorMessages.Length==11,"no guessed character");});
  Test("private read failure after proven identity suppresses",()=>{var(h,f)=Setup();f.ThrowCompleted=true;Check(!Prefix(h),"proven private guard");});
  Test("out-of-range floor icon hidden then restored",()=>{var(h,f)=Setup();var sprite=h.FloorImage.sprite;Run(h,f);Check(!h.FloorImage.enabled,"avoid stale official XI icon");f.Completed=2;Run(h,f);Check(h.FloorImage.enabled&&h.FloorImage.sprite==sprite,"restore owned icon");});
  Test("same HUD return to ordinary restores exact array",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);f.Source=2;f.Completed=2;Run(h,f);Check(h.floorMessages.Pointer==old.Pointer&&h.Text=="III"&&h.FloorImage.enabled,"exact restored");Check(UnityEngine.Object.DestroyedObjects.Count>0,"owned clones released");});
  Test("foreign array replacement preserved",()=>{var(h,f)=Setup();Run(h,f);var foreign=new Il2CppReferenceArray<LocalizedMessage>(11);for(int i=0;i<11;i++)foreign[i]=new(){English=Romans[i]};h.floorMessages=foreign;f.Source=1;f.Completed=1;Run(h,f);Check(h.floorMessages.Pointer==foreign.Pointer,"do not overwrite foreign array");});
  Test("foreign array borrowed clone not destroyed",()=>{var(h,f)=Setup();Run(h,f);var clone=h.floorMessages[11];var foreign=new Il2CppReferenceArray<LocalizedMessage>(12);for(int i=0;i<11;i++)foreign[i]=h.floorMessages[i];foreign[11]=clone;h.floorMessages=foreign;Release(h);Check(h.floorMessages.Pointer==foreign.Pointer&&!clone.Destroyed,"foreign reference preserved");});
  Test("reset disable lifecycle restores owned UI",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);Release(h);Check(h.floorMessages.Pointer==old.Pointer&&h.FloorImage.enabled,"release lifecycle");});
  Test("owned labels restored during lifecycle release",()=>{var(h,f)=Setup();h.BoonTierText.text="original";h.BoonTierShadowText.text="shadow";Run(h,f);Release(h);Check(h.Text=="original"&&h.Shadow=="shadow","exact owned label restore");});
  Test("foreign label and icon changes survive release",()=>{var(h,f)=Setup();Run(h,f);h.BoonTierText.text="foreign";h.FloorImage.sprite=new UnityEngine.Sprite();Release(h);Check(h.Text=="foreign"&&!h.FloorImage.enabled,"do not overwrite external presentation");});
  Test("same cached index on ordinary return reads restored array",()=>{var(h,f)=Setup();Run(h,f);f.Completed=2;Run(h,f);h.BoonTierText.text="foreign-before-return";f.Source=2;Run(h,f);Check(h.Text=="III","invalidate native floor cache when removing owned array");});
  Test("sanity ceiling prevents generated count allocation",()=>{var(h,f)=Setup();f.Generated=64;Run(h,f);Check(h.NativeCalls==0&&h.floorMessages.Length==11,"ceiling64 includes extra endpoint");});
  Test("null original indexed message suppresses",()=>{var(h,f)=Setup(3);h.floorMessages[3]=null;Run(h,f);Check(h.NativeCalls==0,"do not let null slot reach native localization");});
  Test("disposed view releases private UI without stale frame",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);ViewFrame.Active.IsDisposed=true;Check(Prefix(h)&&h.floorMessages.Pointer==old.Pointer,"view lifecycle no retained frame");});
  Test("new run on reused HUD renews ownership",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);var owned=h.floorMessages;f.Start++;Run(h,f);Check(h.floorMessages.Pointer!=owned.Pointer,"fresh run copy");Release(h);Check(h.floorMessages.Pointer==old.Pointer,"original restored");});
  Test("StopWithoutUnityReads retains ownership until safe prefix cleanup",()=>{var(h,f)=Setup();var old=h.floorMessages;Run(h,f);var owned=h.floorMessages;ThirdTrialHudRepair.StopWithoutUnityReads();Check(h.floorMessages.Pointer==owned.Pointer,"stop no Unity reads/writes");Check(!Prefix(h)&&h.floorMessages.Pointer==old.Pointer,"later safe cleanup with private safety suppression");});
  Test("stopped repair cannot expose old private array bounds again",()=>{var(h,f)=Setup();Run(h,f);ThirdTrialHudRepair.StopWithoutUnityReads();int calls=h.NativeCalls;Run(h,f);Check(h.NativeCalls==calls&&h.floorMessages.Length==11,"stopped private safety guard");f.Source=2;f.Completed=3;Run(h,f);Check(h.Text=="IV"&&h.NativeCalls==calls+1,"ordinary trials remain live after stop");});
  Test("diagnostic budget bounded under repeated corrupt state",()=>{var(h,f)=Setup(-1);for(int i=0;i<300;i++){f.Completed=-i-1;Prefix(h);}Check(TrialRepairLog.Lines.Count<=32,"log budget");});
  Test("native callback is short and helper prepared without inlining",()=>{Setup();var callback=typeof(ThirdTrialHudRepair).GetMethod("BeforeUpdateCurrentFloor",BindingFlags.NonPublic|BindingFlags.Static);Check(callback.GetMethodBody().GetILAsByteArray().Length<80,"short callback");var helper=typeof(ThirdTrialHudRepair).GetMethod("RepairCurrentFloor",BindingFlags.NonPublic|BindingFlags.Static);Check(helper!=null&&(helper.MethodImplementationFlags&MethodImplAttributes.NoInlining)!=0,"isolated full helper");});
  ThirdTrialHudRepair.Dispose();Console.WriteLine($"TOTAL {pass} passed, {fail} failed");Environment.ExitCode=fail==0?0:1;
 }
}
