using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

// PEReader and metadata decoding only. No Assembly.Load, dependency resolution,
// inspected method invocation, or game/Unity/IL2CPP assembly reference.
if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: RitualViewJitAudit <plugin.dll> [report.json]");
    return 2;
}
string inputPath = Path.GetFullPath(args[0]);
using var file = File.OpenRead(inputPath);
using var pe = new PEReader(file);
var reader = pe.GetMetadataReader();
var names = new Names();
const string target = "CrucibleUnlock.RitualViewRepair";
var failures = new List<string>();
var observations = new List<object>();
void Require(bool condition, string failure) { if (!condition) failures.Add(failure); }
bool NativeEnumerator(string value) => value.Contains("Il2CppSystem.Collections.Generic.List`1+Enumerator", StringComparison.Ordinal);
bool NativeArray(string value) => value.Contains("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArray", StringComparison.Ordinal);
var rootTypes = reader.TypeDefinitions.Where(h => names.Def(reader, h) == target).ToArray();
Require(rootTypes.Length == 1, "Expected one RitualViewRepair type.");
var types = reader.TypeDefinitions.Where(h => names.Def(reader, h) == target || names.Def(reader, h).StartsWith(target + "+", StringComparison.Ordinal)).ToArray();
var allMethods = types.SelectMany(h => reader.GetTypeDefinition(h).GetMethods()).ToArray();
var callbackHandles = allMethods.Where(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "AfterGetActionView").ToArray();
var helperHandles = allMethods.Where(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "RepairOffering").ToArray();
Require(callbackHandles.Length == 1, "Expected one AfterGetActionView callback.");
Require(helperHandles.Length == 1, "Expected one RepairOffering helper.");
int? callbackBytes = null;
string[] callbackLocals = Array.Empty<string>();
bool callsHelper = false;
int enumeratorReferences = 0;
int arrayEnumeratorReferences = 0;
int nativeArrayReferences = 0;
var prepareOffsets = new List<int>();
var patchOffsets = new List<int>();
var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
    .ToDictionary(c => unchecked((ushort)c.Value));

foreach (var handle in allMethods)
{
    var method = reader.GetMethodDefinition(handle);
    string name = reader.GetString(method.Name);
    string owner = names.Def(reader, method.GetDeclaringType());
    string location = owner + "::" + name;
    var signature = method.DecodeSignature(names, (object?)null);
    if (NativeArray(signature.ReturnType) || signature.ParameterTypes.Any(NativeArray))
    { nativeArrayReferences++; failures.Add("Native array method signature: " + location); }
    if (NativeEnumerator(signature.ReturnType) || signature.ParameterTypes.Any(NativeEnumerator))
    { enumeratorReferences++; failures.Add("Native List<T>.Enumerator method signature: " + location); }
    if (name == "AfterGetActionView")
    {
        var expected = new[] { "Il2CppMoon.Forsaken.EntityView", "Il2CppQuantum.ActionData", "Il2CppMoon.Forsaken.ActionView&" };
        Require(owner == target && signature.ReturnType == "Void" && signature.ParameterTypes.SequenceEqual(expected),
            "Callback exact parameter/return ABI changed: " + location);
        Require((method.Attributes & MethodAttributes.Static) != 0 && signature.GenericParameterCount == 0,
            "Callback must remain static and nongeneric.");
        string[] parameterNames = method.GetParameters().Select(reader.GetParameter)
            .Where(p => p.SequenceNumber != 0).OrderBy(p => p.SequenceNumber).Select(p => reader.GetString(p.Name)).ToArray();
        Require(parameterNames.SequenceEqual(new[] { "__instance", "__0", "__result" }), "Harmony callback parameter names changed.");
    }
    if (name == "RepairOffering")
    {
        Require((method.ImplAttributes & MethodImplAttributes.NoInlining) != 0, "RepairOffering missing NoInlining implementation flag.");
        Require((method.ImplAttributes & MethodImplAttributes.NoOptimization) == 0, "Experimental plan excludes NoOptimization.");
    }
    Require(method.RelativeVirtualAddress != 0, "Missing inspectable IL body: " + location);
    if (method.RelativeVirtualAddress == 0) continue;
    var body = pe.GetMethodBody(method.RelativeVirtualAddress);
    var il = body.GetILBytes() ?? Array.Empty<byte>();
    var locals = body.LocalSignature.IsNil ? Array.Empty<string>()
        : reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(names, (object?)null).ToArray();
    foreach (string local in locals.Where(NativeEnumerator))
    { enumeratorReferences++; failures.Add("Native List<T>.Enumerator local in " + location + ": " + local); }
    foreach (string local in locals.Where(NativeArray))
    { nativeArrayReferences++; failures.Add("Native array local in " + location + ": " + local); }
    if (name == "AfterGetActionView")
    {
        callbackBytes = il.Length; callbackLocals = locals;
        Require(il.Length <= 128, "Callback IL exceeds 128 bytes: " + il.Length);
        Require(!locals.Any(x => x.Contains("Il2CppSystem.Collections.Generic.", StringComparison.Ordinal)),
            "Callback locals still contain native generic collection bodies.");
        Require(body.ExceptionRegions.Length == 0, "Callback retains exception/repair machinery; expected filter and helper dispatch only.");
    }
    observations.Add(new { method = location, token = $"0x{MetadataTokens.GetToken(handle):x8}",
        il_bytes = il.Length, implementation_flags = method.ImplAttributes.ToString(), locals });
    for (int cursor = 0; cursor < il.Length;)
    {
        int offset = cursor;
        ushort opcode = il[cursor++];
        if (opcode == 0xfe)
        { if (cursor >= il.Length) throw new BadImageFormatException("Truncated two-byte opcode."); opcode = (ushort)(0xfe00 | il[cursor++]); }
        if (!opcodes.TryGetValue(opcode, out var code)) throw new BadImageFormatException("Unknown IL opcode.");
        int bytes = code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(il, cursor)),
            _ => 4
        };
        if (bytes < 0 || cursor + bytes > il.Length) throw new BadImageFormatException("Truncated IL operand.");
        if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineSig)
        {
            int token = BitConverter.ToInt32(il, cursor);
            string described = names.Entity(reader, MetadataTokens.EntityHandle(token));
            if (NativeArray(described))
            { nativeArrayReferences++; failures.Add($"Native array token in {location} IL_{offset:x4} {code.Name}: {described}"); }
            if (NativeEnumerator(described))
            { enumeratorReferences++; failures.Add($"Native List<T>.Enumerator token in {location} IL_{offset:x4} {code.Name}: {described}"); }
            if (described.Contains("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase`1", StringComparison.Ordinal) &&
                described.Contains("::GetEnumerator(", StringComparison.Ordinal))
            { arrayEnumeratorReferences++; failures.Add($"Il2CppArrayBase<T>.GetEnumerator token in {location} IL_{offset:x4} {code.Name}: {described}"); }
            if (owner == target && name == "Install" && code.OperandType == OperandType.InlineMethod &&
                (code == OpCodes.Call || code == OpCodes.Callvirt))
            {
                if (described.StartsWith("System.Runtime.CompilerServices.RuntimeHelpers::PrepareMethod(", StringComparison.Ordinal)) prepareOffsets.Add(offset);
                if (described.StartsWith("CrucibleUnlock.TrialRepairLog::Patch(", StringComparison.Ordinal)) patchOffsets.Add(offset);
            }
            if (name == "AfterGetActionView" && code.OperandType == OperandType.InlineMethod &&
                helperHandles.Length == 1 && token == MetadataTokens.GetToken(helperHandles[0]) && code == OpCodes.Call)
                callsHelper = true;
        }
        cursor += bytes;
    }
}
Require(callsHelper, "Callback does not directly dispatch to the inspected RepairOffering helper.");
Require(prepareOffsets.Count > 0, "Install does not call RuntimeHelpers.PrepareMethod.");
Require(patchOffsets.Count > 0, "Install does not call TrialRepairLog.Patch.");
Require(prepareOffsets.Count > 0 && patchOffsets.Count > 0 && prepareOffsets.Max() < patchOffsets.Min(),
    "Install PrepareMethod call must precede its first TrialRepairLog.Patch call in IL order.");
Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) == true ||
    a.GetName().Name?.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) == true), "An Il2Cpp/Unity assembly is loaded into the audit process.");
string hash;
using (var hashFile = File.OpenRead(inputPath))
using (var sha = SHA256.Create()) hash = Convert.ToHexString(sha.ComputeHash(hashFile)).ToLowerInvariant();
var report = new { input = inputPath, sha256 = hash, metadata_only = true, target, callback_il_bytes = callbackBytes,
    callback_locals = callbackLocals, callback_calls_helper = callsHelper, native_enumerator_reference_count = enumeratorReferences,
    il2cpp_array_getenumerator_reference_count = arrayEnumeratorReferences, install_preparemethod_il_offsets = prepareOffsets,
    native_array_reference_count = nativeArrayReferences,
    install_patch_il_offsets = patchOffsets, install_prepare_before_patch = prepareOffsets.Count > 0 && patchOffsets.Count > 0 && prepareOffsets.Max() < patchOffsets.Min(),
    method_count = allMethods.Length, methods = observations, passed = failures.Count == 0, failures,
    limitation = "This checks emitted IL shape only. It neither executes game DLLs nor proves the CoreCLR JIT crash is fixed." };
if (args.Length == 2) File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Callback IL: {callbackBytes?.ToString() ?? "missing"} bytes; native Enumerator references: {enumeratorReferences}; array GetEnumerator references: {arrayEnumeratorReferences}; native array references: {nativeArrayReferences}; inspected methods: {allMethods.Length}.");
Console.WriteLine("Install PrepareMethod offsets: " + string.Join(",", prepareOffsets.Select(x => $"IL_{x:x4}")) + "; Patch offsets: " + string.Join(",", patchOffsets.Select(x => $"IL_{x:x4}")) + ".");
foreach (string failure in failures) Console.WriteLine("FAIL: " + failure);
Console.WriteLine(failures.Count == 0 ? "PASS: exact callback ABI, short callback, NoInlining helper, no native arrays/collection enumerators and PrepareMethod before Patch; PE metadata only." : $"RED: {failures.Count} audit failures; PE metadata only.");
Console.WriteLine("No game DLL execution; this result does not establish a live JIT fix.");
return failures.Count == 0 ? 0 : 1;

sealed class Names : ISignatureTypeProvider<string, object?>
{
    public string Entity(MetadataReader r, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => Def(r, (TypeDefinitionHandle)h),
        HandleKind.TypeReference => GetTypeFromReference(r, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(r, null, (TypeSpecificationHandle)h, 0),
        HandleKind.MethodDefinition => Method(r, (MethodDefinitionHandle)h),
        HandleKind.MemberReference => Member(r, (MemberReferenceHandle)h),
        HandleKind.MethodSpecification => Spec(r, (MethodSpecificationHandle)h),
        HandleKind.FieldDefinition => Field(r, (FieldDefinitionHandle)h),
        HandleKind.StandaloneSignature => Standalone(r, (StandaloneSignatureHandle)h),
        _ => h.Kind.ToString()
    };
    string Method(MetadataReader r, MethodDefinitionHandle h)
    { var m = r.GetMethodDefinition(h); return Def(r, m.GetDeclaringType()) + "::" + r.GetString(m.Name) + Signature(m.DecodeSignature(this, null)); }
    string Member(MetadataReader r, MemberReferenceHandle h)
    { var m = r.GetMemberReference(h); return Entity(r, m.Parent) + "::" + r.GetString(m.Name) + (m.GetKind() == MemberReferenceKind.Method ? Signature(m.DecodeMethodSignature(this, null)) : ":" + m.DecodeFieldSignature(this, null)); }
    string Spec(MetadataReader r, MethodSpecificationHandle h)
    { var m = r.GetMethodSpecification(h); return Entity(r, m.Method) + "<" + string.Join(",", m.DecodeSignature(this, null)) + ">"; }
    string Field(MetadataReader r, FieldDefinitionHandle h)
    { var f = r.GetFieldDefinition(h); return Def(r, f.GetDeclaringType()) + "::" + r.GetString(f.Name) + ":" + f.DecodeSignature(this, null); }
    string Standalone(MetadataReader r, StandaloneSignatureHandle h)
    { var s = r.GetStandaloneSignature(h); return s.GetKind() == StandaloneSignatureKind.LocalVariables ? string.Join(",", s.DecodeLocalSignature(this, null)) : Signature(s.DecodeMethodSignature(this, null)); }
    static string Signature(MethodSignature<string> s) => "(" + string.Join(",", s.ParameterTypes) + ")->" + s.ReturnType;
    public string Def(MetadataReader r, TypeDefinitionHandle h)
    { var t = r.GetTypeDefinition(h); var p = t.GetDeclaringType(); return p.IsNil ? r.GetString(t.Namespace) + "." + r.GetString(t.Name) : Def(r, p) + "+" + r.GetString(t.Name); }
    public string GetArrayType(string t, ArrayShape s) => t + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string t) => t + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr" + Signature(s);
    public string GetGenericInstantiation(string t, ImmutableArray<string> a) => t + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string t, bool required) => t + " mod(" + m + ")";
    public string GetPinnedType(string t) => t + " pinned";
    public string GetPointerType(string t) => t + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c == PrimitiveTypeCode.Void ? "Void" : "System." + c;
    public string GetSZArrayType(string t) => t + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => Def(r, h);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    { var t = r.GetTypeReference(h); return t.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(r, (TypeReferenceHandle)t.ResolutionScope, 0) + "+" + r.GetString(t.Name) : r.GetString(t.Namespace) + "." + r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
