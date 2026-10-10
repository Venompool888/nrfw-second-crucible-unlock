using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
var provider=new Names();
const string Q="Il2CppQuantum.";
var hooks=new[] {
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"InitializeContentSystem","OnInit","Void",new[]{Q+"Frame"},"ThirdTrialRuntime","AfterInit",false),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Playmode.InitializeQuantumAssetDatabase","InitializeResourceManager","Void",new[]{"Il2CppMoon.TheLoop.ViewFrame"},"EnemyRestorationRuntime","BeforeResourceManagerReset",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"QuestAPI","IsQuestStepComplete","System.Boolean",new[]{Q+"Frame",Q+"AssetRefGameQuestStepData"},"ThirdTrialEntry","QuestPrefix",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"QuantumConditionQuestStepState","ConditionPassedInternal","System.Boolean",new[]{Q+"Frame",Q+"EntityRef"},"ThirdTrialEntry","ConditionPrefix",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"InfiniteDungeonDirector","SelectNextPlaylist",Q+"InfiniteDungeonPlaylist",new[]{Q+"Frame"},"ThirdTrialEntry","AfterSelectNextPlaylist",false),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"InfiniteDungeonRoguelitePlaylistRuleDirector","UpdateActiveRun","Void",new[]{Q+"Frame"},"ThirdTrialRuntime","BeforeRuleUpdate",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"DynamicNpcSpawnerData","Spawn",Q+"EntityRef",new[]{Q+"Frame",Q+"EntityRef",Q+"AssetRefNpcData","Il2CppPhoton.Deterministic.FPVector3","Il2CppPhoton.Deterministic.FP",Q+"EnemyDifficulty","System.Int64","System.String"},"ThirdTrialSpawning","BeforeSpawn",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"DynamicNpcSpawnerData","Spawn",Q+"EntityRef",new[]{Q+"Frame",Q+"EntityRef",Q+"AssetRefNpcData","Il2CppPhoton.Deterministic.FPVector3","Il2CppPhoton.Deterministic.FP",Q+"EnemyDifficulty","System.Int64","System.String"},"ThirdTrialSpawning","AfterSpawn",false),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Forsaken.HumanoidEnemyView","OnItemAssetResolved","Void",new[]{"Il2Cpp.EnemyItemDataBaseAsset","System.Boolean"},"EnemyRestorationActionHooks","BeforeItemAssetResolved",true),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Forsaken.EntityView","GetActionView","Il2CppMoon.Forsaken.ActionView",new[]{Q+"ActionData","System.Boolean&"},"EnemyRestorationActionHooks","AfterGetActionView",false),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Forsaken.HumanoidEnemyView","OnReturnedToPool","Void",Array.Empty<string>(),"EnemyRestorationActionHooks","BeforeHumanoidReturned",true),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Forsaken.EntityView","OnDestroy","Void",Array.Empty<string>(),"EnemyRestorationActionHooks","BeforeEntityDestroyed",true),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Forsaken.HumanoidEnemyView","OnItemViewInstantiated","Void",new[]{"UnityEngine.GameObject","Il2Cpp.EnemyItemDataBaseAsset","System.Boolean"},"EnemyRestorationActionHooks","AfterItemViewInstantiated",false)
};
using var pluginFile=File.OpenRead(args[1]);using var pluginPe=new PEReader(pluginFile);var plugin=pluginPe.GetMetadataReader();
// A signature can exist while its generated implementation only throws.
// Audit every supplied generated assembly referenced by this plugin before
// treating hook/clone method signatures as usable native bridges.
var stubs=new HashSet<string>();
int inspectedDependencies=0;
foreach(var reference in plugin.AssemblyReferences) {
 var dependencyPath=Path.Combine(args[0],plugin.GetString(plugin.GetAssemblyReference(reference).Name)+".dll");
 if(!File.Exists(dependencyPath))continue; // CLR/loader references live elsewhere.
 using var dependencyFile=File.OpenRead(dependencyPath);using var dependencyPe=new PEReader(dependencyFile);
 var dependency=dependencyPe.GetMetadataReader();++inspectedDependencies;
 foreach(var th in dependency.TypeDefinitions)foreach(var mh in dependency.GetTypeDefinition(th).GetMethods()) {
  var method=dependency.GetMethodDefinition(mh);if(method.RelativeVirtualAddress==0)continue;
  var il=dependencyPe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
  if(il.Length<5||il[0]!=0x72||(BitConverter.ToInt32(il,1)&unchecked((int)0xff000000))!=0x70000000)continue;
  var message=dependency.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il,1)&0xffffff));
  if(!message.Contains("unstripping failed",StringComparison.Ordinal))continue;
  var sig=method.DecodeSignature(provider,(object)null);
  stubs.Add(provider.GetTypeFromDefinition(dependency,th,0)+"::"+dependency.GetString(method.Name)+"("+string.Join(",",sig.ParameterTypes)+")");
 }
}
int stubFailures=0;
foreach(var mh in plugin.MemberReferences) {
 var method=plugin.GetMemberReference(mh);
 if(method.GetKind()!=MemberReferenceKind.Method||method.Parent.Kind!=HandleKind.TypeReference)continue;
 var sig=method.DecodeMethodSignature(provider,(object)null);
 var key=provider.GetTypeFromReference(plugin,(TypeReferenceHandle)method.Parent,0)+"::"+plugin.GetString(method.Name)+"("+string.Join(",",sig.ParameterTypes)+")";
 if(stubs.Contains(key)){++stubFailures;Console.WriteLine("FAIL: compiled plugin references throwing interop stub "+key);}
}
if(stubFailures!=0){Environment.ExitCode=1;return;}
Console.WriteLine("PASS: throwing interop stub references=0; generated dependencies inspected="+inspectedDependencies+"; PE metadata only.");
foreach(var hook in hooks) {
 using var file=File.OpenRead(Path.Combine(args[0],hook.Assembly+".dll"));using var pe=new PEReader(file);var reader=pe.GetMetadataReader();
 var type=reader.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(reader,t,0)==hook.Type);
 int matches=reader.GetTypeDefinition(type).GetMethods().Count(m=>{
  var d=reader.GetMethodDefinition(m);var s=d.DecodeSignature(provider,(object)null);
  return reader.GetString(d.Name)==hook.Method && s.ReturnType==hook.Return && s.ParameterTypes.SequenceEqual(hook.Args);
 });
 if(matches!=1)throw new Exception("Target ABI mismatch: "+hook.Type+"."+hook.Method);
 var callbackType=plugin.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(plugin,t,0)=="CrucibleUnlock."+hook.CallbackType);
 var callback=plugin.GetTypeDefinition(callbackType).GetMethods().Single(m=>plugin.GetString(plugin.GetMethodDefinition(m).Name)==hook.Callback);
 var method=plugin.GetMethodDefinition(callback);var sig=method.DecodeSignature(provider,(object)null);
 foreach(var h in method.GetParameters()) {
  var parameter=plugin.GetParameter(h);if(parameter.SequenceNumber==0)continue;
  string name=plugin.GetString(parameter.Name),actual=sig.ParameterTypes[parameter.SequenceNumber-1],expected;
  if(name=="__instance")expected=hook.Type;
  else if(name=="__result")expected=hook.Return+"&";
  else if(name.StartsWith("__") && int.TryParse(name.Substring(2),out int i) && i<hook.Args.Length) {
    expected=hook.Args[i];
    if(hook.Callback=="BeforeSpawn" && name=="__2")expected+="&";
  } else throw new Exception("Unknown callback parameter "+name);
  if(actual!=expected)throw new Exception("Callback ABI mismatch "+hook.Callback+" "+name+": "+actual+" != "+expected);
 }
 if(sig.ReturnType!="Void" && !(hook.Prefix && sig.ReturnType=="System.Boolean"))throw new Exception("Bad callback return type");
 Console.WriteLine("PASS: "+hook.Type+"."+hook.Method+" / "+hook.Callback);
}
var resources=plugin.ManifestResources.Select(h=>plugin.GetString(plugin.GetManifestResource(h).Name)).ToHashSet();
foreach(string resource in new[]{"NRFW.ThirdTrial.Rooms.json","NRFW.EnemyRestoration.Bindings.json","NRFW.ThirdBowlBlood.layout.json"})
 if(!resources.Contains(resource))throw new Exception("Missing embedded resource "+resource);
if(resources.Count(x=>x.StartsWith("NRFW.ThirdBowlBlood."))!=7)throw new Exception("Third blood resources incomplete");
var main = PluginMethod("CrucibleUnlock.ModMain", "OnUpdate");
var pump = PluginMethod("CrucibleUnlock.EnemyRestorationRuntime", "PumpCurrentViews");
if (!Calls(main).Contains("CrucibleUnlock.EnemyRestorationRuntime.PumpCurrentViews"))
 throw new Exception("Loader OnUpdate does not pump current enemy views.");
if (!Calls(pump).Contains("Il2CppMoon.Forsaken.Core.get_AssetCtx"))
 throw new Exception("View pump does not read the official current resolver.");
if (!Calls(pump).Contains("Il2CppMoon.Forsaken.Core.get_ResourceManager"))
 throw new Exception("View pump does not bind the simulation snapshot to the current resource manager.");
var viewEnsure=PluginMethod("CrucibleUnlock.EnemyRestorationViews", "Ensure");
if(Calls(viewEnsure).Contains("Il2CppQuantum.EntityViewData..ctor"))
 throw new Exception("Unity view registration constructs a second simulation data object.");
var rootChecks=Calls(PluginMethod("CrucibleUnlock.EnemyRestorationViews","VerifyRoot"));
if(!rootChecks.Contains("UnityEngine.GameObject.GetComponent") ||
 !rootChecks.Any(c=>c.EndsWith(".TryCast<Il2CppMoon.Forsaken.EntityView>")) ||
 !rootChecks.Any(c=>c.EndsWith(".TryCast<Il2CppMoon.Forsaken.HumanoidEnemyView>")))
 throw new Exception("Enemy prefab root lacks native EntityView and HumanoidEnemyView component validation.");
Console.WriteLine("PASS: enemy prefab roots validate native EntityView and HumanoidEnemyView through non-generic Unity GetComponent.");
var weaponEnsure = PluginMethod("CrucibleUnlock.EnemyRestorationWeaponViews", "Ensure");
var weaponSignature = plugin.GetMethodDefinition(weaponEnsure).DecodeSignature(provider,(object)null);
if (!weaponSignature.ParameterTypes.SequenceEqual(new[]{"Il2Cpp.RuntimeAssetResolutionContext","CrucibleUnlock.EnemyRestorationCatalog",Q+"EnemyWeaponData[]"}) ||
    !Calls(pump).Contains("CrucibleUnlock.EnemyRestorationWeaponViews.Ensure"))
 throw new Exception("Main-thread pump does not require canonical private weapon wrappers before readiness.");
var weaponType=plugin.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(plugin,t,0)=="CrucibleUnlock.EnemyRestorationWeaponViews");
var weaponCalls=plugin.GetTypeDefinition(weaponType).GetMethods().Where(h=>plugin.GetMethodDefinition(h).RelativeVirtualAddress!=0).SelectMany(Calls).ToArray();
if(weaponCalls.Contains(Q+"EnemyWeaponData..ctor") || weaponCalls.Any(c=>c.EndsWith(".LoadOpResult")))
 throw new Exception("Unity private weapon registration constructs simulation data or activates a prefab while loading.");
var weaponRootChecks=Calls(PluginMethod("CrucibleUnlock.EnemyRestorationWeaponViews","VerifyRoot"));
if(!weaponRootChecks.Contains("UnityEngine.GameObject.GetComponent") ||
 !weaponRootChecks.Any(c=>c.EndsWith(".TryCast<Il2Cpp.EnemyWeaponView>")))
 throw new Exception("Private weapon root is not validated through native EnemyWeaponView.");
Console.WriteLine("PASS: private weapon wrappers require canonical simulation data and native item roots; no duplicate data or activating load result.");
foreach(string helper in new[]{"EnemyRestorationKeeperWeaponViews","EnemyRestorationReactionViews"})
 if(!Calls(pump).Contains("CrucibleUnlock."+helper+".Ensure"))
  throw new Exception("Pump does not gate animation source readiness: "+helper);
var animationInstall = Calls(PluginMethod("CrucibleUnlock.EnemyRestorationRuntime","Install"));
if(!animationInstall.Contains("CrucibleUnlock.EnemyRestorationActionHooks.Install"))
 throw new Exception("Animation hooks not installed within enemy module transaction.");
var ownerCalls=Calls(PluginMethod("CrucibleUnlock.EnemyRestorationOwnerScope","TryGetProfile"));
if(!ownerCalls.Contains(Q+"EntityRefExtensions.Get<"+Q+"NpcAttachmentComponent>") ||
 ownerCalls.Any(c=>c.Contains("Frame")&&c.EndsWith(".Get<"+Q+"NpcAttachmentComponent>")) ||
 ownerCalls.Contains(Q+"NpcAttachmentComponent..ctor") || ownerCalls.Any(c=>c.EndsWith(".get_QuantumAsset")))
 throw new Exception("Enemy owner proof does not use current native attachment pointers.");
foreach(string callback in new[]{"BeforeItemAssetResolved","AfterGetActionView","BeforeHumanoidReturned","BeforeEntityDestroyed","AfterItemViewInstantiated"}) {
 var handle=PluginMethod("CrucibleUnlock.EnemyRestorationActionHooks",callback);
 var definition=plugin.GetMethodDefinition(handle);
 if((definition.ImplAttributes & System.Reflection.MethodImplAttributes.NoInlining)==0 ||
 pluginPe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes().Length>120)
  throw new Exception("Animation detour callback is not short and isolated: "+callback);
}
var animationHooks=plugin.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(plugin,t,0)=="CrucibleUnlock.EnemyRestorationActionHooks");
var helperCalls=plugin.GetTypeDefinition(animationHooks).GetMethods().Where(h=>plugin.GetMethodDefinition(h).RelativeVirtualAddress!=0).SelectMany(Calls).ToArray();
if(!helperCalls.Contains("System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod"))
 throw new Exception("Animation hooks lack startup helper compilation.");
foreach(string type in new[]{"EnemyRestorationActionHooks","EnemyRestorationKeeperWeaponViews","EnemyRestorationReactionViews","EnemyRestorationOwnerScope","EnemyRestorationAudioDiagnostics"}) {
 var h=plugin.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(plugin,t,0)=="CrucibleUnlock."+type);
 var calls=plugin.GetTypeDefinition(h).GetMethods().Where(m=>plugin.GetMethodDefinition(m).RelativeVirtualAddress!=0).SelectMany(Calls);
 if(calls.Any(c=>c.Contains("Il2Cpp")&&c.EndsWith(".GetEnumerator")))
  throw new Exception("Animation helper traverses a native enumerator: "+type);
}
var keeperEquipCalls=Calls(PluginMethod("CrucibleUnlock.EnemyRestorationKeeperWeaponViews","TryEquip"));
int keeperParent=keeperEquipCalls.IndexOf("UnityEngine.Transform.SetParent");
// C# emits the base virtual slot; its generated wrapper uses
// il2cpp_object_get_virtual_method to reach the native weapon override.
int keeperInit=keeperEquipCalls.FindIndex(c=>c=="Il2CppMoon.Forsaken.EnemyItemView.OnInstantiatedFromPool" ||
 c=="Il2Cpp.EnemyWeaponView.OnInstantiatedFromPool");
int keeperCallback=keeperEquipCalls.IndexOf("Il2CppMoon.Forsaken.HumanoidEnemyView.OnItemViewInstantiated");
if(keeperParent<0 || keeperInit<keeperParent || keeperCallback<keeperInit ||
 !keeperEquipCalls.Contains("CrucibleUnlock.EnemyRestorationKeeperWeaponViews.VerifyMeleeEffects"))
 throw new Exception("Keeper clone is not parented before native initialization/equipment with effect-owner readback.");
var audioCalls=Calls(PluginMethod("CrucibleUnlock.EnemyRestorationAudioDiagnostics","ObserveEquipment"));
if(!audioCalls.Contains("CrucibleUnlock.EnemyRestorationOwnerScope.TryGetProfile") ||
 audioCalls.Any(c=>c.StartsWith("Il2Cpp")&&(c.Contains(".set_")||c.EndsWith(".PlaySound")||c.EndsWith(".PostEvent"))))
 throw new Exception("Audio wiring diagnostics lack private owner proof or mutate native audio state.");
Console.WriteLine("PASS: keeper/reaction readiness, native owner proof, five short NoInlining callbacks, helper preparation, clone parenting and scoped read-only audio diagnostics.");
var viewSignature=plugin.GetMethodDefinition(viewEnsure).DecodeSignature(provider,(object)null);
if(!viewSignature.ParameterTypes.SequenceEqual(new[]{"Il2Cpp.RuntimeAssetResolutionContext","CrucibleUnlock.EnemyRestorationCatalog",Q+"EntityViewData[]"}))
 throw new Exception("Unity view registration does not require the canonical simulation views.");
var initCalls=Calls(PluginMethod("CrucibleUnlock.ThirdTrialRuntime","AfterInit"));
int storagePosition=initCalls.IndexOf("CrucibleUnlock.ThirdTrialStoragePolicy.Ensure");
int playlistPosition=initCalls.IndexOf("CrucibleUnlock.ThirdTrialRuntime.EnsurePlaylist");
int publishPosition=initCalls.IndexOf("CrucibleUnlock.EnemyRestorationRuntime.PublishSimulationViews");
if(storagePosition<0 || playlistPosition<=storagePosition || publishPosition<=playlistPosition)
 throw new Exception("Canonical views are not published after storage and playlist initialization.");
var runtime = plugin.TypeDefinitions.Single(t => provider.GetTypeFromDefinition(plugin,t,0)=="CrucibleUnlock.EnemyRestorationRuntime");
if (plugin.GetTypeDefinition(runtime).GetMethods().Any(m => plugin.GetString(plugin.GetMethodDefinition(m).Name)=="BeforeResolutionUpdate"))
 throw new Exception("Unused resolver Update callback remains.");
Console.WriteLine("PASS: loader OnUpdate calls current-view pump; pump reads Core.AssetCtx; unused resolver Update callback removed.");
Console.WriteLine("PASS: canonical simulation views required by Unity; no second EntityViewData construction; snapshot published after full bootstrap; current manager read.");
var spawnType=plugin.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(plugin,t,0)=="CrucibleUnlock.ThirdTrialSpawning");
var spawnCalls=plugin.GetTypeDefinition(spawnType).GetMethods()
 .Where(h=>plugin.GetMethodDefinition(h).RelativeVirtualAddress!=0).SelectMany(Calls).ToArray();
foreach(string component in new[]{"IdentityComponent","IntegrationGroupComponent","DynamicSpawnerComponent","DynamicNpcSpawnerComponent"}) {
 string generic="<"+Q+component+">";
 if(spawnCalls.Any(c=>c==Q+"Core.FrameBase.Get"+generic || c==Q+"FrameBase.Get"+generic || c==Q+"Frame.Get"+generic))
  throw new Exception("Spawner uses boxed component Get instead of official component pointer: "+component);
 if(!spawnCalls.Contains(Q+"EntityRefExtensions.Get"+generic))
  throw new Exception("Spawner lacks official component pointer read: "+component);
}
if(spawnCalls.Contains(Q+"DynamicSpawnerComponent..ctor") || spawnCalls.Contains(Q+"DynamicNpcSpawnerComponent..ctor"))
 throw new Exception("Spawner wraps raw component memory as a boxed IL2CPP object.");
if(Calls(PluginMethod("CrucibleUnlock.ThirdTrialSpawning","GeneratedEntryMatches"))
 .Any(c=>c.Contains("QListPtr") && (c.EndsWith(".op_Equality") || c.EndsWith(".op_Inequality"))))
 throw new Exception("Generated entry null guard invokes native QListPtr equality with a null object.");
Console.WriteLine("PASS: spawner reads four component pointers through official EntityRefExtensions.Get; no boxed Frame.Get component reads or raw-pointer object wrappers.");
Console.WriteLine("PASS: generated entry null guard does not call native QListPtr equality.");
Console.WriteLine("PASS: 13 third-trial callbacks on 12 native targets and required embedded manifests/blood resources; PE metadata only.");

MethodDefinitionHandle PluginMethod(string type, string method) {
 var t=plugin.TypeDefinitions.Single(t=>provider.GetTypeFromDefinition(plugin,t,0)==type);
 return plugin.GetTypeDefinition(t).GetMethods().Single(m=>plugin.GetString(plugin.GetMethodDefinition(m).Name)==method);
}
List<string> Calls(MethodDefinitionHandle method) {
 var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>unchecked((ushort)o.Value));
 var il=pluginPe.GetMethodBody(plugin.GetMethodDefinition(method).RelativeVirtualAddress).GetILBytes();
 var calls=new List<string>();
 for(int i=0;i<il.Length;) {
  ushort code=il[i++]; if(code==0xfe)code=(ushort)(0xfe00|il[i++]);
  var op=opcodes[code]; int size=op.OperandType switch {
   OperandType.InlineNone=>0,
   OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
   OperandType.InlineVar=>2,
   OperandType.InlineI8 or OperandType.InlineR=>8,
   OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,i),
   _=>4
  };
  if(op.OperandType==OperandType.InlineMethod && (op==OpCodes.Call || op==OpCodes.Callvirt || op==OpCodes.Newobj))
   calls.Add(MethodName(MetadataTokens.EntityHandle(BitConverter.ToInt32(il,i))));
  i+=size;
 }
 return calls;
}
string MethodName(EntityHandle h) {
 if(h.Kind==HandleKind.MethodSpecification) {
  var spec=plugin.GetMethodSpecification((MethodSpecificationHandle)h);
  return MethodName(spec.Method)+"<"+string.Join(",",spec.DecodeSignature(provider,(object)null))+">";
 }
 if(h.Kind==HandleKind.MethodDefinition) {
  var m=plugin.GetMethodDefinition((MethodDefinitionHandle)h);
  return provider.GetTypeFromDefinition(plugin,m.GetDeclaringType(),0)+"."+plugin.GetString(m.Name);
 }
 var r=plugin.GetMemberReference((MemberReferenceHandle)h);
 string owner=r.Parent.Kind switch {
  HandleKind.TypeReference=>provider.GetTypeFromReference(plugin,(TypeReferenceHandle)r.Parent,0),
  HandleKind.TypeDefinition=>provider.GetTypeFromDefinition(plugin,(TypeDefinitionHandle)r.Parent,0),
  HandleKind.TypeSpecification=>provider.GetTypeFromSpecification(plugin,null,(TypeSpecificationHandle)r.Parent,0),
  _=>r.Parent.Kind.ToString()
 };
 return owner+"."+plugin.GetString(r.Name);
}
record Spec(string Assembly,string Type,string Method,string Return,string[] Args,string CallbackType,string Callback,bool Prefix);

sealed class Names : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string t, ArrayShape s) => t + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string t) => t + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string t, ImmutableArray<string> a) => t + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string m, string t, bool r) => t;
    public string GetPinnedType(string t) => t;
    public string GetPointerType(string t) => t + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c == PrimitiveTypeCode.Void ? "Void" : "System." + c;
    public string GetSZArrayType(string t) => t + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    {
        var t = r.GetTypeDefinition(h); var parent = t.GetDeclaringType();
        return parent.IsNil ? r.GetString(t.Namespace) + "." + r.GetString(t.Name) : GetTypeFromDefinition(r, parent, 0) + "+" + r.GetString(t.Name);
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var t = r.GetTypeReference(h);
        return t.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(r, (TypeReferenceHandle)t.ResolutionScope, 0) + "+" + r.GetString(t.Name)
            : r.GetString(t.Namespace) + "." + r.GetString(t.Name);
    }
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
