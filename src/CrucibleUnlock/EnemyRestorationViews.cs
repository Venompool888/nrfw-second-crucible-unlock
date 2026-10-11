using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Playmode;
using Il2CppMoon.Pooling;
using Il2CppQuantum;
using UnityEngine;
using EntityView = Il2CppMoon.Forsaken.EntityView;

namespace CrucibleUnlock
{
    // Called only by the loader's main-thread pump of the current Core.AssetCtx.
    // These wrappers refer to shipped pooled prefabs. Extra source asset load
    // references keep roots alive; no prefab, costume or NPC is cloned or modified.
    // Ownership is established by pointers,
    // never merely by matching a GUID/path supplied by another mod.
    internal static class EnemyRestorationViews
    {
        // PoolingSystem.LoadPoolingData uses this exact WorldLUTAsset GUID in
        // ViewFrame.LoadAssetWrapper<WorldLUTAsset>. The shipped QdbTask path
        // caches the wrapper instead of publishing the editor GeneratedLut.
        private const long OfficialWorldLutGuid = 8265928748392077239L;
        private sealed class Entry
        {
            public EnemyRestorationProfile Profile;
            public EntityViewData Data;
            public EntityViewDataAsset Wrapper;
            public EntityViewDataAsset Source;
            public GameObject Root;
            public int ListIndex;
        }

        private sealed class OwnedContext
        {
            // Retaining the context also prevents a recycled native pointer from
            // being mistaken for a previously registered context.
            public RuntimeAssetResolutionContext Context;
            public List<Entry> Entries;
            public IntPtr AssetsPointer, WrappersPointer, ListPointer;
            public IntPtr PoolPointer;
        }

        private sealed class RootLoad
        {
            public RuntimeAssetResolutionContext Context;
            public PoolingSystem Pool;
            public Func<GameObject> ReadRoot;
        }
        // Own only extra Addressables references, never pooled clones. Keeping the
        // read-only source root loaded allows repeated readiness verification.
        private static readonly Dictionary<string, RootLoad> RootLoads = new Dictionary<string, RootLoad>();

        private static readonly Dictionary<IntPtr, OwnedContext> Owned = new Dictionary<IntPtr, OwnedContext>();

        // False means the asynchronous world LUT is not ready yet; the caller may
        // retry on the next main-thread pump. Any collision or inconsistent state throws.
        public static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog, EntityViewData[] canonicalViews)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (catalog?.Profiles == null || catalog.Profiles.Length == 0)
                throw new ArgumentException("Enemy view catalog is empty.", nameof(catalog));
            // Simulation owns these assets and has finished its complete bootstrap
            // before publication. Snapshot only managed references: never construct,
            // reconfigure or load another EntityViewData in the Unity resolver.
            if (canonicalViews == null || canonicalViews.Length != catalog.Profiles.Length)
                throw new InvalidOperationException("Canonical enemy view snapshot is incomplete.");
            var views = (EntityViewData[])canonicalViews.Clone();
            var dataPointers = new HashSet<IntPtr>();
            for (int i = 0; i < views.Length; ++i)
            {
                var p = catalog.Profiles[i];
                var data = views[i];
                if (p == null || p.Index != i || data == null || data.Guid.Value != p.ViewGuid ||
                    data.Path != p.Path("view") || data.Pointer == IntPtr.Zero || !dataPointers.Add(data.Pointer))
                    throw new InvalidOperationException("Canonical enemy view identity/configuration mismatch at index " + i);
            }
            if (Owned.TryGetValue(ctx.Pointer, out var owned))
            {
                ValidateOwned(ctx, catalog, views, owned);
                return true;
            }

            var lut = InitializeQuantumAssetDatabase.GeneratedLut;
            RequireCaches(ctx);
            if (lut == null)
            {
                var lutGuid = new AssetGuid { Value = OfficialWorldLutGuid };
                if (!ctx.m_loadedWrappers.TryGetValue(lutGuid, out var wrapper) || wrapper == null)
                    return false; // Official asynchronous load has not completed.
                lut = wrapper.TryCast<WorldLUTAsset>();
                if (lut == null || lut.AssetGuid.Value != OfficialWorldLutGuid)
                    throw new InvalidOperationException("Official pooled prefab LUT wrapper type/GUID mismatch.");
            }
            var profiles = catalog.Profiles;
            var sources = new EntityViewDataAsset[profiles.Length];
            var roots = new GameObject[profiles.Length];
            var pool = PoolingSystem.Active;
            if (pool == null || pool.m_prefabs == null) return false;
            var guids = new HashSet<long>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in profiles)
            {
                if (p == null || p.PrefabGuid == null || string.IsNullOrWhiteSpace(p.Key) ||
                    string.IsNullOrWhiteSpace(p.PrefabName) || ((ulong)p.ViewGuid >> 62) != 3 ||
                    !guids.Add(p.ViewGuid) || !paths.Add(p.Path("view")))
                    throw new InvalidOperationException("Invalid or duplicated enemy view binding.");
                VerifyPrefab(lut, p);
                if (p.ViewTemplateGuid <= 0)
                    throw new InvalidOperationException("Enemy native view template is missing: " + p.Key);
                // Native non-generic load path; do not depend on an unverified
                // closed TryFindWrapper<EntityViewDataAsset> AOT MethodSpec.
                if (!ctx.TryFindWrapperImpl(new AssetGuid { Value = p.ViewTemplateGuid }, out var sourceBase, true))
                    return false;
                var source = sourceBase?.TryCast<EntityViewDataAsset>();
                if (source == null || source.AssetGuid.Value != p.ViewTemplateGuid || source.ViewConfigs == null)
                    throw new InvalidOperationException("Enemy native view wrapper mismatch: " + p.Key);
                var nativeGuid = source.ViewPrefabRef.Guid;
                if (nativeGuid.m_data1 != p.PrefabGuid.Data1 || nativeGuid.m_data2 != p.PrefabGuid.Data2)
                    throw new InvalidOperationException("Enemy native view root GUID mismatch: " + p.Key);
                if (!TryReadRoot(ctx, pool, source.ViewPrefabRef, out var root)) return false;
                VerifyRoot(root, p.Key);
                sources[p.Index] = source;
                roots[p.Index] = root;
                var guid = Guid(p);
                if (ctx.m_assetByGuid.ContainsKey(guid) || ctx.m_loadedWrappers.ContainsKey(guid) ||
                    (ctx.m_wrappersBeingLoaded != null && ctx.m_wrappersBeingLoaded.ContainsKey(guid)))
                    throw new InvalidOperationException("Enemy view GUID already belongs to another registration: " + p.Path("view"));
            }
            // Check the independent list too, including path collisions with a
            // different GUID. Do the complete preflight before creating objects.
            for (int i = 0; i < ctx.m_allAssets.Count; ++i)
            {
                var asset = ctx.m_allAssets[i];
                if (asset != null && (guids.Contains(asset.Guid.Value) || paths.Contains(asset.Path)))
                    throw new InvalidOperationException("Enemy view asset list collision: " + asset.Path);
            }

            var entries = new List<Entry>();
            try
            {
                for (int i = 0; i < profiles.Length; ++i)
                {
                    var p = profiles[i];
                    // Snapshot the binding so mutating the caller's catalog cannot
                    // silently redefine the ownership we have already established.
                    var snapshot = new EnemyRestorationProfile
                    {
                        Key = p.Key, Index = p.Index, PrefabName = p.PrefabName,
                        ViewTemplateGuid = p.ViewTemplateGuid,
                        PrefabGuid = new EnemyRestorationPrefabGuid { Data1 = p.PrefabGuid.Data1, Data2 = p.PrefabGuid.Data2 }
                    };
                    var entry = new Entry { Profile = snapshot, Data = views[i], Source = sources[i], Root = roots[i], ListIndex = -1 };
                    // Track the wrapper immediately so later configuration failure
                    // destroys only this attempt's ScriptableObject.
                    entries.Add(entry);
                    entry.Wrapper = ScriptableObject.CreateInstance<EntityViewDataAsset>();
                    if (entry.Wrapper == null) throw new InvalidOperationException("Cannot create enemy view wrapper.");
                    entry.Wrapper.name = p.Path("view");
                    entry.Wrapper.hideFlags = HideFlags.DontSave;
                    entry.Wrapper.AssetGuid = Guid(p);
                    // Official settings are read-only shared references. Preserve
                    // native costume and view configuration instead of dropping it.
                    entry.Wrapper.ViewPrefabRef = entry.Source.ViewPrefabRef;
                    entry.Wrapper.ViewConfigs = entry.Source.ViewConfigs;
                    entry.Wrapper.ViewConfigsRef = entry.Source.ViewConfigsRef;
                    ValidateEntry(entry);
                }

                foreach (var entry in entries)
                {
                    var guid = Guid(entry.Profile);
                    // Add, never indexer assignment: no source/foreign asset can
                    // be overwritten even if a cache changed after preflight.
                    ctx.m_assetByGuid.Add(guid, entry.Data);
                    ctx.m_loadedWrappers.Add(guid, entry.Wrapper);
                    entry.ListIndex = ctx.m_allAssets.Count;
                    ctx.m_allAssets.Add(entry.Data);
                }

                var context = new OwnedContext
                {
                    Context = ctx, Entries = entries,
                    AssetsPointer = ctx.m_assetByGuid.Pointer, WrappersPointer = ctx.m_loadedWrappers.Pointer,
                    ListPointer = ctx.m_allAssets.Pointer, PoolPointer = pool.Pointer
                };
                ValidateOwned(ctx, catalog, views, context);
                // Get(ctx) dispatches IAssetResolutionContext.FindAsset; AssetObject
                // uses AssetBase.Resolve. Verify both without replacing that global
                // delegate, which belongs to the game's active resolution context.
                var resolution = ctx.Cast<IAssetResolutionContext>();
                foreach (var entry in entries)
                {
                    var viaContext = entry.Wrapper.Get(resolution);
                    var viaGlobal = entry.Wrapper.AssetObject;
                    if (viaContext == null || viaContext.Pointer != entry.Data.Pointer ||
                        viaGlobal == null || viaGlobal.Pointer != entry.Data.Pointer)
                        throw new InvalidOperationException("Enemy view wrapper lookup failed: " + entry.Profile.Path("view"));
                }
                Owned.Add(ctx.Pointer, context);
                return true;
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                Rollback(ctx, entries, errors);
                if (errors.Count > 1) throw new AggregateException("Enemy view registration/rollback failed.", errors);
                throw;
            }
        }

        private static AssetGuid Guid(EnemyRestorationProfile p) => new AssetGuid { Value = p.ViewGuid };

        private static bool TryReadRoot(RuntimeAssetResolutionContext ctx, PoolingSystem pool, PooledPrefabRef prefab, out GameObject root)
        {
            root = null;
            if (!pool.TryResolvePrefab(prefab, out var index) || index < 0 || index >= pool.m_prefabs.Length)
                throw new InvalidOperationException("Native enemy view root is absent from current pool.");
            var native = pool.m_prefabs[index];
            root = native.RootInstance;
            if (root != null) VerifyRoot(root, "pool-index=" + index);
            // Retain an independent asset load even when the pool is already
            // warm: its RootInstance may be pruned during normal gameplay.
            if (string.IsNullOrWhiteSpace(native.LoadKey))
                throw new InvalidOperationException("Native enemy view root has no load key.");
            var key = ctx.Pointer.ToString() + ":" + pool.Pointer.ToString() + ":" + index;
            if (!RootLoads.TryGetValue(key, out var load))
            {
                var op = pool.LoadOpCreate(native.LoadKey);
                load = new RootLoad { Context = ctx, Pool = pool,
                    // LoadOpResult activates/reparents/clones source objects. Use
                    // only the asset-operation result, without that callback.
                    ReadRoot = () => {
                        if (!pool.LoadOpDone(op)) return null;
                        var result = op.Result;
                        if (result == null) throw new InvalidOperationException("Native enemy source root load failed: " + native.LoadKey);
                        return result;
                    } };
                RootLoads.Add(key, load);
            }
            if (load.Context.Pointer != ctx.Pointer || load.Pool.Pointer != pool.Pointer)
                throw new InvalidOperationException("Enemy root load ownership changed.");
            root = load.ReadRoot();
            return root != null;
        }

        private static void VerifyRoot(GameObject root, string key)
        {
            // The native manager uses the root component, not a descendant.
            // Non-generic Unity API avoids requiring an unshipped AOT method.
            var entity = root.GetComponent(Il2CppType.Of<EntityView>())?.TryCast<EntityView>();
            var humanoid = root.GetComponent(Il2CppType.Of<HumanoidEnemyView>())?.TryCast<HumanoidEnemyView>();
            if (entity == null || humanoid == null)
                throw new InvalidOperationException("Enemy pooled root lacks EntityView/HumanoidEnemyView: " + key + " root=" + root.name);
        }

        private static void RequireCaches(RuntimeAssetResolutionContext ctx)
        {
            if (ctx.m_assetByGuid == null || ctx.m_loadedWrappers == null || ctx.m_allAssets == null)
                throw new InvalidOperationException("Enemy view resolution caches are unavailable.");
        }

        private static void VerifyPrefab(WorldLUTAsset lut, EnemyRestorationProfile p)
        {
            var prefabs = lut.PooledPrefabs;
            var names = lut.DebugNames;
            if (prefabs == null || names == null || prefabs.Length != names.Length)
                throw new InvalidOperationException("Enemy pooled prefab LUT is incomplete.");
            int matches = 0;
            for (int i = 0; i < prefabs.Length; ++i)
            {
                var guid = prefabs[i].PrefabGuid;
                if (guid.m_data1 != p.PrefabGuid.Data1 || guid.m_data2 != p.PrefabGuid.Data2) continue;
                if (!string.Equals(names[i], p.PrefabName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Enemy pooled prefab GUID/name mismatch: " + p.Key);
                ++matches;
            }
            if (matches != 1) throw new InvalidOperationException("Enemy pooled prefab binding is missing/ambiguous: " + p.Key);
        }

        private static void ValidateEntry(Entry entry)
        {
            var p = entry.Profile;
            var w = entry.Wrapper;
            if (entry.Data == null || w == null)
                throw new InvalidOperationException("Enemy view object is unavailable: " + p.Path("view"));
            var prefab = w.ViewPrefabRef.Guid;
            var settings = w.ViewConfigsRef;
            var sourceSettings = entry.Source.ViewConfigsRef;
            VerifyRoot(entry.Root, p.Key);
            if (entry.Data.Guid.Value != p.ViewGuid ||
                entry.Data.Path != p.Path("view") || w.AssetGuid.Value != p.ViewGuid ||
                prefab.m_data1 != p.PrefabGuid.Data1 || prefab.m_data2 != p.PrefabGuid.Data2 ||
                entry.Source.AssetGuid.Value != p.ViewTemplateGuid ||
                entry.Source.ViewPrefabRef.Guid.m_data1 != prefab.m_data1 || entry.Source.ViewPrefabRef.Guid.m_data2 != prefab.m_data2 ||
                w.ViewConfigs == null || w.ViewConfigs.Pointer != entry.Source.ViewConfigs.Pointer || settings.Id != sourceSettings.Id ||
                settings.Namespace.m_data1 != sourceSettings.Namespace.m_data1 || settings.Namespace.m_data2 != sourceSettings.Namespace.m_data2)
                throw new InvalidOperationException("Enemy view configuration changed: " + p.Path("view"));
        }

        private static void ValidateOwned(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog, EntityViewData[] canonicalViews, OwnedContext owned)
        {
            RequireCaches(ctx);
            if (owned.Context.Pointer != ctx.Pointer || ctx.m_assetByGuid.Pointer != owned.AssetsPointer ||
                ctx.m_loadedWrappers.Pointer != owned.WrappersPointer || ctx.m_allAssets.Pointer != owned.ListPointer ||
                owned.Entries.Count != catalog.Profiles.Length)
                throw new InvalidOperationException("Enemy view context/catalog changed after registration.");
            if (PoolingSystem.Active == null || PoolingSystem.Active.Pointer != owned.PoolPointer)
                throw new InvalidOperationException("Enemy view current pool changed after registration.");
            for (int i = 0; i < owned.Entries.Count; ++i)
            {
                var entry = owned.Entries[i];
                var p = catalog.Profiles[i];
                if (p == null || p.PrefabGuid == null || p.ViewGuid != entry.Profile.ViewGuid || p.Key != entry.Profile.Key ||
                    p.PrefabName != entry.Profile.PrefabName || p.ViewTemplateGuid != entry.Profile.ViewTemplateGuid || canonicalViews[i].Pointer != entry.Data.Pointer ||
                    p.PrefabGuid.Data1 != entry.Profile.PrefabGuid.Data1 ||
                    p.PrefabGuid.Data2 != entry.Profile.PrefabGuid.Data2)
                    throw new InvalidOperationException("Enemy view catalog changed after registration.");
                ValidateEntry(entry);
                if (!TryReadRoot(ctx, PoolingSystem.Active, entry.Wrapper.ViewPrefabRef, out var loadedRoot) ||
                    loadedRoot.Pointer != entry.Root.Pointer)
                    throw new InvalidOperationException("Enemy source root ownership was lost: " + p.Key);
                var guid = Guid(entry.Profile);
                if (!ctx.m_assetByGuid.TryGetValue(guid, out var data) || data == null || data.Pointer != entry.Data.Pointer ||
                    !ctx.m_loadedWrappers.TryGetValue(guid, out var wrapper) || wrapper == null || wrapper.Pointer != entry.Wrapper.Pointer ||
                    entry.ListIndex < 0 || entry.ListIndex >= ctx.m_allAssets.Count ||
                    ctx.m_allAssets[entry.ListIndex]?.Pointer != entry.Data.Pointer)
                    throw new InvalidOperationException("Enemy view registration ownership was lost: " + entry.Profile.Path("view"));
            }
        }

        private static void Rollback(RuntimeAssetResolutionContext ctx, List<Entry> entries, List<Exception> errors)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                var entry = entries[i];
                var guid = Guid(entry.Profile);
                bool wrapperReleased = false;
                try
                {
                    if (!ctx.m_loadedWrappers.TryGetValue(guid, out var wrapper)) wrapperReleased = true;
                    else if (entry.Wrapper != null && wrapper != null && wrapper.Pointer == entry.Wrapper.Pointer)
                    {
                        if (!ctx.m_loadedWrappers.Remove(guid)) throw new InvalidOperationException("Cannot remove owned enemy wrapper.");
                        wrapperReleased = true;
                    }
                    else throw new InvalidOperationException("Foreign enemy wrapper retained during rollback.");
                }
                catch (Exception error) { errors.Add(error); }
                try
                {
                    if (ctx.m_assetByGuid.TryGetValue(guid, out var asset))
                    {
                        if (entry.Data == null || asset == null || asset.Pointer != entry.Data.Pointer)
                            throw new InvalidOperationException("Foreign enemy data retained during rollback.");
                        if (!ctx.m_assetByGuid.Remove(guid)) throw new InvalidOperationException("Cannot remove owned enemy view data.");
                    }
                    if (entry.Data != null)
                        for (int j = ctx.m_allAssets.Count - 1; j >= 0; --j)
                            if (ctx.m_allAssets[j]?.Pointer == entry.Data.Pointer) ctx.m_allAssets.RemoveAt(j);
                }
                catch (Exception error) { errors.Add(error); }
                // Never destroy a wrapper that is still cached by the game.
                if (wrapperReleased && entry.Wrapper != null)
                    try { UnityEngine.Object.Destroy(entry.Wrapper); } catch (Exception error) { errors.Add(error); }
            }
        }
    }
}
