using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Performance;

// Metadata only: never loads an inspected game or plugin assembly.
// args: <vendor/generated dir> <built NRFWPerformance.dll>
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PerformanceHookAudit <vendor/generated dir> <NRFWPerformance.dll>");
    return 2;
}

var p = new Names();
var methods = new List<(string Type, string Method, string Return, string[] Args, bool Static)>();
var types = new HashSet<string>();
var assemblies = new List<string>
{
    "Il2Cpp__forsaken",
    "Il2Cppmoon.quantum.forsaken",
    "Il2CppQuantum.Engine",
    "Il2CppQuantum.Deterministic",
    "Il2CppInterop.Runtime",
    "Il2Cppmscorlib",
    "UnityEngine.CoreModule",
};
bool hasImgui = File.Exists(Path.Combine(args[0], "UnityEngine.IMGUIModule.dll"));
if (hasImgui) assemblies.Add("UnityEngine.IMGUIModule");

foreach (string assembly in assemblies)
{
    using var s = File.OpenRead(Path.Combine(args[0], assembly + ".dll"));
    using var pe = new PEReader(s);
    var r = pe.GetMetadataReader();
    foreach (var th in r.TypeDefinitions)
    {
        string tn = p.GetTypeFromDefinition(r, th, 0);
        types.Add(tn);
        foreach (var mh in r.GetTypeDefinition(th).GetMethods())
        {
            var m = r.GetMethodDefinition(mh);
            var sg = m.DecodeSignature(p, null);
            methods.Add((tn, r.GetString(m.Name), sg.ReturnType, sg.ParameterTypes.ToArray(), m.Attributes.HasFlag(MethodAttributes.Static)));
        }
    }
}

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS: " : "FAIL: ") + what);
    if (!ok) failures++;
}

// 1. Verify every Harmony hook target uniquely matches return type + exact parameter types.
foreach (var h in PerformancePolicy.Hooks)
{
    int n = methods.Count(m => m.Type == h.Type && m.Method == h.Method && m.Return == h.Return && m.Args.SequenceEqual(h.Args));
    Check(n == 1, $"hook {h.Type}.{h.Method}({string.Join(", ", h.Args)}) -> {h.Return} unique");
}

// 2. Verify every SRP / Cloth / Quantum / Profiler member accessed by NRFWPerformance.
void CheckCall(string type, string name, string result, bool isStatic, params string[] parameters)
{
    int n = methods.Count(m => m.Type == type && m.Method == name && m.Return == result && m.Static == isStatic && m.Args.SequenceEqual(parameters));
    Check(n == 1, $"call {type}.{name}({string.Join(",", parameters)}) -> {result}");
}

const string Mrp = "Il2Cpp.MoonRenderPipeline";
const string Mrpa = "Il2Cpp.MoonRenderPipelineAsset";
const string Mrpv = "Il2Cpp.MoonRenderPipelineView";
const string Cloth = "Il2CppMoon.AnimationControl.MoonClothUpdate";

CheckCall(Mrp, "get_currentAsset", Mrpa, true);
CheckCall(Mrp, "get_currentPipeline", Mrp, true);
CheckCall(Mrp, "get_gameView", Mrpv, false);
CheckCall(Mrp, "get_currentView", Mrpv, false);
CheckCall(Mrp, "get_FPS", "System.Int32", true);
CheckCall(Mrp, "get_frameTimeGPUAvg", "System.Single", true);
CheckCall(Mrp, "get_frameTimeGPUMax", "System.Single", true);
CheckCall(Mrp, "get_frameTimeCPUWithoutPresentWaitAvg", "System.Single", true);
CheckCall(Mrp, "get_m_MaxWorkerThreadCnt", "System.Int32", true);

foreach (var (prop, propType) in new[]
{
    ("SelectedQualityPreset", Mrpa + "+QualityPreset"),
    ("LimitWorkerThreadCnt", Mrpa + "+LimitWorkers"),
    ("WorkerThreadCnt", "System.Int32"),
    ("iMXAOShadingRate", Mrpa + "+IMXAOShadingRate"),
    ("iMXAOGlobalSampleQualityPreset", Mrpa + "+IMXAOSampleQuality"),
    ("FlashLightResDivider", "System.Int32"),
    ("FlashLightResDividerMinRes", "System.Int32"),
    ("FlashLightStepsMax", "System.Int32"),
    ("HalfResTransparencyEnabled", "System.Boolean"),
    ("HalfResTransparencyScale", "System.Single"),
    ("ExcludePareticleSystemShadowCasters", "System.Boolean"),
    ("MainLightShadowMapResolution", Mrpa + "+ShadowResolution"),
    ("ShadowMapResolution", Mrpa + "+TexResolution"),
    ("PCSSSearchNumSamples", "System.Int32"),
    ("PCSSMinNumSamples", "System.Int32"),
    ("PCSSMaxNumSamples", "System.Int32"),
    ("VolumetricUseLowResDepth", "System.Boolean"),
    ("VolumetricSamples", "System.Int32"),
    ("SSRMaxSteps", "System.Int32"),
    ("SSRUseHiZ", "System.Boolean"),
    ("ReflPlaneTexScaling", "System.Single"),
    ("MaxNumReflPlaneTextures", "System.Int32"),
    ("SmallObjectsCullingRelativeScreenHeight", "System.Single"),
    ("SmallObjectsCullingMinDistance", "System.Single"),
    ("AssetBlendingPassEnabled", "System.Boolean"),
})
{
    CheckCall(Mrpa, "get_" + prop, propType, false);
    CheckCall(Mrpa, "set_" + prop, "Void", false, propType);
}

CheckCall(Mrpv, "get_LastCameraPixelWidth", "System.Int32", false);
CheckCall(Mrpv, "get_LastCameraPixelHeight", "System.Int32", false);
CheckCall(Mrpv, "get_DepthHierarchyMipCount", "System.Int32", false);
CheckCall(Mrpv, "get_m_CurrentResolutionScaling", "System.Single", false);

CheckCall(Cloth, "get_EnableDistanceCulling", "System.Boolean", true);
CheckCall(Cloth, "get_DistanceCullingThreshold", "System.Single", true);
CheckCall(Cloth, "set_DistanceCullingThreshold", "Void", true, "System.Single");

CheckCall("Il2CppQuantum.ComponentTypeId", "GetComponentIndex", "System.Int32", true, "System.String");
CheckCall("Il2CppQuantum.ComponentTypeId", "GetComponentIndex", "System.Int32", true, "Il2CppSystem.Type");
CheckCall("Il2CppQuantum.Core.FrameBase", "Has", "System.Boolean", false, "Il2CppQuantum.EntityRef", "System.Int32");
CheckCall("Il2CppQuantum.EntityRef", "get_IsValid", "System.Boolean", false);
// EntityRef.Raw is a field (System.UInt64 at offset 0), not a property getter.
bool hasEntityRawField = false;
{
    using var es = File.OpenRead(Path.Combine(args[0], "Il2CppQuantum.Engine.dll"));
    using var epe = new PEReader(es);
    var er = epe.GetMetadataReader();
    foreach (var th in er.TypeDefinitions)
    {
        if (p.GetTypeFromDefinition(er, th, 0) != "Il2CppQuantum.EntityRef") continue;
        foreach (var fh in er.GetTypeDefinition(th).GetFields())
        {
            var f = er.GetFieldDefinition(fh);
            if (er.GetString(f.Name) == "Raw" && f.DecodeSignature(p, null) == "System.UInt64" && f.GetOffset() == 0)
                hasEntityRawField = true;
        }
    }
}
Check(hasEntityRawField, "field Il2CppQuantum.EntityRef.Raw : System.UInt64 @ 0");

if (hasImgui)
{
    CheckCall("UnityEngine.GUI", "Box", "Void", true, "UnityEngine.Rect", "System.String", "UnityEngine.GUIStyle");
    CheckCall("UnityEngine.GUI", "Label", "Void", true, "UnityEngine.Rect", "System.String", "UnityEngine.GUIStyle");
    CheckCall("UnityEngine.GUI", "Button", "System.Boolean", true, "UnityEngine.Rect", "System.String", "UnityEngine.GUIStyle");
}

// 3. Inspect built NRFWPerformance.dll metadata: references all hooks, does NOT touch forbidden CrucibleUnlock JIT targets.
using (var s = File.OpenRead(args[1]))
using (var pe = new PEReader(s))
{
    var r = pe.GetMetadataReader();
    var userStrings = new List<string>();
    var us = MetadataTokens.UserStringHandle(1);
    while (!us.IsNil)
    {
        userStrings.Add(r.GetUserString(us));
        us = r.GetNextHandle(us);
    }
    var memberNames = new HashSet<string>();
    foreach (var h in r.MemberReferences)
        memberNames.Add(r.GetString(r.GetMemberReference(h).Name));

    foreach (var h in PerformancePolicy.Hooks)
        Check(userStrings.Contains(h.Method), "plugin patches '" + h.Method + "' by name");

    foreach (string forbidden in new[] { "GetActionView", "InitializeResourceManager" })
        Check(!userStrings.Contains(forbidden), "plugin does not patch '" + forbidden + "' (CrucibleUnlock startup JIT order)");

    Check(!memberNames.Contains("TryGetPointer"), "plugin never calls Frame.TryGetPointer");
    Check(memberNames.Contains("GetComponentIndex") && memberNames.Contains("Has"), "plugin uses safe non-generic FrameBase.Has");
}

if (failures > 0)
{
    Console.WriteLine($"{failures} check(s) failed");
    return 1;
}
Console.WriteLine("All performance metadata checks passed.");
return 0;

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
        var t = r.GetTypeDefinition(h);
        var parent = t.GetDeclaringType();
        return parent.IsNil
            ? (r.GetString(t.Namespace) is string ns && ns.Length > 0 ? ns + "." : "") + r.GetString(t.Name)
            : GetTypeFromDefinition(r, parent, 0) + "+" + r.GetString(t.Name);
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var t = r.GetTypeReference(h);
        var s = t.ResolutionScope;
        if (s.Kind == HandleKind.TypeReference) return GetTypeFromReference(r, (TypeReferenceHandle)s, 0) + "+" + r.GetString(t.Name);
        return (r.GetString(t.Namespace) is string ns && ns.Length > 0 ? ns + "." : "") + r.GetString(t.Name);
    }
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k)
        => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
