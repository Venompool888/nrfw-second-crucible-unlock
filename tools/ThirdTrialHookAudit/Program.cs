using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var provider=new Names();
const string Q="Il2CppQuantum.";
var hooks=new[] {
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"InitializeContentSystem","OnInit","Void",new[]{Q+"Frame"},"ThirdTrialRuntime","AfterInit",false),
 new Spec("Il2Cpp__forsaken","Il2Cpp.RuntimeAssetResolutionContext","Update","Void",Array.Empty<string>(),"EnemyRestorationRuntime","BeforeResolutionUpdate",true),
 new Spec("Il2Cpp__forsaken","Il2CppMoon.Playmode.InitializeQuantumAssetDatabase","InitializeResourceManager","Void",new[]{"Il2CppMoon.TheLoop.ViewFrame"},"EnemyRestorationRuntime","BeforeResourceManagerReset",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"QuestAPI","IsQuestStepComplete","System.Boolean",new[]{Q+"Frame",Q+"AssetRefGameQuestStepData"},"ThirdTrialEntry","QuestPrefix",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"QuantumConditionQuestStepState","ConditionPassedInternal","System.Boolean",new[]{Q+"Frame",Q+"EntityRef"},"ThirdTrialEntry","ConditionPrefix",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"InfiniteDungeonDirector","SelectNextPlaylist",Q+"InfiniteDungeonPlaylist",new[]{Q+"Frame"},"ThirdTrialEntry","AfterSelectNextPlaylist",false),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"InfiniteDungeonRoguelitePlaylistRuleDirector","UpdateActiveRun","Void",new[]{Q+"Frame"},"ThirdTrialRuntime","BeforeRuleUpdate",true),
 new Spec("Il2Cppmoon.quantum.forsaken",Q+"DynamicNpcSpawnerData","Spawn",Q+"EntityRef",new[]{Q+"Frame",Q+"EntityRef",Q+"AssetRefNpcData","Il2CppPhoton.Deterministic.FPVector3","Il2CppPhoton.Deterministic.FP",Q+"EnemyDifficulty","System.Int64","System.String"},"ThirdTrialSpawning","BeforeSpawn",true)
};
using var pluginFile=File.OpenRead(args[1]);using var pluginPe=new PEReader(pluginFile);var plugin=pluginPe.GetMetadataReader();
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
Console.WriteLine("PASS: 8 third-trial hook signatures/callbacks and required embedded manifests/blood resources; PE metadata only.");
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
