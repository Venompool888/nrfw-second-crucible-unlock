using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Necromancy;

// Metadata only: never loads an inspected game or plugin assembly.
// args: <vendor/generated dir> <built NRFWNecromancy.dll>
var p = new Names();
var methods = new List<(string Type, string Method, string Return, string[] Args)>();
var types = new HashSet<string>();
foreach (string assembly in new[] { "Il2Cppmoon.quantum.forsaken", "Il2Cpp__forsaken" })
{
    using var s = File.OpenRead(Path.Combine(args[0], assembly + ".dll")); using var pe = new PEReader(s); var r = pe.GetMetadataReader();
    foreach (var th in r.TypeDefinitions)
    {
        string tn = p.GetTypeFromDefinition(r, th, 0); types.Add(tn);
        foreach (var mh in r.GetTypeDefinition(th).GetMethods())
        {
            var m = r.GetMethodDefinition(mh); var sg = m.DecodeSignature(p, (object)null);
            methods.Add((tn, r.GetString(m.Name), sg.ReturnType, sg.ParameterTypes.ToArray()));
        }
    }
}
int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS: " : "FAIL: ") + what); if (!ok) failures++; }

foreach (var h in NecromancyPolicy.Hooks)
{
    int n = methods.Count(m => m.Type == h.Type && m.Method == h.Method && m.Return == h.Return && m.Args.SequenceEqual(h.Args));
    Check(n == 1, $"hook {h.Type}.{h.Method}({string.Join(", ", h.Args)}) -> {h.Return} unique");
}
foreach (string t in new[] { "Il2CppQuantum.CorpseMarkerSystem", "Il2CppQuantum.RaisedMinionSystem" })
    Check(methods.Any(m => m.Type == t && m.Method == ".ctor" && m.Args.Length == 0), t + " has parameterless constructor");
Check(methods.Any(m => m.Type == "Il2CppQuantum.InventoryAPI" && m.Method == "PreviewAwardItemImmediate" && m.Args.SequenceEqual(new[] {
    "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.AssetRefHeroItemData", "System.Int32", "Il2CppQuantum.NewInventoryItemSource" })),
    "InventoryAPI.PreviewAwardItemImmediate signature");
Check(methods.Any(m => m.Type == "Il2CppQuantum.SystemBase" && m.Method == "get__children"), "SystemBase._children accessor");

// The compiled plugin must reference every hooked method name and must not use the Cheat item source.
using (var s = File.OpenRead(args[1]))
using (var pe = new PEReader(s))
{
    var r = pe.GetMetadataReader();
    var strings = new HashSet<string>();
    foreach (var h in r.MemberReferences) strings.Add(r.GetString(r.GetMemberReference(h).Name));
    var userStrings = new List<string>();
    var us = MetadataTokens.UserStringHandle(1);
    while (!us.IsNil) { userStrings.Add(r.GetUserString(us)); us = r.GetNextHandle(us); }
    foreach (var h in NecromancyPolicy.Hooks) Check(userStrings.Contains(h.Method), "plugin patches '" + h.Method + "' by name");
    Check(strings.Contains("PreviewAwardItemImmediate"), "plugin calls PreviewAwardItemImmediate");
}
if (failures > 0) { Console.WriteLine(failures + " check(s) failed"); return 1; }
Console.WriteLine("All necromancy metadata checks passed.");
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
