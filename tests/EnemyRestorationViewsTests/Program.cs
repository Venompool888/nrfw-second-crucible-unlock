using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon;
using Il2CppMoon.Playmode;
using Il2CppMoon.Pooling;
using Il2CppQuantum;

// Compile the production orchestration against pure managed fakes. No game or
// Unity assembly is referenced, loaded or executed by this test executable.
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        ++checks;
    }
    private static void Throws(Action action, string name)
    {
        try { action(); } catch (InvalidOperationException) { ++checks; return; }
        catch (AggregateException) { ++checks; return; }
        throw new Exception("Expected failure: " + name);
    }
    private static EnemyRestorationCatalog Catalog() => new EnemyRestorationCatalog
    {
        Profiles = new[]
        {
            new EnemyRestorationProfile { Index = 0, Key = "a", PrefabName = "PrefabA", PrefabGuid = new EnemyRestorationPrefabGuid { Data1 = 10, Data2 = 11 } },
            new EnemyRestorationProfile { Index = 1, Key = "b", PrefabName = "PrefabB", PrefabGuid = new EnemyRestorationPrefabGuid { Data1 = 20, Data2 = 21 } }
        }
    };
    private static RuntimeAssetResolutionContext Context()
    {
        var ctx = new RuntimeAssetResolutionContext();
        AssetBase.Resolve = guid => ctx.m_assetByGuid.TryGetValue(guid, out var found) ? found : null;
        InitializeQuantumAssetDatabase.GeneratedLut = new WorldLUTAsset
        {
            PooledPrefabs = new[]
            {
                new RuntimePooledPrefab { PrefabGuid = new SerializableGuid { m_data1 = 10, m_data2 = 11 } },
                new RuntimePooledPrefab { PrefabGuid = new SerializableGuid { m_data1 = 20, m_data2 = 21 } }
            },
            DebugNames = new[] { "PrefabA", "PrefabB" }
        };
        ctx.CanonicalViews = new EntityViewData[Catalog().Profiles.Length];
        for (int i = 0; i < ctx.CanonicalViews.Length; ++i)
        {
            var p = Catalog().Profiles[i];
            var data = new EntityViewData { Guid = new AssetGuid { Value = p.ViewGuid }, Path = p.Path("view") };
            ctx.CanonicalViews[i] = data;
            ctx.SimulationAssets.Add(p.ViewGuid, data);
            ctx.SimulationStorage.Add(p.ViewGuid, data);
        }
        return ctx;
    }
    private static void Empty(RuntimeAssetResolutionContext ctx, string label)
    {
        Check(ctx.m_assetByGuid.Count == 0 && ctx.m_loadedWrappers.Count == 0 && ctx.m_allAssets.Count == 0, label);
    }
    // Adapter inspects this own managed test assembly only. It lets the same
    // test expose the frozen R4 implementation's duplicate view identity.
    private static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog) =>
        Ensure(ctx, catalog, ctx.CanonicalViews);
    private static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog, EntityViewData[] canonical)
    {
        var types = new[] { typeof(RuntimeAssetResolutionContext), typeof(EnemyRestorationCatalog), typeof(EntityViewData[]) };
        var method = typeof(EnemyRestorationViews).GetMethod("Ensure", types);
        var args = new object[] { ctx, catalog, canonical };
        if (method == null)
        {
            method = typeof(EnemyRestorationViews).GetMethod("Ensure", new[] { types[0], types[1] });
            args = new object[] { ctx, catalog };
        }
        try { return (bool)method.Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void CanonicalChecks()
    {
        var catalog = Catalog(); var ctx = Context();
        int created = EntityViewData.Created;
        var source = ctx.CanonicalViews;
        var writes = new[] { source[0].Writes, source[1].Writes };
        Check(Ensure(ctx, catalog), "canonical graph registration ready");
        foreach (var p in catalog.Profiles)
        {
            var guid = new AssetGuid { Value = p.ViewGuid };
            var data = source[p.Index];
            Check(ctx.m_assetByGuid[guid].Pointer == data.Pointer, "Unity resolver must use canonical simulation view identity");
            var wrapper = (EntityViewDataAsset)ctx.m_loadedWrappers[guid];
            Check(wrapper.Get(ctx).Pointer == data.Pointer && wrapper.AssetObject.Pointer == data.Pointer,
                "both wrapper lookups return canonical view");
            Check(ctx.SimulationAssets[p.ViewGuid].Pointer == data.Pointer && ctx.SimulationStorage[p.ViewGuid].Pointer == data.Pointer,
                "Unity and simulation dynamic asset/storage fixture share exact pointer");
            Check(data.Writes == writes[p.Index], "canonical GUID/path never assigned by Unity registration");
        }
        Check(EntityViewData.Created == created, "Unity view registration creates no EntityViewData");
        Check(Ensure(ctx, catalog, (EntityViewData[])source.Clone()), "repeat accepts new snapshot array with same canonical pointers");
        var foreign = new[] { new EntityViewData { Guid = source[0].Guid, Path = source[0].Path }, source[1] };
        Throws(() => Ensure(ctx, catalog, foreign), "repeat refuses same GUID/path replacement canonical pointer");
        Check(ctx.m_assetByGuid[source[0].Guid].Pointer == source[0].Pointer, "rejected replacement preserves owned canonical pointer");

        foreach (string invalid in new[] { "null", "length", "element", "guid", "path", "duplicate", "index" })
        {
            ctx = Context(); catalog = Catalog();
            var input = (EntityViewData[])ctx.CanonicalViews.Clone();
            switch (invalid)
            {
                case "null": input = null; break;
                case "length": input = new[] { input[0] }; break;
                case "element": input[1] = null; break;
                case "guid": input[1] = new EntityViewData { Guid = new AssetGuid { Value = 7 }, Path = input[1].Path }; break;
                case "path": input[1] = new EntityViewData { Guid = input[1].Guid, Path = "foreign/path" }; break;
                case "duplicate": input[1] = input[0]; break;
                case "index": catalog.Profiles[1].Index = 0; break;
            }
            int wrapperCount = UnityEngine.ScriptableObject.Created;
            var capturedContext = ctx; var capturedCatalog = catalog;
            Throws(() => Ensure(capturedContext, capturedCatalog, input), "canonical preflight rejects " + invalid);
            Empty(ctx, "canonical invalid input leaves all Unity caches empty: " + invalid);
            Check(UnityEngine.ScriptableObject.Created == wrapperCount, "canonical invalid input creates no wrapper: " + invalid);
        }
        catalog = Catalog(); ctx = Context();
        var existing = new EntityViewData { Guid = ctx.CanonicalViews[0].Guid, Path = ctx.CanonicalViews[0].Path };
        ctx.m_assetByGuid.Add(existing.Guid, existing);
        Throws(() => Ensure(ctx, catalog), "foreign cached same GUID/path cannot be adopted as canonical");
        Check(ctx.m_assetByGuid[existing.Guid].Pointer == existing.Pointer && ctx.m_loadedWrappers.Count == 0,
            "foreign matching shape retained without owned wrapper");

        // The release bootstrap has ten profiles. Model all ten dynamic/frame
        // storage identities, rather than inferring full closure from two rows.
        ctx = Context(); ctx.SimulationAssets.Clear(); ctx.SimulationStorage.Clear();
        catalog = new EnemyRestorationCatalog { Profiles = new EnemyRestorationProfile[10] };
        ctx.CanonicalViews = new EntityViewData[10];
        var prefabs = new RuntimePooledPrefab[10]; var names = new string[10];
        for (int i = 0; i < 10; ++i)
        {
            var p = new EnemyRestorationProfile { Index = i, Key = "profile" + i, PrefabName = "Prefab" + i,
                PrefabGuid = new EnemyRestorationPrefabGuid { Data1 = (ulong)(100 + i), Data2 = (ulong)(200 + i) } };
            catalog.Profiles[i] = p;
            var data = new EntityViewData { Guid = new AssetGuid { Value = p.ViewGuid }, Path = p.Path("view") };
            ctx.CanonicalViews[i] = data;
            ctx.SimulationAssets.Add(p.ViewGuid, data); ctx.SimulationStorage.Add(p.ViewGuid, data);
            prefabs[i] = new RuntimePooledPrefab { PrefabGuid = new SerializableGuid { m_data1 = p.PrefabGuid.Data1, m_data2 = p.PrefabGuid.Data2 } };
            names[i] = p.PrefabName;
        }
        InitializeQuantumAssetDatabase.GeneratedLut = new WorldLUTAsset { PooledPrefabs = prefabs, DebugNames = names };
        created = EntityViewData.Created;
        Check(Ensure(ctx, catalog), "complete ten profile canonical graph ready");
        Check(ctx.m_assetByGuid.Count == 10 && ctx.m_loadedWrappers.Count == 10 && ctx.m_allAssets.Count == 10,
            "ten canonical views registered in all Unity stores");
        foreach (var p in catalog.Profiles)
        {
            var guid = new AssetGuid { Value = p.ViewGuid };
            var canonical = ctx.CanonicalViews[p.Index];
            var wrapper = (EntityViewDataAsset)ctx.m_loadedWrappers[guid];
            Check(ctx.m_assetByGuid[guid].Pointer == canonical.Pointer && wrapper.Get(ctx).Pointer == canonical.Pointer &&
                wrapper.AssetObject.Pointer == canonical.Pointer && ctx.SimulationAssets[p.ViewGuid].Pointer == canonical.Pointer &&
                ctx.SimulationStorage[p.ViewGuid].Pointer == canonical.Pointer && canonical.Writes == 2,
                "ten profile Unity/global/dynamic/frame storage identity and immutability: " + p.Index);
        }
        Check(EntityViewData.Created == created, "complete ten profile registration creates no data");
    }
    private const long OfficialLutGuid = 8265928748392077239L;
    private static WorldLUTAsset CacheOfficialLut(RuntimeAssetResolutionContext ctx)
    {
        var lut = InitializeQuantumAssetDatabase.GeneratedLut;
        lut.AssetGuid = new AssetGuid { Value = OfficialLutGuid };
        ctx.m_loadedWrappers.Add(lut.AssetGuid, lut);
        InitializeQuantumAssetDatabase.GeneratedLut = null;
        return lut;
    }
    private static void FallbackChecks()
    {
        var catalog = Catalog();
        var ctx = Context();
        var lut = CacheOfficialLut(ctx);
        var created = UnityEngine.ScriptableObject.Created;
        var destroyed = UnityEngine.Object.Destroyed;
        Check(Ensure(ctx, catalog), "cached official LUT restores readiness when GeneratedLut is null");
        Check(InitializeQuantumAssetDatabase.GeneratedLut == null, "fallback never writes GeneratedLut");
        Check(ctx.m_loadedWrappers[new AssetGuid { Value = OfficialLutGuid }].Pointer == lut.Pointer,
            "fallback preserves official LUT wrapper identity");
        Check(ctx.m_assetByGuid.Count == 2 && ctx.m_loadedWrappers.Count == 3 && ctx.m_allAssets.Count == 2,
            "fallback appends only enemy stores alongside original LUT");
        Check(UnityEngine.ScriptableObject.Created - created == 2 && UnityEngine.Object.Destroyed == destroyed,
            "fallback creates only two enemy wrappers and destroys no source");
        created = UnityEngine.ScriptableObject.Created;
        Check(Ensure(ctx, catalog) && UnityEngine.ScriptableObject.Created == created &&
            ctx.m_loadedWrappers.Count == 3 && ctx.m_allAssets.Count == 2, "fallback repeat has no additions");

        ctx = Context(); lut = CacheOfficialLut(ctx);
        ctx.m_loadedWrappers.Remove(new AssetGuid { Value = OfficialLutGuid });
        created = UnityEngine.ScriptableObject.Created;
        Check(!Ensure(ctx, catalog), "uncached official LUT remains waiting");
        Empty(ctx, "uncached official LUT creates no stores");
        Check(UnityEngine.ScriptableObject.Created == created, "uncached official LUT creates no wrappers");

        ctx = Context(); lut = CacheOfficialLut(ctx);
        var key = new AssetGuid { Value = OfficialLutGuid };
        var wrong = new EntityViewDataAsset { AssetGuid = key };
        ctx.m_loadedWrappers[key] = wrong;
        created = UnityEngine.ScriptableObject.Created;
        Throws(() => Ensure(ctx, catalog), "cached official LUT wrong CLR type rejected");
        Check(ctx.m_loadedWrappers.Count == 1 && ctx.m_loadedWrappers[key].Pointer == wrong.Pointer &&
            ctx.m_assetByGuid.Count == 0 && ctx.m_allAssets.Count == 0 && UnityEngine.ScriptableObject.Created == created,
            "wrong cached type causes zero writes");

        ctx = Context(); lut = CacheOfficialLut(ctx);
        lut.AssetGuid = new AssetGuid { Value = OfficialLutGuid + 1 };
        created = UnityEngine.ScriptableObject.Created;
        Throws(() => Ensure(ctx, catalog), "cached LUT mismatched own GUID rejected");
        Check(ctx.m_loadedWrappers.Count == 1 && ctx.m_loadedWrappers[key].Pointer == lut.Pointer &&
            ctx.m_assetByGuid.Count == 0 && ctx.m_allAssets.Count == 0 && UnityEngine.ScriptableObject.Created == created,
            "wrong cached GUID causes zero writes");

        ctx = Context(); lut = CacheOfficialLut(ctx);
        lut.DebugNames[0] = "ForeignPrefab";
        created = UnityEngine.ScriptableObject.Created;
        Throws(() => Ensure(ctx, catalog), "cached LUT still enforces exact GUID/name");
        Check(ctx.m_loadedWrappers.Count == 1 && ctx.m_loadedWrappers[key].Pointer == lut.Pointer &&
            ctx.m_assetByGuid.Count == 0 && ctx.m_allAssets.Count == 0 && UnityEngine.ScriptableObject.Created == created,
            "cached prefab mismatch preflight leaves source untouched");

        ctx = Context(); lut = CacheOfficialLut(ctx);
        ctx.m_loadedWrappers.ThrowAfterAdd = 1;
        destroyed = UnityEngine.Object.Destroyed;
        Throws(() => Ensure(ctx, catalog), "fallback post-insert failure rolls back enemy registration");
        Check(ctx.m_loadedWrappers.Count == 1 && ctx.m_loadedWrappers[key].Pointer == lut.Pointer &&
            ctx.m_assetByGuid.Count == 0 && ctx.m_allAssets.Count == 0 && UnityEngine.Object.Destroyed - destroyed == 2,
            "fallback rollback preserves LUT and destroys only own enemy wrappers");
        Check(Ensure(ctx, catalog), "fallback retry succeeds after clean rollback");

        ctx = Context();
        ctx.m_loadedWrappers.Add(key, new EntityViewDataAsset { AssetGuid = key });
        Check(Ensure(ctx, catalog), "ready GeneratedLut takes precedence over unrelated cached wrong type");

        ctx = Context();
        var goodCached = new WorldLUTAsset { AssetGuid = key,
            PooledPrefabs = InitializeQuantumAssetDatabase.GeneratedLut.PooledPrefabs,
            DebugNames = new[] { "PrefabA", "PrefabB" } };
        ctx.m_loadedWrappers.Add(key, goodCached);
        InitializeQuantumAssetDatabase.GeneratedLut.DebugNames[0] = "WrongPrimary";
        Throws(() => Ensure(ctx, catalog), "invalid present GeneratedLut is not masked by valid fallback");
        Check(ctx.m_loadedWrappers.Count == 1 && ctx.m_assetByGuid.Count == 0 && ctx.m_allAssets.Count == 0,
            "invalid primary LUT retains source caches");
    }
    private static void Main()
    {
        var catalog = Catalog();
        var ctx = Context();
        InitializeQuantumAssetDatabase.GeneratedLut = null;
        var created = UnityEngine.ScriptableObject.Created;
        Check(!Ensure(ctx, catalog), "LUT wait returns false");
        Empty(ctx, "LUT wait leaves caches empty");
        Check(UnityEngine.ScriptableObject.Created == created, "LUT wait creates no wrapper");
        CanonicalChecks();
        FallbackChecks();

        ctx = Context();
        Check(Ensure(ctx, catalog), "register ready");
        Check(ctx.m_assetByGuid.Count == 2 && ctx.m_loadedWrappers.Count == 2 && ctx.m_allAssets.Count == 2, "register all three stores");
        created = UnityEngine.ScriptableObject.Created;
        Check(Ensure(ctx, catalog), "repeat is ready");
        Check(UnityEngine.ScriptableObject.Created == created && ctx.m_allAssets.Count == 2, "repeat creates/adds nothing");
        var first = ctx.m_loadedWrappers[new AssetGuid { Value = catalog.Profiles[0].ViewGuid }];
        var view = (EntityViewDataAsset)first;
        Check(view.ViewPrefabRef.Guid.m_data1 == 10 && view.ViewConfigs.Length == 0 && view.ViewConfigsRef.Id == 0, "exact prefab/no donor settings");
        catalog.Profiles[0].PrefabName = "Changed";
        Throws(() => Ensure(ctx, catalog), "changed mutable catalog refused");
        Check(ctx.m_loadedWrappers[new AssetGuid { Value = Catalog().Profiles[0].ViewGuid }].Pointer == first.Pointer, "mutation failure preserves original registration");

        catalog = Catalog(); ctx = Context();
        var foreign = new EntityViewData { Guid = new AssetGuid { Value = catalog.Profiles[0].ViewGuid }, Path = "foreign" };
        ctx.m_assetByGuid.Add(foreign.Guid, foreign);
        created = UnityEngine.ScriptableObject.Created;
        Throws(() => Ensure(ctx, catalog), "foreign GUID refused");
        Check(ctx.m_assetByGuid[foreign.Guid].Pointer == foreign.Pointer && UnityEngine.ScriptableObject.Created == created, "foreign GUID retained/no wrapper created");

        ctx = Context();
        foreign = new EntityViewData { Guid = new AssetGuid { Value = 100 }, Path = catalog.Profiles[0].Path("view") };
        ctx.m_allAssets.Add(foreign);
        Throws(() => Ensure(ctx, catalog), "foreign list path refused");
        Check(ctx.m_allAssets.Count == 1 && ctx.m_allAssets[0].Pointer == foreign.Pointer, "foreign list retained");

        ctx = Context();
        ctx.m_wrappersBeingLoaded.Add(new AssetGuid { Value = catalog.Profiles[0].ViewGuid }, new object());
        Throws(() => Ensure(ctx, catalog), "pending async key refused");
        Empty(ctx, "pending failure leaves owned caches empty");

        ctx = Context();
        InitializeQuantumAssetDatabase.GeneratedLut.DebugNames[0] = "WrongName";
        Throws(() => Ensure(ctx, catalog), "GUID/name mismatch refused");
        Empty(ctx, "GUID/name failure untouched");

        ctx = Context();
        var destroyed = UnityEngine.Object.Destroyed;
        ctx.m_loadedWrappers.ThrowAfterAdd = 2;
        Throws(() => Ensure(ctx, catalog), "exception after dictionary insertion");
        Empty(ctx, "dictionary failure rolls back every owned key");
        Check(UnityEngine.Object.Destroyed - destroyed == 2, "dictionary failure destroys own wrappers");
        Check(Ensure(ctx, catalog), "retry after clean rollback succeeds");

        ctx = Context();
        foreign = new EntityViewData { Guid = new AssetGuid { Value = 105 }, Path = "original/source" };
        var foreignWrapper = new EntityViewDataAsset { AssetGuid = foreign.Guid };
        ctx.m_assetByGuid.Add(foreign.Guid, foreign);
        ctx.m_loadedWrappers.Add(foreign.Guid, foreignWrapper);
        ctx.m_allAssets.Add(foreign);
        ctx.m_loadedWrappers.ThrowAfterAdd = 2;
        Throws(() => Ensure(ctx, catalog), "rollback with unrelated original assets");
        Check(ctx.m_assetByGuid.Count == 1 && ctx.m_assetByGuid[foreign.Guid].Pointer == foreign.Pointer &&
            ctx.m_loadedWrappers.Count == 1 && ctx.m_loadedWrappers[foreign.Guid].Pointer == foreignWrapper.Pointer &&
            ctx.m_allAssets.Count == 1 && ctx.m_allAssets[0].Pointer == foreign.Pointer,
            "rollback retains all original cache/list pointers");

        ctx = Context();
        ctx.m_allAssets.ThrowAfterAdd = 2;
        Throws(() => Ensure(ctx, catalog), "exception after list insertion");
        Empty(ctx, "list failure rolls back appended data");

        ctx = Context();
        AssetBase.Resolve = _ => null;
        Throws(() => Ensure(ctx, catalog), "global resolve mismatch fails closed");
        Empty(ctx, "lookup mismatch rollback");

        ctx = Context();
        Check(Ensure(ctx, catalog), "new context registers independently");
        ctx.m_loadedWrappers.Remove(new AssetGuid { Value = catalog.Profiles[0].ViewGuid });
        Throws(() => Ensure(ctx, catalog), "ownership lost refuses re-add");
        Check(ctx.m_loadedWrappers.Count == 1 && ctx.m_assetByGuid.Count == 2, "lost ownership failure does not mutate caches");
        Console.WriteLine("EnemyRestorationViews pure orchestration tests: " + checks + " passed.");
    }
}

public class FakeNative
{
    private static long next;
    public IntPtr Pointer { get; } = new IntPtr(++next);
    public T TryCast<T>() where T : class => this as T;
}
public sealed class FakeDictionary<TKey, TValue> : FakeNative
{
    private readonly Dictionary<TKey, TValue> items = new Dictionary<TKey, TValue>();
    public int ThrowAfterAdd;
    public int Count => items.Count;
    public TValue this[TKey key] { get => items[key]; set => items[key] = value; }
    public bool ContainsKey(TKey key) => items.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => items.TryGetValue(key, out value);
    public bool Remove(TKey key) => items.Remove(key);
    public void Add(TKey key, TValue value)
    {
        items.Add(key, value);
        if (ThrowAfterAdd > 0 && --ThrowAfterAdd == 0) throw new InvalidOperationException("Injected post-insert dictionary failure");
    }
}
public sealed class FakeList<T> : FakeNative
{
    private readonly List<T> items = new List<T>();
    public int ThrowAfterAdd;
    public int Count => items.Count;
    public T this[int index] { get => items[index]; set => items[index] = value; }
    public void RemoveAt(int index) => items.RemoveAt(index);
    public void Add(T value)
    {
        items.Add(value);
        if (ThrowAfterAdd > 0 && --ThrowAfterAdd == 0) throw new InvalidOperationException("Injected post-insert list failure");
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T>
    {
        public int Length;
        public Il2CppReferenceArray(int length) { Length = length; }
    }
}
namespace UnityEngine
{
    public enum HideFlags { DontSave }
    public class Object : FakeNative
    {
        public string name;
        public HideFlags hideFlags;
        public static int Destroyed;
        public static void Destroy(Object value) { ++Destroyed; }
    }
    public class ScriptableObject : Object
    {
        public static int Created;
        public static T CreateInstance<T>() where T : ScriptableObject, new() { ++Created; return new T(); }
    }
}
namespace Il2CppQuantum
{
    public struct AssetGuid { public long Value; }
    public class AssetObject : FakeNative
    {
        private AssetGuid guid; private string path; public int Writes;
        public AssetGuid Guid { get => guid; set { guid = value; ++Writes; } }
        public string Path { get => path; set { path = value; ++Writes; } }
    }
    public class EntityViewData : AssetObject { public static int Created; public EntityViewData() { ++Created; } }
    public interface IAssetResolutionContext { AssetObject FindAsset(AssetGuid guid); }
}
namespace Il2CppMoon { public struct SerializableGuid { public ulong m_data1, m_data2; } }
namespace Il2CppMoon.Pooling
{
    public struct PooledPrefabRef { public SerializableGuid Guid; }
    public struct RuntimePooledPrefab { public SerializableGuid PrefabGuid; }
}
namespace Il2CppMoon.Playmode { public static class InitializeQuantumAssetDatabase { public static WorldLUTAsset GeneratedLut; } }
namespace Il2Cpp
{
    public class AssetBase : UnityEngine.ScriptableObject
    {
        public AssetGuid AssetGuid;
        public static Func<AssetGuid, AssetObject> Resolve;
        public AssetObject AssetObject => Resolve(AssetGuid);
    }
    public interface IEntityViewSettings { }
    public class EntityViewSettingsViewData { }
    public struct GuidRef<T> { public long Id; public SerializableGuid Namespace; }
    public class EntityViewDataAsset : AssetBase
    {
        public PooledPrefabRef ViewPrefabRef;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<IEntityViewSettings> ViewConfigs;
        public GuidRef<EntityViewSettingsViewData> ViewConfigsRef;
        public EntityViewData Get(IAssetResolutionContext ctx) => (EntityViewData)ctx.FindAsset(AssetGuid);
    }
    public class WorldLUTAsset : AssetBase
    {
        public RuntimePooledPrefab[] PooledPrefabs;
        public string[] DebugNames;
    }
    public class RuntimeAssetResolutionContext : FakeNative, IAssetResolutionContext
    {
        public FakeDictionary<AssetGuid, AssetObject> m_assetByGuid = new FakeDictionary<AssetGuid, AssetObject>();
        public FakeDictionary<AssetGuid, AssetBase> m_loadedWrappers = new FakeDictionary<AssetGuid, AssetBase>();
        public FakeDictionary<AssetGuid, object> m_wrappersBeingLoaded = new FakeDictionary<AssetGuid, object>();
        public FakeList<AssetObject> m_allAssets = new FakeList<AssetObject>();
        public EntityViewData[] CanonicalViews;
        public Dictionary<long, EntityViewData> SimulationAssets = new();
        public Dictionary<long, EntityViewData> SimulationStorage = new();
        public T Cast<T>() => (T)(object)this;
        public AssetObject FindAsset(AssetGuid guid) => m_assetByGuid.TryGetValue(guid, out var asset) ? asset : null;
    }
}
