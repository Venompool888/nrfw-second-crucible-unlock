using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppMoon.Pooling;
using Il2CppQuantum;
using UnityEngine;

namespace CrucibleUnlock
{
    // Main-thread bridge for canonical private simulation weapons. Source assets
    // and prefabs remain read-only; only our wrappers and resolver entries are new.
    internal static class EnemyRestorationWeaponViews
    {
        private sealed class Entry
        {
            public int Index;
            public string Key, Path;
            public long Guid, SourceGuid;
            public EnemyWeaponData Data;
            public EnemyWeaponDataAsset Source, Wrapper;
            public PooledPrefabRef Prefab;
            public GameObject Root;
            public int ListIndex = -1;
        }
        private sealed class Owner
        {
            public RuntimeAssetResolutionContext Context;
            public IntPtr Assets, Wrappers, List, Pool;
            public int ProfileCount;
            public List<Entry> Entries;
        }
        private sealed class Load
        {
            public RuntimeAssetResolutionContext Context;
            public PoolingSystem Pool;
            public string LoadKey;
            public Func<GameObject> Read;
        }
        private static readonly Dictionary<IntPtr, Owner> Owners = new Dictionary<IntPtr, Owner>();
        // Independent Addressables references survive pool pruning. At most three
        // distinct weapon roots per context/pool are retained until process exit.
        private static readonly Dictionary<string, Load> Loads = new Dictionary<string, Load>();

        public static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog, EnemyWeaponData[] canonicalWeapons)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (catalog?.Profiles == null || catalog.Profiles.Length == 0 || canonicalWeapons == null ||
                canonicalWeapons.Length != catalog.Profiles.Length)
                throw new InvalidOperationException("Canonical enemy weapon snapshot is incomplete.");
            RequireCaches(ctx);
            var weapons = (EnemyWeaponData[])canonicalWeapons.Clone();
            var pointers = new HashSet<IntPtr>();
            var guids = new HashSet<long>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var expected = new List<Entry>();
            for (int i = 0; i < weapons.Length; ++i)
            {
                var p = catalog.Profiles[i];
                if (p == null || p.Index != i || string.IsNullOrWhiteSpace(p.Key))
                    throw new InvalidOperationException("Enemy weapon profile identity mismatch at index " + i);
                if (p.BlockedActions == null || p.BlockedActions.Length == 0)
                {
                    if (weapons[i] != null) throw new InvalidOperationException("Unexpected private enemy weapon: " + p.Key);
                    continue;
                }
                var data = weapons[i];
                if (p.WeaponGuid <= 0 || data == null || data.Pointer == IntPtr.Zero || data.Guid.Value != p.PrivateWeaponGuid ||
                    data.Path != p.Path("weapon") || ((ulong)data.Guid.Value >> 62) != 3 || !pointers.Add(data.Pointer) ||
                    !guids.Add(data.Guid.Value) || !paths.Add(data.Path))
                    throw new InvalidOperationException("Canonical enemy weapon identity mismatch: " + p.Key);
                expected.Add(new Entry { Index = i, Key = p.Key, Path = p.Path("weapon"), Guid = p.PrivateWeaponGuid,
                    SourceGuid = p.WeaponGuid, Data = data });
            }
            if (Owners.TryGetValue(ctx.Pointer, out var owner))
            {
                ValidateOwned(ctx, catalog.Profiles.Length, expected, owner);
                return true;
            }
            var pool = PoolingSystem.Active;
            if (expected.Count > 0 && (pool == null || pool.m_prefabs == null)) return false;
            foreach (var entry in expected)
            {
                var guid = new AssetGuid { Value = entry.Guid };
                if (ctx.m_assetByGuid.ContainsKey(guid) || ctx.m_loadedWrappers.ContainsKey(guid) ||
                    (ctx.m_wrappersBeingLoaded != null && ctx.m_wrappersBeingLoaded.ContainsKey(guid)))
                    throw new InvalidOperationException("Enemy weapon GUID already registered: " + entry.Path);
                if (!ctx.TryFindWrapperImpl(new AssetGuid { Value = entry.SourceGuid }, out var sourceBase, true)) return false;
                entry.Source = sourceBase?.TryCast<EnemyWeaponDataAsset>();
                if (entry.Source == null || entry.Source.AssetGuid.Value != entry.SourceGuid)
                    throw new InvalidOperationException("Enemy weapon source wrapper type/GUID mismatch: " + entry.Key);
                entry.Prefab = entry.Source.PrefabRef;
                if (!ReadRoot(ctx, pool, entry.Prefab, out entry.Root)) return false;
                VerifyRoot(entry.Root, entry.Key);
            }
            for (int i = 0; i < ctx.m_allAssets.Count; ++i)
            {
                var asset = ctx.m_allAssets[i];
                if (asset != null && (guids.Contains(asset.Guid.Value) || paths.Contains(asset.Path)))
                    throw new InvalidOperationException("Enemy weapon asset list collision: " + asset.Path);
            }
            try
            {
                foreach (var entry in expected)
                {
                    entry.Wrapper = ScriptableObject.CreateInstance<EnemyWeaponDataAsset>();
                    if (entry.Wrapper == null) throw new InvalidOperationException("Cannot create enemy weapon wrapper.");
                    entry.Wrapper.name = entry.Path;
                    entry.Wrapper.hideFlags = HideFlags.DontSave;
                    entry.Wrapper.AssetGuid = new AssetGuid { Value = entry.Guid };
                    entry.Wrapper.PrefabRef = entry.Prefab;
                    ValidateEntry(entry);
                }
                foreach (var entry in expected)
                {
                    var guid = new AssetGuid { Value = entry.Guid };
                    ctx.m_assetByGuid.Add(guid, entry.Data);
                    ctx.m_loadedWrappers.Add(guid, entry.Wrapper);
                    entry.ListIndex = ctx.m_allAssets.Count;
                    ctx.m_allAssets.Add(entry.Data);
                }
                owner = new Owner { Context = ctx, Assets = ctx.m_assetByGuid.Pointer, Wrappers = ctx.m_loadedWrappers.Pointer,
                    List = ctx.m_allAssets.Pointer, Pool = pool?.Pointer ?? IntPtr.Zero, ProfileCount = catalog.Profiles.Length, Entries = expected };
                ValidateOwned(ctx, catalog.Profiles.Length, expected, owner);
                var resolution = ctx.Cast<IAssetResolutionContext>();
                foreach (var entry in expected)
                    if (entry.Wrapper.Get(resolution)?.Pointer != entry.Data.Pointer || entry.Wrapper.AssetObject?.Pointer != entry.Data.Pointer)
                        throw new InvalidOperationException("Enemy weapon wrapper canonical lookup failed: " + entry.Path);
                Owners.Add(ctx.Pointer, owner);
                return true;
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                Rollback(ctx, expected, errors);
                if (errors.Count > 1) throw new AggregateException("Enemy weapon registration/rollback failed.", errors);
                throw;
            }
        }

        private static bool Same(PooledPrefabRef a, PooledPrefabRef b) => a.Guid.m_data1 == b.Guid.m_data1 && a.Guid.m_data2 == b.Guid.m_data2;
        private static void RequireCaches(RuntimeAssetResolutionContext ctx)
        {
            if (ctx.m_assetByGuid == null || ctx.m_loadedWrappers == null || ctx.m_allAssets == null)
                throw new InvalidOperationException("Enemy weapon resolver caches are unavailable.");
        }
        private static void VerifyRoot(GameObject root, string key)
        {
            if (root == null || root.GetComponent(Il2CppType.Of<EnemyWeaponView>())?.TryCast<EnemyWeaponView>() == null)
                throw new InvalidOperationException("Enemy weapon pooled root lacks EnemyWeaponView: " + key);
        }
        private static bool ReadRoot(RuntimeAssetResolutionContext ctx, PoolingSystem pool, PooledPrefabRef prefab, out GameObject root)
        {
            root = null;
            if ((prefab.Guid.m_data1 == 0 && prefab.Guid.m_data2 == 0) ||
                !pool.TryResolvePrefab(prefab, out var index) || index < 0 || index >= pool.m_prefabs.Length)
                throw new InvalidOperationException("Enemy weapon prefab is absent from current pool.");
            var native = pool.m_prefabs[index];
            if (native.RootInstance != null) VerifyRoot(native.RootInstance, "pool-index=" + index);
            if (string.IsNullOrWhiteSpace(native.LoadKey)) throw new InvalidOperationException("Enemy weapon prefab load key is missing.");
            var key = ctx.Pointer.ToString() + ":" + pool.Pointer.ToString() + ":" + index;
            if (!Loads.TryGetValue(key, out var load))
            {
                var op = pool.LoadOpCreate(native.LoadKey);
                load = new Load { Context = ctx, Pool = pool, LoadKey = native.LoadKey, Read = () => {
                    if (!pool.LoadOpDone(op)) return null;
                    var result = op.Result; // Asset result only, never activating LoadOpResult.
                    if (result == null) throw new InvalidOperationException("Enemy weapon source root load failed: " + native.LoadKey);
                    return result;
                } };
                Loads.Add(key, load);
            }
            if (load.Context.Pointer != ctx.Pointer || load.Pool.Pointer != pool.Pointer || load.LoadKey != native.LoadKey)
                throw new InvalidOperationException("Enemy weapon root load ownership changed.");
            root = load.Read();
            return root != null;
        }
        private static void ValidateEntry(Entry e)
        {
            VerifyRoot(e.Root, e.Key);
            if (e.Data == null || e.Data.Pointer == IntPtr.Zero || e.Data.Guid.Value != e.Guid || e.Data.Path != e.Path ||
                e.Source == null || e.Source.AssetGuid.Value != e.SourceGuid || !Same(e.Source.PrefabRef, e.Prefab) ||
                e.Wrapper == null || e.Wrapper.AssetGuid.Value != e.Guid || !Same(e.Wrapper.PrefabRef, e.Prefab))
                throw new InvalidOperationException("Enemy weapon configuration changed: " + e.Path);
        }
        private static void ValidateOwned(RuntimeAssetResolutionContext ctx, int count, List<Entry> expected, Owner owner)
        {
            RequireCaches(ctx);
            if (owner.Context.Pointer != ctx.Pointer || owner.Assets != ctx.m_assetByGuid.Pointer || owner.Wrappers != ctx.m_loadedWrappers.Pointer ||
                owner.List != ctx.m_allAssets.Pointer || owner.ProfileCount != count || owner.Entries.Count != expected.Count ||
                (expected.Count > 0 && (PoolingSystem.Active == null || PoolingSystem.Active.Pointer != owner.Pool)))
                throw new InvalidOperationException("Enemy weapon context/catalog/pool changed after registration.");
            for (int i = 0; i < expected.Count; ++i)
            {
                var e = owner.Entries[i]; var p = expected[i];
                if (e.Index != p.Index || e.Key != p.Key || e.Guid != p.Guid || e.Path != p.Path || e.SourceGuid != p.SourceGuid || e.Data.Pointer != p.Data.Pointer)
                    throw new InvalidOperationException("Enemy weapon canonical snapshot changed after registration.");
                ValidateEntry(e);
                if (!ctx.TryFindWrapperImpl(new AssetGuid { Value = e.SourceGuid }, out var source, false) || source?.Pointer != e.Source.Pointer ||
                    !ReadRoot(ctx, PoolingSystem.Active, e.Prefab, out var root) || root.Pointer != e.Root.Pointer)
                    throw new InvalidOperationException("Enemy weapon source ownership was lost: " + e.Key);
                var guid = new AssetGuid { Value = e.Guid };
                if (!ctx.m_assetByGuid.TryGetValue(guid, out var data) || data?.Pointer != e.Data.Pointer ||
                    !ctx.m_loadedWrappers.TryGetValue(guid, out var wrapper) || wrapper?.Pointer != e.Wrapper.Pointer ||
                    e.ListIndex < 0 || e.ListIndex >= ctx.m_allAssets.Count || ctx.m_allAssets[e.ListIndex]?.Pointer != e.Data.Pointer)
                    throw new InvalidOperationException("Enemy weapon registration ownership was lost: " + e.Path);
                var resolution = ctx.Cast<IAssetResolutionContext>();
                if (e.Wrapper.Get(resolution)?.Pointer != e.Data.Pointer || e.Wrapper.AssetObject?.Pointer != e.Data.Pointer)
                    throw new InvalidOperationException("Enemy weapon resolver identity changed: " + e.Path);
            }
        }
        private static void Rollback(RuntimeAssetResolutionContext ctx, List<Entry> entries, List<Exception> errors)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                var e = entries[i]; var guid = new AssetGuid { Value = e.Guid }; bool released = false;
                try
                {
                    if (!ctx.m_loadedWrappers.TryGetValue(guid, out var w)) released = true;
                    else if (e.Wrapper != null && w?.Pointer == e.Wrapper.Pointer)
                    {
                        if (!ctx.m_loadedWrappers.Remove(guid)) throw new InvalidOperationException("Cannot remove owned enemy weapon wrapper.");
                        released = true;
                    }
                    else throw new InvalidOperationException("Foreign enemy weapon wrapper retained during rollback.");
                }
                catch (Exception error) { errors.Add(error); }
                try
                {
                    if (ctx.m_assetByGuid.TryGetValue(guid, out var data))
                    {
                        if (data?.Pointer != e.Data.Pointer) throw new InvalidOperationException("Foreign enemy weapon data retained during rollback.");
                        if (!ctx.m_assetByGuid.Remove(guid)) throw new InvalidOperationException("Cannot remove owned enemy weapon data.");
                    }
                    for (int j = ctx.m_allAssets.Count - 1; j >= 0; --j)
                        if (ctx.m_allAssets[j]?.Pointer == e.Data.Pointer) ctx.m_allAssets.RemoveAt(j);
                }
                catch (Exception error) { errors.Add(error); }
                if (released && e.Wrapper != null)
                    try { UnityEngine.Object.Destroy(e.Wrapper); } catch (Exception error) { errors.Add(error); }
            }
        }
    }
}
