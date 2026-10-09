using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CrucibleUnlock;

// Read PE metadata only; never load an inspected game or plugin assembly.
var provider = new Names();
string references = args[0];
var methods = new List<(string Assembly, string Type, string Method, string Return, string[] Args)>();
foreach (string assembly in new[] { "Il2Cpp__forsaken", "Il2Cppmoon.quantum.forsaken" })
{
    using var stream = File.OpenRead(Path.Combine(references, assembly + ".dll"));
    using var pe = new PEReader(stream);
    var reader = pe.GetMetadataReader();
    foreach (var th in reader.TypeDefinitions)
    {
        var type = reader.GetTypeDefinition(th);
        string typeName = provider.GetTypeFromDefinition(reader, th, 0);
        foreach (var mh in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            var signature = method.DecodeSignature(provider, (object)null);
            methods.Add((assembly, typeName, reader.GetString(method.Name), signature.ReturnType, signature.ParameterTypes.ToArray()));
        }
    }
}

int Count(BowgunHookSpec spec) => methods.Count(m => m.Assembly == spec.Assembly && m.Type == spec.Type &&
    m.Method == spec.Method && m.Return == spec.ReturnType && m.Args.SequenceEqual(spec.Parameters));

// Regression: this exact 0.9.6 managed-array signature compiled but failed to hook.
var previousBroken = new BowgunHookSpec("Il2Cppmoon.quantum.forsaken", "Il2CppQuantum.ArmamentAPI",
    "TryResolveActions", "System.Boolean", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef",
        "Il2CppQuantum.ArmamentActionType", "Il2CppQuantum.ArmamentAPI+ActionResolutionReason", "Il2CppQuantum.RuntimeActionInfo[]&" });
if (Count(previousBroken) != 0) throw new Exception("Regression fixture no longer matches the known broken signature");
Console.WriteLine("PASS: metadata gate rejects 0.9.6 managed-array hook signature.");

foreach (var spec in BowgunHookPlan.Hooks)
{
    if (Count(spec) != 1) throw new Exception("FAIL: hook signature does not uniquely exist: " + spec.Type + "." + spec.Method);
    Console.WriteLine("PASS: " + spec.Type + "." + spec.Method + "(" + string.Join(", ", spec.Parameters) + ") -> " + spec.ReturnType);
}

// Check the actual compiled callback against the same plan consumed at runtime.
using var pluginStream = File.OpenRead(args[1]);
using var pluginPe = new PEReader(pluginStream);
var plugin = pluginPe.GetMetadataReader();
foreach (var mh in plugin.MemberReferences)
{
    var member = plugin.GetMemberReference(mh);
    if (plugin.GetString(member.Name) == "get_m_localHeroRef")
        throw new Exception("FAIL: input still depends on unpopulated QuantumLocalInputSource.m_localHeroRef; use live bound hero identity");
}
Console.WriteLine("PASS: compiled plugin does not use the unpopulated input-source hero cache.");
var hookType = plugin.TypeDefinitions.Single(t => provider.GetTypeFromDefinition(plugin, t, 0) == "CrucibleUnlock.BowgunInputRepair");
foreach (var spec in BowgunHookPlan.Hooks)
{
    foreach (var callback in new[] { spec.Prefix, spec.Postfix }.Where(n => n != null))
    {
        var handle = plugin.GetTypeDefinition(hookType).GetMethods().Single(m => plugin.GetString(plugin.GetMethodDefinition(m).Name) == callback);
        var method = plugin.GetMethodDefinition(handle);
        var signature = method.DecodeSignature(provider, (object)null);
        foreach (var ph in method.GetParameters())
        {
            var parameter = plugin.GetParameter(ph);
            if (parameter.SequenceNumber == 0) continue;
            string name = plugin.GetString(parameter.Name);
            string actual = signature.ParameterTypes[parameter.SequenceNumber - 1];
            string expected = name == "__instance" ? spec.Type : name == "__result" ? spec.ReturnType + (actual.EndsWith("&") ? "&" : "") :
                name == "__state" ? "System.Boolean" + (callback == spec.Prefix ? "&" : "") :
                name.StartsWith("__") && int.TryParse(name.Substring(2), out int i) ? spec.Parameters[i] :
                throw new Exception("Unexpected callback parameter: " + name);
            if (actual != expected) throw new Exception("FAIL: " + callback + " " + name + " has " + actual + "; expected " + expected);
        }
        if (signature.ReturnType != "Void" && !(spec.Prefix == callback && signature.ReturnType == "System.Boolean"))
            throw new Exception("Invalid callback return type: " + callback);
        Console.WriteLine("PASS: compiled callback " + callback + " matches target parameter and result types.");
    }
}
Console.WriteLine($"PASS: all {BowgunHookPlan.Hooks.Length} registrations and callbacks; PE metadata only, no game assemblies loaded.");

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
