using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Necromancy;

// Metadata only: never loads an inspected game or plugin assembly.
// args: <vendor/generated dir> <built NRFWNecromancy.dll>
var p = new Names();
var methods = new List<(string Type, string Method, string Return, string[] Args, bool Static)>();
var layoutFields = new[]
{
    (Type: "Il2CppQuantum.Damage", Field: "HitEntity", FieldType: "Il2CppQuantum.EntityRef", Offset: 0x28),
    (Type: "Il2CppQuantum.Damage", Field: "Proxy", FieldType: "Il2CppQuantum.PayloadProxyGroup", Offset: 0xb8),
    (Type: "Il2CppQuantum.DamageResponse", Field: "Type", FieldType: "Il2CppQuantum.DamagePayloadResponseType", Offset: 8),
    (Type: "Il2CppQuantum.RaisedMinionComponent", Field: "Owner", FieldType: "Il2CppQuantum.EntityRef", Offset: 0x10),
    (Type: "Il2CppQuantum.RaisedMinionComponent", Field: "FollowStartDistance", FieldType: "Il2CppPhoton.Deterministic.FP", Offset: 0x38),
    (Type: "Il2CppQuantum.RaisedMinionComponent", Field: "MaxDistanceBeforeTeleport", FieldType: "Il2CppPhoton.Deterministic.FP", Offset: 0x50),
    (Type: "Il2CppQuantum.TransformComponent", Field: "RawPosition", FieldType: "Il2CppPhoton.Deterministic.FPVector3", Offset: 0x08),
    (Type: "Il2CppQuantum.ProjectileComponent", Field: "InstigatorRef", FieldType: "Il2CppQuantum.EntityRef", Offset: 0x30),
    (Type: "Il2CppQuantum.CascadeInstanceComponent", Field: "Caster", FieldType: "Il2CppQuantum.EntityRef", Offset: 0x40),
    // T-016 round 3: Boss-rune minions get NavMeshTracker dynamic search; nav index is read for the log.
    (Type: "Il2CppQuantum.NavMeshTrackerComponent", Field: "NavMeshDynamicSearch", FieldType: "Il2CppQuantum.QBoolean", Offset: 0x08),
    (Type: "Il2CppQuantum.NavMeshTrackerComponent", Field: "State", FieldType: "Il2CppQuantum.NavMeshTrackerState", Offset: 0),
    (Type: "Il2CppQuantum.UseNavDataComponent", Field: "NavDataIndex", FieldType: "Il2CppQuantum.RuntimeNavigationDataRef", Offset: 0),
    (Type: "Il2CppQuantum.RuntimeNavigationDataRef", Field: "m_index", FieldType: "System.Int32", Offset: 0)
};
var roarFields = new[]
{
    (Type: "Il2CppQuantum.PerformGesturePlayerCommand", Field: "GestureData", FieldType: "Il2CppQuantum.AssetRefCollectableGestureData", Offset: 0),
    (Type: "Il2CppQuantum.AssetRefCollectableGestureData", Field: "Id", FieldType: "Il2CppQuantum.AssetGuid", Offset: 0),
    (Type: "Il2CppQuantum.AssetGuid", Field: "Value", FieldType: "System.Int64", Offset: 0),
    (Type: "Il2CppQuantum.EntityRef", Field: "Raw", FieldType: "System.UInt64", Offset: 0),
    (Type: "Il2CppPhoton.Deterministic.FP", Field: "RawValue", FieldType: "System.Int64", Offset: 0),
};
const string Q = "Il2CppQuantum.";
const string Arrays = "Il2CppInterop.Runtime.InteropTypes.Arrays.";
const string Collections = "Il2CppSystem.Collections.Generic.";
const string Storage = Q + "InstanceDataStorageSystem";
const string Resolver = "Il2Cpp.RuntimeAssetResolutionContext";
const string Frame = Q + "Frame";
const string Guid = Q + "AssetGuid";
const string Entity = Q + "EntityRef";
const string Asset = Q + "AssetObject";
const string Serializer = Q + "IAssetSerializer";
const string Bytes = Arrays + "Il2CppStructArray`1<System.Byte>";
const string Actions = Arrays + "Il2CppStructArray`1<Il2CppQuantum.AssetRefActionData>";
const string Classes = Arrays + "Il2CppStructArray`1<Il2CppQuantum.WeaponClass>";
string[] bossTables = { "RuneData", "ItemData", "CommodityData", "ItemAttachments" };
(string Type, string Field, string FieldType, int Offset)[] bossFields = bossTables
    .Select(t => (Type: Q + t + "Id", Field: "InternalId", FieldType: "System.Int64", Offset: 0))
    .Concat(new[] { "AssetRefActionData", "AssetRefHeroItemData", "AssetRefHeroRuneData", "AssetRefNpcData" }
        .Select(t => (Type: Q + t, Field: "Id", FieldType: Guid, Offset: 0)))
    .Concat(new[] {
        (Q + "RuneDataRef", "m_index", "System.Int32", 0),
        (Q + "RaisedMinionComponent", "RaiseActionGuid", Guid, 8),
        (Q + "QBoolean", "Value", "System.Int32", 0),
        (Q + "InventoryEnumeratorEntry", "Entity", Entity, 0),
    }).ToArray();
var checkedFields = layoutFields.Concat(roarFields).Concat(bossFields).ToLookup(field => field.Type);
var layouts = new HashSet<(string Type, string Field, string FieldType, int Offset)>();
var types = new HashSet<string>();
var bases = new Dictionary<string, string>();
var assemblies = new List<string> { "Il2Cppmoon.quantum.forsaken", "Il2Cpp__forsaken", "Il2CppQuantum.Engine",
    "Il2CppQuantum.Deterministic", "Il2CppInterop.Runtime", "Il2Cppmscorlib", "UnityEngine.CoreModule" };
bool bossPng = File.Exists(Path.Combine(args[0], "UnityEngine.ImageConversionModule.dll"));
if (bossPng) assemblies.Add("UnityEngine.ImageConversionModule");
foreach (string assembly in assemblies)
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
            methods.Add((tn, r.GetString(m.Name), sg.ReturnType, sg.ParameterTypes.ToArray(), m.Attributes.HasFlag(System.Reflection.MethodAttributes.Static)));
        }
        foreach (var candidate in checkedFields[tn])
        {
            foreach (var handle in r.GetTypeDefinition(th).GetFields())
            {
                var field = r.GetFieldDefinition(handle);
                if (r.GetString(field.Name) == candidate.Field)
                    layouts.Add((tn, candidate.Field, field.DecodeSignature(p, (object)null), field.GetOffset()));
            }
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
void BossCall(string type, string name, string result, bool isStatic, params string[] parameters) =>
    Check(methods.Count(m => m.Type == type && m.Method == name && m.Return == result && m.Static == isStatic &&
        m.Args.SequenceEqual(parameters)) == 1, $"boss-rune call {type}.{name}({string.Join(",", parameters)}) -> {result}");
foreach (var field in bossFields) Check(layouts.Contains(field), "boss-rune field " + field.Type + "." + field.Field);
foreach (var t in bossTables)
{
    BossCall(Q + t, ".ctor", "Void", false);
    BossCall(Storage, "get_m_storage" + t, Storage + "+Storage`1<" + Q + t + ">", false);
    BossCall(Storage, "Add", Q + t + "Ref", false, Q + t);
    BossCall(Storage, "Resolve", Q + t + "Ref", false, Q + t + "Id");
    BossCall(Q + t + "Ref", "Get", Q + t, false, Frame);
}
foreach (var t in new[] { "RuneData", "ItemData", "CommodityData", "UpgradesData", "WeaponData", "EquipmentData", "ArmorData", "CraftableData" })
    BossCall(Q + t + "Ref", "get_IsDefault", "System.Boolean", false);
foreach (var (type, name, result, get, set) in new[]
{
    (Asset, "Guid", Guid, false, true), (Asset, "Path", "System.String", false, true),
    (Q + "HeroRuneData", "Actions", Actions, true, true),
    (Q + "HeroRuneData", "CompatibleClasses", Classes, true, false),
    (Q + "HeroRuneData", "IsUtility", "System.Boolean", true, false),
    (Q + "RuneData", "Actions", Actions, true, true),
    (Q + "RuneData", "CompatibleClasses", Classes, false, true),
    (Q + "RuneData", "IsUtility", "System.Boolean", false, true),
    (Q + "RuneData", "OldData", Q + "AssetRefHeroRuneData", true, true),
    (Q + "RaiseMinionActionData", "MinionNpcData", Q + "AssetRefNpcData", true, true),
    (Q + "RaiseMinionActionData", "UseCorpseNpcType", "System.Boolean", true, true),
    (Q + "RaiseMinionActionData", "MaxActiveMinions", "System.Int32", true, true),
    (Q + "RaiseMinionActionData", "HealthMultiplierBoss", "Il2CppPhoton.Deterministic.FP", true, true),
    (Q + "RaiseMinionActionData", "HealthMultiplierGiant", "Il2CppPhoton.Deterministic.FP", true, true),
    (Q + "RaiseMinionActionData", "HealthMultiplierElite", "Il2CppPhoton.Deterministic.FP", true, true),
    (Q + "RaiseMinionActionData", "HealthMultiplierNormal", "Il2CppPhoton.Deterministic.FP", true, true),
    (Q + "ItemData", "OldItemData", Q + "AssetRefHeroItemData", true, true),
    (Q + "ItemAttachments", "Guid", Guid, true, true),
    (Q + "ItemAttachments", "OldItemId", Q + "AssetRefHeroItemData", true, true),
    ("Il2Cpp.AssetBase", "AssetGuid", Guid, true, true),
    ("Il2Cpp.AssetBase", "AssetObject", Asset, true, false),
})
{
    if (get) BossCall(type, "get_" + name, result, false);
    if (set) BossCall(type, "set_" + name, "Void", false, result);
}
foreach (var t in new[] { "RuneData", "ItemData", "CommodityData", "UpgradesData", "WeaponData", "EquipmentData", "ArmorData", "CraftableData" })
{
    BossCall(Q + "ItemAttachments", "get_" + t, Q + t + "Ref", false);
    if (bossTables.Contains(t)) BossCall(Q + "ItemAttachments", "set_" + t, "Void", false, Q + t + "Ref");
}
foreach (var (name, type) in new[] { ("MaxItemsInStack", "System.Int32"), ("SalePrice", "System.Int32"), ("ItemValue", "System.Int32"),
             ("CanBeSold", "System.Boolean"), ("CanBeDestroyed", "System.Boolean"), ("FilterType", Q + "SecondaryItemType") })
{
    BossCall(Q + "CommodityData", "get_" + name, type, false);
    BossCall(Q + "CommodityData", "set_" + name, "Void", false, type);
}
foreach (var (type, name, result, isStatic, parameters) in new[]
{
    (Frame, "get_InstanceDataStorage", Storage, false, ""),
    (Q + "Core.FrameBase", "get_DynamicAssetDB", Q + "DynamicAssetDB", false, ""),
    (Q + "Core.FrameBase", "FindAsset", "!!0", false, Guid),
    (Q + "Core.FrameBase", "AddAsset", "Void", false, Asset + "," + Guid),
    (Q + "Core.FrameBase", "Exists", "System.Boolean", false, Entity),
    (Q + "DynamicAssetDB", "FindAsset", Asset, false, Guid),
    (Q + "Core.FrameContext", "get_AssetSerializer", Serializer, false, ""),
    (Q + "IAssetSerializerExtensions", "AssetToByteArray", Bytes, true, Serializer + "," + Asset),
    (Q + "IAssetSerializerExtensions", "AssetsFromByteArray", Arrays + "Il2CppReferenceArray`1<" + Asset + ">", true, Serializer + "," + Bytes),
    (Arrays + "Il2CppArrayBase`1", "get_Length", "System.Int32", false, ""),
    (Arrays + "Il2CppReferenceArray`1", "get_Item", "!0", false, "System.Int32"),
    (Arrays + "Il2CppStructArray`1", "get_Item", "!0", false, "System.Int32"),
    (Arrays + "Il2CppStructArray`1", "set_Item", "Void", false, "System.Int32,!0"),
    ("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", "get_Pointer", "System.IntPtr", false, ""),
    (Storage + "+Storage`1", "get_m_lookup", Collections + "Dictionary`2<System.Int64,System.Int32>", false, ""),
    (Q + "EntityRefExtensions", "Has", "System.Boolean", true, Entity + "," + Frame),
    (Q + "EntityRefExtensions", "Get", "!!0*", true, Entity + "," + Frame),
    (Q + "RunesAPI", "GetRune", Q + "RuneDataRef", true, Frame + "," + Q + "AssetRefHeroItemData"),
    (Q + "RunesAPI+Weapon", "TryFindRune", "System.Boolean", true, Frame + "," + Entity + "," + Q + "RuneDataRef,System.Int32&"),
    (Q + "InventoryAPI", "GetInventoryEntity", Entity, true, Frame + "," + Entity),
    (Q + "InventoryAPI", "GetItemMatchingCount", "System.Int32", true, Frame + "," + Entity + "," + Q + "AssetRefHeroItemData,System.Boolean"),
    (Q + "InventoryAPI", "PreviewAwardItemImmediate", "Void", true, Frame + "," + Entity + "," + Q + "AssetRefHeroItemData,System.Int32," + Q + "NewInventoryItemSource"),
    (Q + "InventoryAPI", "GetInventoryItems", Q + "InventoryEnumerable", true, Frame + "," + Entity + ",System.Boolean,System.Boolean"),
    (Q + "InventoryEnumerable", "GetEnumerator", Q + "InventoryEnumerator", false, ""),
    (Q + "InventoryEnumerator", "MoveNext", "System.Boolean", false, ""),
    (Q + "InventoryEnumerator", "get_Current", Q + "InventoryEnumeratorEntry", false, ""),
    (Q + "EquipmentAPI", "GetItemEntity", Entity, true, Frame + "," + Q + "EquipmentSlot," + Entity),
    ("Il2CppMoon.Forsaken.Core", "get_AssetCtx", "Il2Cpp.IAssetWrapperResolver", true, ""),
    ("Il2CppMoon.Forsaken.Core", "get_ResourceManager", Q + "IResourceManager", true, ""),
    (Resolver, "TryFindWrapperImpl", "System.Boolean", false, Guid + ",Il2Cpp.AssetBase&,System.Boolean"),
    (Resolver, "get_m_allAssets", Collections + "List`1<" + Asset + ">", false, ""),
    (Resolver, "get_m_assetByGuid", Collections + "Dictionary`2<" + Guid + "," + Asset + ">", false, ""),
    (Resolver, "get_m_loadedWrappers", Collections + "Dictionary`2<" + Guid + ",Il2Cpp.AssetBase>", false, ""),
    (Resolver, "get_m_wrappersBeingLoaded", Collections + "Dictionary`2<" + Guid + ",Il2CppMoon.Collections.DenseStorageHandle>", false, ""),
    (Collections + "Dictionary`2", "ContainsKey", "System.Boolean", false, "!0"),
    (Collections + "Dictionary`2", "TryGetValue", "System.Boolean", false, "!0,!1&"),
    (Collections + "Dictionary`2", "Add", "Void", false, "!0,!1"),
    (Collections + "Dictionary`2", "Remove", "System.Boolean", false, "!0"),
    (Collections + "List`1", "Add", "Void", false, "!0"),
    (Collections + "List`1", "Remove", "System.Boolean", false, "!0"),
    ("UnityEngine.Object", "Instantiate", "!!0", true, "!!0"),
    ("UnityEngine.Object", "Destroy", "Void", true, "UnityEngine.Object"),
    ("UnityEngine.Object", "set_hideFlags", "Void", false, "UnityEngine.HideFlags"),
    ("UnityEngine.Object", "op_Equality", "System.Boolean", true, "UnityEngine.Object,UnityEngine.Object"),
    ("UnityEngine.Object", "op_Inequality", "System.Boolean", true, "UnityEngine.Object,UnityEngine.Object"),
}) BossCall(type, name, result, isStatic, parameters.Length == 0 ? Array.Empty<string>() : parameters.Split(','));
if (bossPng)
{
    BossCall("UnityEngine.ImageConversion", "LoadImage", "System.Boolean", true, "UnityEngine.Texture2D", Bytes, "System.Boolean");
    BossCall(Arrays + "Il2CppStructArray`1", ".ctor", "Void", false, "!0[]");
    BossCall("UnityEngine.Texture2D", ".ctor", "Void", false, "System.Int32", "System.Int32");
    BossCall("UnityEngine.Texture", "get_width", "System.Int32", false);
    BossCall("UnityEngine.Texture", "get_height", "System.Int32", false);
    BossCall("UnityEngine.Rect", ".ctor", "Void", false, "System.Single", "System.Single", "System.Single", "System.Single");
    BossCall("UnityEngine.Vector2", ".ctor", "Void", false, "System.Single", "System.Single");
    BossCall("UnityEngine.Sprite", "Create", "UnityEngine.Sprite", true, "UnityEngine.Texture2D", "UnityEngine.Rect", "UnityEngine.Vector2", "System.Single");
    BossCall("Il2Cpp.HeroItemDataAsset", "set_ItemIcon", "Void", false, "UnityEngine.Sprite");
    BossCall("Il2Cpp.HeroItemDataAsset", "set_ItemTexture", "Void", false, "UnityEngine.Sprite");
    BossCall("Il2Cpp.AnimatedActionDataCommonAsset", "set_Icon", "Void", false, "UnityEngine.Sprite");
}

var nativeEntrypoints = new[]
{
    (Type: "Il2CppQuantum.DamageResolverSystem", Name: "ResolveDamage", Return: "Void", Static: true,
        Args: new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.Damage&", "Il2CppQuantum.DamageResponse&" }),
    (Type: "Il2CppQuantum.ProjectileData", Name: "OnCollisionWithDamageReceiver", Return: "Void", Static: false,
        Args: new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.EntityRef",
            "Il2CppPhoton.Deterministic.FPVector3", "Il2CppPhoton.Deterministic.FPVector3" }),
    (Type: "Il2CppQuantum.PayloadProxyGroup", Name: "TryGetProxyOfType", Return: "System.Boolean", Static: false,
        Args: new[] { "Il2CppQuantum.ProxyTypeFlags", "Il2CppQuantum.EntityRef&" })
};
foreach (var entry in nativeEntrypoints)
    Check(methods.Count(m => m.Type == entry.Type && m.Method == entry.Name && m.Return == entry.Return &&
        m.Static == entry.Static && m.Args.SequenceEqual(entry.Args)) == 1, "native damage entry " + entry.Type + "." + entry.Name);
foreach (var field in layoutFields)
    Check(layouts.Contains(field), "native field layout " + field.Type + "." + field.Field);
foreach (var field in roarFields)
    Check(layouts.Contains(field), "roar field " + field.Type + "." + field.Field);

foreach (var (type, name, result, isStatic, parameters) in new[]
{
    ("Il2CppQuantum.Frame", "GetPlayerHero", "Il2CppQuantum.EntityRef", false, "Il2CppQuantum.PlayerRef"),
    ("Il2CppQuantum.PlayerRef", "op_Implicit", "Il2CppQuantum.PlayerRef", true, "System.Int32"),
    ("Il2CppQuantum.EntityRef", "get_IsValid", "System.Boolean", false, ""),
    ("Il2CppPhoton.Deterministic.DeterministicFrame", "get_IsVerified", "System.Boolean", false, ""),
    ("Il2CppQuantum.Frame", "get_TimeSinceStart", "Il2CppPhoton.Deterministic.FP", false, ""),
    ("Il2CppQuantum.Frame", "get_Context", "Il2CppQuantum.FrameContextUser", false, ""),
    ("Il2CppQuantum.Core.FrameContext", "get_ResourceManager", "Il2CppQuantum.IResourceManager", false, ""),
    ("Il2CppQuantum.IResourceManager", "GetAsset", "Il2CppQuantum.AssetObject", false, "Il2CppQuantum.AssetGuid"),
    ("Il2CppQuantum.IResourceManager", "FindNextAssetGuid", "System.Boolean", false, "Il2CppQuantum.AssetObjectQuery&,Il2CppQuantum.AssetGuid&"),
    ("Il2CppQuantum.AssetObjectQuery", "op_Implicit", "Il2CppQuantum.AssetObjectQuery", true, "Il2CppSystem.Type"),
    ("Il2CppQuantum.AssetObjectQuery", "Dispose", "Void", false, ""),
    ("Il2CppInterop.Runtime.Il2CppType", "Of", "Il2CppSystem.Type", true, ""),
    ("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", "TryCast", "!!0", false, ""),
    ("Il2CppQuantum.AssetObject", "get_Path", "System.String", false, ""),
    ("Il2CppQuantum.AssetObject", "get_Guid", "Il2CppQuantum.AssetGuid", false, ""),
    ("Il2CppQuantum.Core.FrameBase", "GetComponentIterator", "Il2CppQuantum.ComponentIterator`1<!!0>", false, ""),
    ("Il2CppQuantum.ComponentIterator`1", "GetEnumerator", "Il2CppQuantum.ComponentIterator`1+Enumerator<!0>", false, ""),
    ("Il2CppQuantum.ComponentIterator`1+Enumerator", "MoveNext", "System.Boolean", false, ""),
    ("Il2CppQuantum.ComponentIterator`1+Enumerator", "get_Current", "Il2CppQuantum.EntityComponentPair`1<!0>", false, ""),
    ("Il2CppQuantum.EntityComponentPair`1", "get_Entity", "Il2CppQuantum.EntityRef", false, ""),
    ("Il2CppQuantum.EntityComponentPair`1", "get_Component", "!0", false, ""),
    ("Il2CppQuantum.EntityRefExtensions", "GetData", "!!0", true, "Il2CppQuantum.EntityRef,Il2CppQuantum.Frame"),
    ("Il2CppQuantum.ActionComponent", "Execute", "System.Int32", false,
     "Il2CppQuantum.Frame,Il2CppQuantum.ActionData,Il2CppQuantum.ActionComponent+BeforeActionExecutedDelegate,System.Boolean," +
     "Il2CppPhoton.Deterministic.FP,System.Int32,System.Boolean,System.Boolean,Il2CppQuantum.RuntimeActionAndOverrideRef"),
})
    Check(methods.Count(m => m.Type == type && m.Method == name && m.Return == result && m.Static == isStatic &&
        string.Join(",", m.Args) == parameters) == 1, $"roar call {type}.{name}({parameters}) -> {result}");

Check(NecromancyPolicy.IsWarrickNpc(2680182020363611965) && NecromancyPolicy.IsWarrickNpc(670058593051072033) &&
      !NecromancyPolicy.IsWarrickNpc(NecromancyPolicy.WildlingSwordNpc) && !NecromancyPolicy.IsWarrickNpc(0),
      "roar minion: only the two Warrick NpcData GUIDs qualify");
Check(NecromancyPolicy.RoarAssetGuid(" 123 ") == 123 && NecromancyPolicy.RoarAssetGuid("0") == 0 &&
      NecromancyPolicy.RoarAssetGuid("-5") == 0 && NecromancyPolicy.RoarAssetGuid("  ") == 0 &&
      NecromancyPolicy.RoarAssetGuid(null) == 0 && NecromancyPolicy.RoarAssetGuid("roar") == null,
      "roar setting: positive decimal GUID or keyword; blank/nonpositive rejected");
Check(NecromancyPolicy.MatchesRoarAsset("  ROAR ", 123, "gestures/heroRoarData") &&
      NecromancyPolicy.MatchesRoarAsset("123", 123, null) &&
      NecromancyPolicy.MatchesRoarAsset("mutantSoldierSwordAOEInnerData", 123, "multiplayerActions/mutantSoldierSwordAOEInnerData") &&
      !NecromancyPolicy.MatchesRoarAsset("mutantSoldierSwordAOEInnerData", 124, "multiplayerActions/mutantSoldierSwordAOEOuterData") &&
      NecromancyPolicy.MatchesRoarAsset(NecromancyPolicy.DefaultRoarGesture, 2126111680215882670, "") && !NecromancyPolicy.MatchesRoarAsset(NecromancyPolicy.DefaultRoarGesture, 1770795828947168763, "") &&
      NecromancyPolicy.MatchesRoarAsset(NecromancyPolicy.DefaultRoarAction, 4386907888524794362, null) && !NecromancyPolicy.MatchesRoarAsset(NecromancyPolicy.DefaultRoarAction, 3660829114871757151, null) &&
      !NecromancyPolicy.MatchesRoarAsset("roar", 123, "gestures/wave") && !NecromancyPolicy.MatchesRoarAsset("roar", 123, null) &&
      !NecromancyPolicy.MatchesRoarAsset("123", 124, "gestures/123") && !NecromancyPolicy.MatchesRoarAsset("roar", 0, "roar") &&
      !NecromancyPolicy.MatchesRoarAsset("-5", 123, "gestures/-5") && !NecromancyPolicy.MatchesRoarAsset("  ", 123, "roar"),
      "roar match: exact GUID or case-insensitive path keyword; no path fallback for numeric settings");
Check(!NecromancyPolicy.LifetimeExpired(0, (3L << 16) - 1, NecromancyPolicy.RoarCooldownSeconds) &&
      NecromancyPolicy.LifetimeExpired(0, 3L << 16, NecromancyPolicy.RoarCooldownSeconds), "roar cooldown: exactly 3 sim seconds");

// T-014: the default reaction table is T-012's rule, so behaviour with a fresh file is unchanged.
{
    var table = GestureReactions.Parse(GestureReactions.DefaultJson(2126111680215882670, 4386907888524794362), out string tableError);
    Check(table != null && table.Errors.Length == 0 && table.Rules.Length == 1 &&
          GestureReactions.Match(table, 2126111680215882670, NecromancyPolicy.WarrickBossNpc, null, "Boss")?.Actions.SequenceEqual(new[] { 4386907888524794362 }) == true &&
          GestureReactions.Match(table, 2126111680215882670, NecromancyPolicy.WildlingSwordNpc, null, "Normal") == null,
          "default reaction table = T-012 Warrick rule (" + (tableError ?? "ok") + ")");
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
Check(methods.Any(m => m.Type == "Il2CppQuantum.RaiseMinionActionData" && m.Method == "get_MaxDistanceBeforeTeleport" && m.Return == "Il2CppPhoton.Deterministic.FP") &&
      methods.Any(m => m.Type == "Il2CppQuantum.RaiseMinionActionData" && m.Method == "set_MaxDistanceBeforeTeleport" && m.Args.SequenceEqual(new[] { "Il2CppPhoton.Deterministic.FP" })),
      "RaiseMinionActionData.MaxDistanceBeforeTeleport FP get/set");
Check(NecromancyPolicy.ScaledTeleportRaw(20L << 16, 3f) == 60L << 16 && NecromancyPolicy.ScaledTeleportRaw(20L << 16, 1f) == 20L << 16 &&
      NecromancyPolicy.ScaledTeleportRaw(20L << 16, 0f) == 20L << 16 && NecromancyPolicy.ScaledTeleportRaw(20L << 16, float.NaN) == 20L << 16 &&
      NecromancyPolicy.ScaledTeleportRaw(20L << 16, 1000f) == 1000L << 16 && NecromancyPolicy.ScaledTeleportRaw(0, 3f) == 0,
      "teleport distance: 3x of 20 m is 60 m; <=0/NaN keep shipped value; capped at 50x");
Check(NecromancyPolicy.ShouldHoldStuckTeleport(false, true, true, 11L << 16, 10, 20L << 16, null) &&
      NecromancyPolicy.ShouldHoldStuckTeleport(true, true, true, 11L << 16, 10, 20L << 16, null),
      "stuck teleport hold: near owner always holds, even following or past cap without a hit");
Check(NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 11L << 16, 10, 20L << 16, 16L << 16) &&
      !NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 11L << 16, 10, 20L << 16, null) &&
      !NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 10L << 16, 10, 20L << 16, 14L << 16) &&
      NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 9L << 16, 10, 20L << 16, null) &&
      NecromancyPolicy.ShouldHoldStuckTeleport(false, true, false, 11L << 16, 0, 20L << 16, null) &&
      NecromancyPolicy.ShouldHoldStuckTeleport(true, true, false, 5L << 16, 10, 20L << 16, null) &&
      !NecromancyPolicy.ShouldHoldStuckTeleport(true, true, false, 11L << 16, 10, 20L << 16, 19L << 16) &&
      !NecromancyPolicy.ShouldHoldStuckTeleport(true, true, false, 11L << 16, 0, 20L << 16, null) &&
      NecromancyPolicy.ShouldHoldStuckTeleport(true, true, false, 9L << 16, 0, 20L << 16, null) &&
      !NecromancyPolicy.ShouldHoldStuckTeleport(false, false, false, 0, 0, 20L << 16, 20L << 16),
      "stuck teleport hold beyond near radius: off-follow and in range, recent hit or below cap, 0 uncaps");
Check(NecromancyPolicy.EntityLabel(9223372084099418004UL) == "E.01940.011" && NecromancyPolicy.EntityLabel(9223372118459156023UL) == "E.01591.019",
      "entity label matches the Unity log form E.index.version");
Check(types.Contains("Il2CppQuantum.UseNavDataComponent") && types.Contains("Il2CppQuantum.CorpseMarkerComponent"), "UseNavDataComponent / CorpseMarkerComponent exist");
foreach (var (type, name, result, parameters) in new[]
{
    ("Il2CppQuantum.ComponentTypeId", "GetComponentIndex", "System.Int32", "System.String"),
    ("Il2CppQuantum.Core.FrameBase", "Has", "System.Boolean", "Il2CppQuantum.EntityRef,System.Int32"),
    ("Il2CppQuantum.Core.FrameBase", "GetPointer", "Void*", "Il2CppQuantum.EntityRef,System.Int32"),
})
    Check(methods.Count(m => m.Type == type && m.Method == name && m.Return == result && m.Static == (name == "GetComponentIndex") &&
        string.Join(",", m.Args) == parameters) == 1, $"nav call {type}.{name}({parameters}) -> {result}");
Check(bases.TryGetValue("Il2CppQuantum.Frame", out var frameBase) && frameBase == "Il2CppQuantum.Core.FrameBase", "Frame derives from FrameBase (non-generic Has/GetPointer)");
Check(NecromancyPolicy.InHoldRange(59, 7.9, 60, 8) && !NecromancyPolicy.InHoldRange(61, 0, 60, 8) && !NecromancyPolicy.InHoldRange(10, -8.1, 60, 8) &&
      NecromancyPolicy.InHoldRange(55, 30, 60, 0), "hold range: horizontal <= teleport distance and |dy| <= max dy (0 = no limit)");
// T-010 summon swap: field types the plugin writes, and the setting/limit rules.
const string Raise = "Il2CppQuantum.RaiseMinionActionData";
Check(methods.Any(m => m.Type == Raise && m.Method == "get_MinionNpcData" && m.Return == "Il2CppQuantum.AssetRefNpcData") &&
      methods.Any(m => m.Type == Raise && m.Method == "set_MinionNpcData" && m.Args.SequenceEqual(new[] { "Il2CppQuantum.AssetRefNpcData" })),
      "RaiseMinionActionData.MinionNpcData AssetRefNpcData get/set");
Check(methods.Any(m => m.Type == Raise && m.Method == "get_MaxActiveMinions" && m.Return == "System.Int32") &&
      methods.Any(m => m.Type == Raise && m.Method == "set_MaxActiveMinions" && m.Args.SequenceEqual(new[] { "System.Int32" })),
      "RaiseMinionActionData.MaxActiveMinions Int32 get/set");
Check(methods.Any(m => m.Type == Raise && m.Method == "get_UseCorpseNpcType" && (m.Return == "System.Boolean" || m.Return == "Il2CppQuantum.QBoolean")),
      "RaiseMinionActionData.UseCorpseNpcType readable (separates raiseMinion from revive)");
Check(methods.Any(m => m.Type == "Il2CppQuantum.NpcData" && m.Method == "get_EnemyType"), "NpcData.EnemyType readable");
// T-012 0.15.14: the roar move goes on ActionComponent.ActionLayer (ActionLayer.Action = 1), not layer 0 (Base = locomotion).
Check(methods.Count(m => m.Type == "Il2CppQuantum.ActionComponent" && m.Method == "get_ActionLayer" && m.Return == "System.Int32" && m.Args.Length == 0) == 1 &&
      methods.Count(m => m.Type == "Il2CppQuantum.ActionComponent" && m.Method == "get_BaseLayer" && m.Return == "System.Int32" && m.Args.Length == 0) == 1,
      "ActionComponent.ActionLayer / BaseLayer Int32 getters");
Check(methods.Any(m => m.Type == "Il2CppQuantum.ActionComponent" && m.Method == "IsExecutingAction" && m.Return == "System.Boolean" &&
          m.Args.SequenceEqual(new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.ActionData" })) &&
      methods.Any(m => m.Type == "Il2CppQuantum.ActionComponent" && m.Method == "IsExecuting" && m.Return == "System.Boolean" &&
          m.Args.SequenceEqual(new[] { "Il2CppQuantum.Frame", "System.Int32" })),
      "ActionComponent.IsExecutingAction(Frame, ActionData) / IsExecuting(Frame, Int32) for the roar trace");
Check(methods.Count(m => m.Type == "Il2CppQuantum.NpcDataRef" && m.Method == "Get" && m.Return == "Il2CppQuantum.NpcData" && m.Args.SequenceEqual(new[] { "Il2CppQuantum.Frame" })) == 1 &&
      methods.Any(m => m.Type == "Il2CppQuantum.NpcDataRef" && m.Method == "get_IsDefault") && types.Contains("Il2CppQuantum.NpcAttachmentComponent"),
      "minion NPC identity: NpcAttachmentComponent + NpcDataRef.Get(Frame) -> NpcData");
Check(NecromancyPolicy.MinionNpcGuid("warrick_boss") == 2680182020363611965 && NecromancyPolicy.MinionNpcGuid(" Warrick_Boss ") == NecromancyPolicy.WarrickBossNpc &&
      NecromancyPolicy.MinionNpcGuid("warrick_enemy") == 670058593051072033 && NecromancyPolicy.MinionNpcGuid("wildling") == NecromancyPolicy.WildlingSwordNpc &&
      NecromancyPolicy.MinionNpcGuid("native") == 0 && NecromancyPolicy.MinionNpcGuid("") == 0 && NecromancyPolicy.MinionNpcGuid(null) == 0 &&
      NecromancyPolicy.MinionNpcGuid("123") == 123 && NecromancyPolicy.MinionNpcGuid("-5") == 0 && NecromancyPolicy.MinionNpcGuid("warrick") == 0 &&
      NecromancyPolicy.DefaultMinionNpc == "native" && NecromancyPolicy.MinionNpcGuid(NecromancyPolicy.DefaultMinionNpc) == 0,
      "minion_npc: presets, GUID, native/empty/unknown/negative keep shipped data; default is native");
Check(NecromancyPolicy.MinionMaxActive(NecromancyPolicy.WarrickBossNpc, 1, 5) == 1 && NecromancyPolicy.MinionMaxActive(NecromancyPolicy.WarrickBossNpc, 0, 5) == 5 &&
      NecromancyPolicy.MinionMaxActive(0, 1, 5) == 5 && NecromancyPolicy.MinionMaxActive(NecromancyPolicy.WarrickBossNpc, 9, 5) == 5 &&
      NecromancyPolicy.MinionMaxActive(NecromancyPolicy.WarrickBossNpc, -2, 5) == 5 && NecromancyPolicy.MinionMaxActive(NecromancyPolicy.WarrickBossNpc, 3, 5) == 3,
      "minion_max_active: applies only while swapped, 0/negative keep shipped, never above shipped");
Check(Math.Abs(NecromancyPolicy.DistanceRaw(0, 0, 0, 3L << 16, 0, 4L << 16) - 5) < 1e-9, "teleport log distance: 16.16 vector length");
Check(NecromancyPolicy.ProxyLayoutOk(16, 184, 80, 264, 16, 4, 8) && !NecromancyPolicy.ProxyLayoutOk(16, 184, 80, 200, 16, 4, 8) &&
      !NecromancyPolicy.ProxyLayoutOk(16, 184, 80, 264, 16, 14, 8) && !NecromancyPolicy.ProxyLayoutOk(16, 184, 80, 264, 12, 4, 8) &&
      !NecromancyPolicy.ProxyLayoutOk(16, 184, 80, 264, 16, 4, 9) && !NecromancyPolicy.ProxyLayoutOk(0, 184, 80, 264, 16, 4, 8),
      "proxy layout guard: buffer inside group, group inside Damage, counter before buffer");
Check(methods.Any(m => m.Type == "Il2CppQuantum.PayloadProxyGroup" && m.Method == "get_Main") &&
      types.Contains("Il2CppQuantum.EventDamageResolved") && types.Contains("Il2CppQuantum.Damage"), "PayloadProxyGroup / Damage / EventDamageResolved exist");
Check(methods.Count(m => m.Type == "Il2CppQuantum.DamageAPI" && m.Method == "Kill" && m.Args.Length == 8) == 1, "DamageAPI.Kill(8 args) exists for lifetime expiry");
Check(NecromancyPolicy.ShouldRetryTint(0, 0) && NecromancyPolicy.ShouldRetryTint(2, 0) && !NecromancyPolicy.ShouldRetryTint(3, 0) &&
      !NecromancyPolicy.ShouldRetryTint(500, 0) && NecromancyPolicy.ShouldRetryTint(500, 1), "tint guard: give up after 3 attempts with no success, never once it has succeeded");
Check(NecromancyPolicy.MinionTeamTarget("players_noff", NecromancyPolicy.TeamPlayers) == NecromancyPolicy.TeamPlayersNoFF &&
      NecromancyPolicy.MinionTeamTarget("players_noff", NecromancyPolicy.TeamEnemies) == 0 && NecromancyPolicy.MinionTeamTarget("players_noff", 0) == 0 &&
      NecromancyPolicy.MinionTeamTarget("native", NecromancyPolicy.TeamPlayers) == 0 && NecromancyPolicy.MinionTeamTarget("players_noff", NecromancyPolicy.TeamPlayersNoFF) == 0,
      "minion team: only a Players-team minion moves to PlayersNoFF, and only in players_noff mode");
Check(methods.Count(m => m.Type == "Il2CppQuantum.TeamComponent") > 0 && methods.Any(m => m.Type == "Il2CppQuantum.EventDamageResolved" && m.Method == "get_Response"),
      "TeamComponent and EventDamageResolved.Response exist");
Check(NecromancyPolicy.MinionPairIsAllied(true, false, false, true) && NecromancyPolicy.MinionPairIsAllied(false, true, true, false) &&
      NecromancyPolicy.MinionPairIsAllied(true, false, true, false) && !NecromancyPolicy.MinionPairIsAllied(false, true, false, true) &&
      !NecromancyPolicy.MinionPairIsAllied(true, false, false, false) && !NecromancyPolicy.MinionPairIsAllied(false, false, true, false),
      "friendly-fire rule: minion<->hero and minion<->minion allied; hero<->hero and minion<->enemy unchanged");
foreach (var (name, sourcePlayer, sourceRaisedByPlayer, hitPlayer, hitRaisedByPlayer, expected) in new[]
{
    ("raised minion -> player", false, true, true, false, true),
    ("player -> raised minion", true, false, false, true, true),
    ("raised minion -> raised minion", false, true, false, true, true),
    ("player -> player", true, false, true, false, false),
    ("raised minion -> enemy", false, true, false, false, false),
    ("enemy -> raised minion", false, false, false, true, false),
    ("player -> enemy", true, false, false, false, false),
    ("unknown -> player", false, false, true, false, false)
})
{
    bool actual = NecromancyPolicy.BlocksRaisedMinionDamage(sourcePlayer, sourceRaisedByPlayer, hitPlayer, hitRaisedByPlayer);
    Check(actual == expected, "raised-minion damage: " + name);
}
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
    // T-015 0.18.2: CrucibleUnlock JIT-compiles its view helpers before its own EntityView.GetActionView /
    // InitializeResourceManager detours; patching those first from this plugin crashed the game at launch.
    foreach (string forbidden in new[] { "GetActionView", "InitializeResourceManager" })
        Check(!userStrings.Contains(forbidden), "plugin does not patch '" + forbidden + "' (CrucibleUnlock startup JIT order)");
    foreach (var entry in nativeEntrypoints)
        Check(userStrings.Contains(entry.Name), "compiled plugin resolves native entry " + entry.Name);

    foreach (var (name, result, parameters) in new[]
    {
        ("ResolveDamageNative", "Void", new[] { "System.IntPtr", "Il2CppQuantum.Damage*", "Il2CppQuantum.DamageResponse*", "System.IntPtr" }),
        ("ProjectileImpactNative", "Void", new[] { "System.IntPtr", "System.IntPtr", "System.UInt64", "System.UInt64",
            "System.IntPtr", "System.IntPtr", "System.IntPtr" }),
        ("ProxyLookupNative", "System.Boolean", new[] { "System.IntPtr", "Il2CppQuantum.ProxyTypeFlags",
            "Il2CppQuantum.EntityRef&", "System.IntPtr" })
    })
    {
        int matches = 0;
        foreach (var handle in r.TypeDefinitions)
        {
            var type = r.GetTypeDefinition(handle);
            if (r.GetString(type.Name) != name) continue;
            foreach (var method in type.GetMethods())
            {
                var definition = r.GetMethodDefinition(method);
                if (r.GetString(definition.Name) != "Invoke") continue;
                var signature = definition.DecodeSignature(p, (object)null);
                if (signature.ReturnType == result && signature.ParameterTypes.SequenceEqual(parameters)) matches++;
            }
        }
        Check(matches == 1, "compiled Windows x64 damage delegate " + name);
    }

    var pluginType = r.TypeDefinitions.Single(h => p.GetTypeFromDefinition(r, h, 0) == "Necromancy.NecromancyMod");
    foreach (var (name, result, parameters) in new[]
    {
        ("AfterBossContentInit", "Void", new[] { Frame }),
        ("BeforeBossCount", "System.Boolean", new[] { Frame, Entity, Q + "RaiseMinionActionData", "System.Int32&" }),
        ("BeforeBossHeroLoad", "Void", new[] { Frame }),
        ("AfterBossName", "Void", new[] { "Il2Cpp.HeroItemDataAsset", "System.String&" }),
        ("AfterBossDescription", "Void", new[] { "Il2Cpp.HeroItemDataAsset", "System.String&" }),
        ("AfterRaiseExecute", "Void", new[] { Frame, Entity, Q + "RaiseMinionActionData", "System.Boolean" }),
        ("AfterMinionSpawned", "Void", new[] { Entity, Guid }),
    })
    {
        int matches = r.GetTypeDefinition(pluginType).GetMethods().Count(h =>
        {
            var m = r.GetMethodDefinition(h);
            if (r.GetString(m.Name) != name || !m.Attributes.HasFlag(System.Reflection.MethodAttributes.Static)) return false;
            var sg = m.DecodeSignature(p, (object)null);
            return sg.ReturnType == result && sg.ParameterTypes.SequenceEqual(parameters);
        });
        Check(matches == 1 && userStrings.Contains(name), "compiled boss-rune callback " + name);
    }
    Check(strings.Contains("LoadImage") == bossPng, "plugin PNG support matches the ImageConversion reference");
    Check(strings.Contains("PreviewAwardItemImmediate"), "plugin calls PreviewAwardItemImmediate");
    // T-014: the built DLL carries the table-driven reaction path (a DLL built before T-014 fails these).
    Check(userStrings.Contains(GestureReactions.FileName) && userStrings.Any(u => u.StartsWith("reaction minion entity=")),
        "plugin loads " + GestureReactions.FileName + " and logs reaction commands");
    Check(!userStrings.Any(u => u.StartsWith("roar minion entity=")), "plugin no longer runs the hard-coded Warrick roar path");
    // T-015 0.18.3: the Boss rune count must use native AiUtils.ExistsAlive; a managed Has<PendingDestructionComponent> /
    // Get<HealthComponent> threw NullReferenceException in play and blocked re-summoning after a Boss died.
    Check(!r.TypeReferences.Any(h => r.GetString(r.GetTypeReference(h).Name) == "PendingDestructionComponent"),
        "plugin does not touch PendingDestructionComponent from managed code (use AiUtils.ExistsAlive)");
    // T-016 round 3: the tracker is reached only through the non-generic index path; no managed generic over
    // NavMeshTrackerComponent (unproven in play) may be compiled into the plugin.
    Check(strings.Contains("GetComponentIndex") && strings.Contains("GetPointer") && userStrings.Any(u => u.StartsWith("boss-minion NAV ")),
        "plugin turns on Boss-minion nav dynamic search via ComponentTypeId index");
    Check(!Enumerable.Range(1, r.GetTableRowCount(TableIndex.MethodSpec)).Any(i => r.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(i)).DecodeSignature(p, (object)null).Any(a => a.Contains("NavMeshTrackerComponent"))),
        "plugin has no generic instantiation over NavMeshTrackerComponent");
    Check(!strings.Contains("TryGetPointer"), "plugin never calls Frame.TryGetPointer (its out T* wrapper needs the missing UIntPtr..ctor(IntPtr))");
    foreach (var (type, name, sig) in new[] {

        ("Il2CppQuantum.RunesAPI+Weapon", "ApplyRuneModification", "Il2CppQuantum.Frame,Il2CppQuantum.RuneModificationType,Il2CppQuantum.EntityRef,System.Int32,Il2CppQuantum.EntityRef,Il2CppQuantum.EntityRef"),

        ("Il2CppQuantum.InventoryAPI", "GetFirstItemOfType", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef,Il2CppQuantum.HeroItemData"),

        ("Il2CppQuantum.ItemStatsSystem", "GetOrInitializeAllRunes", "Il2CppQuantum.Frame"),
        ("Il2CppQuantum.RunesAPI+Weapon", "HasRune", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef,System.Int32"),
        ("Il2CppQuantum.RunesAPI", "GetRune", "Il2CppQuantum.Frame,Il2CppQuantum.AssetRefHeroItemData"),
        ("Il2CppQuantum.AiUtils", "ExistsAlive", "Il2CppQuantum.Frame,Il2CppQuantum.EntityRef"),
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
