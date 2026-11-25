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
var bases = new Dictionary<string, string>();
foreach (string assembly in new[] { "Il2Cppmoon.quantum.forsaken", "Il2Cpp__forsaken" })
{
    using var s = File.OpenRead(Path.Combine(args[0], assembly + ".dll")); using var pe = new PEReader(s); var r = pe.GetMetadataReader();
    foreach (var th in r.TypeDefinitions)
    {
        string tn = p.GetTypeFromDefinition(r, th, 0); types.Add(tn);
        var bt = r.GetTypeDefinition(th).BaseType;
        if (!bt.IsNil) bases[tn] = bt.Kind == HandleKind.TypeDefinition ? p.GetTypeFromDefinition(r, (TypeDefinitionHandle)bt, 0)
            : bt.Kind == HandleKind.TypeReference ? p.GetTypeFromReference(r, (TypeReferenceHandle)bt, 0) : "?";
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
Check(!NecromancyPolicy.LifetimeExpired(0, 119L << 16, 120) && NecromancyPolicy.LifetimeExpired(0, 120L << 16, 120) &&
      NecromancyPolicy.LifetimeExpired(10L << 16, 130L << 16, 120) && !NecromancyPolicy.LifetimeExpired(0, 1000L << 16, 0) &&
      !NecromancyPolicy.LifetimeExpired(50L << 16, 10L << 16, 120), "minion lifetime: 16.16 seconds, 0 disables, clock going backwards never expires");
Check(methods.Count(m => m.Type == "Il2CppQuantum.DamageAPI" && m.Method == "Kill" && m.Args.Length == 8) == 1, "DamageAPI.Kill(8 args) exists for lifetime expiry");
Check(NecromancyPolicy.MinionPairIsAllied(true, false, false, true) && NecromancyPolicy.MinionPairIsAllied(false, true, true, false) &&
      NecromancyPolicy.MinionPairIsAllied(true, false, true, false) && !NecromancyPolicy.MinionPairIsAllied(false, true, false, true) &&
      !NecromancyPolicy.MinionPairIsAllied(true, false, false, false) && !NecromancyPolicy.MinionPairIsAllied(false, false, true, false),
      "friendly-fire rule: minion<->hero and minion<->minion allied; hero<->hero and minion<->enemy unchanged");
Check(NecromancyPolicy.ShouldGrantCharacter("hero-b", "hero-a") && !NecromancyPolicy.ShouldGrantCharacter("hero-b", "hero-a, hero-b") &&
      !NecromancyPolicy.ShouldGrantCharacter("", "") && NecromancyPolicy.AddCharacter("hero-a", "hero-b") == "hero-a,hero-b" &&
      NecromancyPolicy.AddCharacter("hero-a,hero-b", "hero-b") == "hero-a,hero-b" && NecromancyPolicy.AddCharacter("", "hero-b") == "hero-b",
      "per-character grant list: grants once per name, case-insensitive");
Check(NecromancyPolicy.SlotLayoutOk(16, 16, 72, 12, 8, 4, 0, 8, 12) && NecromancyPolicy.SlotLayoutOk(16, 16, 64, 64, 8, 3, 0, 4, 12) && !NecromancyPolicy.SlotLayoutOk(16, 16, 71, 12, 8, 4, 0, 8, 12) && !NecromancyPolicy.SlotLayoutOk(16, 20, 64, 64, 8, 3, 0, 4, 12) &&
      !NecromancyPolicy.SlotLayoutOk(16, 16, 40, 40, 8, 3, 0, 4, 12) && !NecromancyPolicy.SlotLayoutOk(16, 16, 64, 64, 8, 3, 0, 0, 12),
      "slot layout guard accepts a consistent layout and rejects size/overflow/overlap mismatches");
Check(bases.TryGetValue("Il2CppQuantum.WeaponStaticData", out var wb) && wb == "Il2CppQuantum.EquippableItemData" &&
      bases.TryGetValue("Il2CppQuantum.EquippableItemData", out var eb) && eb == "Il2CppQuantum.HeroItemData",
      "WeaponStaticData -> EquippableItemData -> HeroItemData (tome can be awarded as AssetRefHeroItemData)");
Check(NecromancyPolicy.Items.Length == NecromancyPolicy.ItemKeys.Length && NecromancyPolicy.Items.Distinct().Count() == NecromancyPolicy.Items.Length, "award list well-formed");

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
    foreach (var (type, name, sig) in new[] {

        ("Il2CppQuantum.RunesAPI+Weapon", "ApplyRuneModification", "Il2CppQuantum.Frame,Il2CppQuantum.RuneModificationType,Il2CppQuantum.EntityRef,System.Int32,Il2CppQuantum.EntityRef,Il2CppQuantum.EntityRef"),

        ("Il2CppQuantum.InventoryAPI", "GetFirstItemOfType", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef,Il2CppQuantum.HeroItemData"),

        ("Il2CppQuantum.ItemStatsSystem", "GetOrInitializeAllRunes", "Il2CppQuantum.Frame"),
        ("Il2CppQuantum.RunesAPI+Weapon", "HasRune", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef,System.Int32"),
        ("Il2CppQuantum.RunesAPI", "GetRune", "Il2CppQuantum.Frame,Il2CppQuantum.AssetRefHeroItemData"),
        ("Il2CppQuantum.RuntimeCharacterDataAPI", "TryGetCharacterRef", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef,Il2CppQuantum.RuntimeCharacterReference&"),
        ("Il2CppQuantum.SavesAPIReferenceExtensions", "GetName", "Il2CppQuantum.RuntimeCharacterReference,Il2CppQuantum.Frame"),
        ("Il2CppQuantum.InventoryAPI", "PreviewAwardItemImmediate", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef,Il2CppQuantum.AssetRefHeroItemData,System.Int32,Il2CppQuantum.NewInventoryItemSource") })
    {
        Check(methods.Count(m => m.Type == type && m.Method == name && string.Join(",", m.Args) == sig) == 1 && strings.Contains(name),
            "runed-tome call " + type + "." + name + "(" + sig + ") exists and is referenced");
    }
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
