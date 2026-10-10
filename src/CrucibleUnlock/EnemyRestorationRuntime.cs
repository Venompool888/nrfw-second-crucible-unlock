using System;
using System.Collections.Generic;
using System.Threading;
using Il2Cpp;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Initialization owns simulation writes. The loader's Unity main-thread
    // update pumps the current resolver. No cached Verified frame, and no spawns.
    internal static class EnemyRestorationRuntime
    {
        private static EnemyRestorationCatalog _catalog;
        private static volatile bool _enabled;
        private static IntPtr _lastReadyViewContext;
        private static int _reportedFailure;
        private static int _reportedViewWaitStates;
        private static volatile bool _currentViewsReady;
        private static IntPtr _readyViewManager;
        private static readonly object ViewSnapshotGate = new object();
        private static readonly Dictionary<IntPtr, SimulationViewSnapshot> ViewSnapshots = new Dictionary<IntPtr, SimulationViewSnapshot>();
        private sealed class SimulationViewSnapshot
        {
            // Strong references prevent manager pointer reuse and keep the exact
            // simulation assets alive. No Frame or storage survives this callback.
            internal readonly object Manager;
            internal readonly EntityViewData[] Views;
            internal readonly IntPtr[] ViewPointers;
            internal readonly EnemyWeaponData[] Weapons;
            internal readonly IntPtr[] WeaponPointers;
            internal SimulationViewSnapshot(object manager, EntityViewData[] views, IntPtr[] pointers, EnemyWeaponData[] weapons, IntPtr[] weaponPointers)
            { Manager = manager; Views = views; ViewPointers = pointers; Weapons = weapons; WeaponPointers = weaponPointers; }
        }
        internal static bool CurrentViewsReady => _enabled && _currentViewsReady;
        internal static EnemyRestorationCatalog Catalog => _catalog;

        public static void Install(string modsDirectory, bool attachSimulationHook = true)
        {
            if (_enabled) return;
            BuildGuard.VerifyEnemyDatabase(modsDirectory);
            var catalog = EnemyRestorationCatalog.Load();
            var owner = new PatchOwner("NRFW.CrucibleUnlock.EnemyRestoration");
            try
            {
                if(attachSimulationHook)
                    TrialRepairLog.Patch(owner, typeof(InitializeContentSystem), "OnInit", new[] { typeof(Frame) },
                        typeof(EnemyRestorationRuntime), postfix: nameof(AfterSimulationInit));
                TrialRepairLog.Patch(owner, typeof(Il2CppMoon.Playmode.InitializeQuantumAssetDatabase), "InitializeResourceManager",
                    new[] { typeof(Il2CppMoon.TheLoop.ViewFrame) }, typeof(EnemyRestorationRuntime), prefix: nameof(BeforeResourceManagerReset));
                EnemyRestorationActionHooks.Install(owner);
                _catalog = catalog;
                _lastReadyViewContext = IntPtr.Zero;
                _reportedFailure = 0;
                _reportedViewWaitStates = 0;
                _currentViewsReady = false;
                _readyViewManager = IntPtr.Zero;
                lock (ViewSnapshotGate) ViewSnapshots.Clear();
                _enabled = true;
                TrialRepairLog.Write("[enemy-restoration] installed; profiles=" + catalog.Profiles.Length +
                    "; build=" + catalog.SteamBuildId + "; qdb SHA256 match; spawn wiring deferred");
            }
            catch
            {
                _enabled = false;
                _catalog = null;
                owner.UnpatchSelf();
                throw;
            }
        }

        internal static void EnsureSimulationAssets(Frame frame)
        {
            if(!_enabled || _catalog==null) throw new InvalidOperationException("Enemy module is unavailable.");
            EnemyRestorationRegistration.Register(_catalog,new EnemyRestorationNativeBackend(frame));
        }

        internal static void PublishSimulationViews(Frame frame, bool requireStorage = false)
        {
            if (!_enabled || _catalog == null || frame == null) throw new InvalidOperationException("Enemy module is unavailable.");
            var manager = frame.Context.ResourceManager;
            if (manager == null || manager.Pointer == IntPtr.Zero) throw new InvalidOperationException("Simulation resource manager is unavailable.");
            var views = new EntityViewData[_catalog.Profiles.Length];
            var viewPointers = new IntPtr[views.Length];
            // Only pruned weapons have private simulation assets. Their Unity
            // wrappers must resolve these exact objects, including the filtered
            // moveset; the seven untouched static weapons keep official wrappers.
            var weapons = new EnemyWeaponData[views.Length];
            var weaponPointers = new IntPtr[views.Length];
            int privateWeapons = 0;
            for (int i = 0; i < views.Length; i++)
            {
                var profile = _catalog.Profiles[i];
                var view = frame.DynamicAssetDB.FindAsset(new AssetGuid { Value = profile.ViewGuid })?.TryCast<EntityViewData>();
                if (view == null || view.Pointer == IntPtr.Zero || view.Guid.Value != profile.ViewGuid || view.Path != profile.Path("view"))
                    throw new InvalidOperationException("Simulation view mismatch: " + profile.Key);
                if (requireStorage)
                {
                    var slot = frame.InstanceDataStorage?.m_storageEntityViewData;
                    if (slot == null || !slot.m_lookup.TryGetValue(profile.ViewGuid, out int index) || index <= 0 ||
                        index > slot.m_storage.Count || slot.m_storage[index - 1]?.Pointer != view.Pointer)
                        throw new InvalidOperationException("Simulation view storage mismatch: " + profile.Key);
                }
                views[i] = view;
                viewPointers[i] = view.Pointer;
                if (profile.BlockedActions.Length > 0)
                {
                    var weapon = frame.DynamicAssetDB.FindAsset(new AssetGuid { Value = profile.PrivateWeaponGuid })?.TryCast<EnemyWeaponData>();
                    if (weapon == null || weapon.Pointer == IntPtr.Zero || weapon.Guid.Value != profile.PrivateWeaponGuid || weapon.Path != profile.Path("weapon"))
                        throw new InvalidOperationException("Simulation private weapon mismatch: " + profile.Key);
                    weapons[i] = weapon;
                    weaponPointers[i] = weapon.Pointer;
                    ++privateWeapons;
                }
            }
            var managerPointer = manager.Pointer;
            lock (ViewSnapshotGate)
            {
                if (!_enabled) throw new InvalidOperationException("Enemy module stopped during view publication.");
                if (ViewSnapshots.TryGetValue(managerPointer, out var existing))
                {
                    for (int i = 0; i < views.Length; i++)
                        if (existing.ViewPointers[i] != viewPointers[i])
                            throw new InvalidOperationException("Simulation view identity changed for the same resource manager.");
                        else if (existing.WeaponPointers[i] != weaponPointers[i])
                            throw new InvalidOperationException("Simulation private weapon identity changed for the same resource manager.");
                    return;
                }
                ViewSnapshots.Add(managerPointer, new SimulationViewSnapshot(manager, views, viewPointers, weapons, weaponPointers));
            }
            TrialRepairLog.Write("[enemy-restoration] SIMULATION VIEWS PUBLISHED profiles=" + views.Length +
                "; privateWeapons=" + privateWeapons + "; manager=" + managerPointer + "; storageChecked=" + requireStorage.ToString().ToLowerInvariant());
        }

        internal static bool IsSimulationViewsReady(Frame frame) => CurrentViewsReady && frame != null &&
            frame.Context.ResourceManager?.Pointer == _readyViewManager;

        private static void BeforeResourceManagerReset()
        {
            _currentViewsReady=false;
            _readyViewManager=IntPtr.Zero;
            _lastReadyViewContext=IntPtr.Zero;
            EnemyRestorationKeeperWeaponViews.StopWithoutUnityReads();
            EnemyRestorationReactionViews.StopWithoutUnityReads();
        }

        private static void AfterSimulationInit(Frame __0)
        {
            if (!_enabled || __0 == null) return;
            try
            {
                var assets = EnemyRestorationRegistration.Register(_catalog, new EnemyRestorationNativeBackend(__0));
                PublishSimulationViews(__0);
                TrialRepairLog.Write("[enemy-restoration] SIMULATION READY profiles=" + _catalog.Profiles.Length +
                    "; assets=" + assets.Count + "; independent hosts/controllers/views/NPCs; no spawn changes");
            }
            catch (Exception error) { Fail("simulation initialization", error); }
        }

        internal static void PumpCurrentViews()
        {
            // Official UpdateAsyncQuantumViewAssetLoading.Tick inlines the
            // resolver Update body, so a detour on that body has no live caller.
            // Melon OnUpdate supplies retries without relying on native inlining.
            if (!_enabled) return;
            try
            {
                var resolver = Il2CppMoon.Forsaken.Core.AssetCtx;
                var ctx = resolver?.TryCast<RuntimeAssetResolutionContext>();
                if (ctx == null)
                {
                    _currentViewsReady = false;
                    _lastReadyViewContext = IntPtr.Zero;
                    ReportViewWait(1, "current runtime resolver");
                    return;
                }
                if (_lastReadyViewContext != ctx.Pointer) _currentViewsReady = false;
                var manager = Il2CppMoon.Forsaken.Core.ResourceManager;
                var managerPointer = manager?.Pointer ?? IntPtr.Zero;
                if (_readyViewManager != managerPointer) _currentViewsReady = false;
                SimulationViewSnapshot snapshot;
                lock (ViewSnapshotGate) ViewSnapshots.TryGetValue(managerPointer, out snapshot);
                if (snapshot == null)
                {
                    _currentViewsReady = false;
                    string publishedManagers;
                    lock (ViewSnapshotGate) publishedManagers = string.Join(",", ViewSnapshots.Keys);
                    ReportViewWait(16, "simulation views publication; current-manager=" + managerPointer + "; published-managers=" + publishedManagers);
                    return;
                }
                // Prove ownership before any writes or global wrapper lookups.
                // A retired resolver must not disable the current world's module.
                if (Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer != ctx.Pointer ||
                    Il2CppMoon.Forsaken.Core.ResourceManager?.Pointer != managerPointer)
                {
                    _currentViewsReady = false;
                    ReportViewWait(2, "current resolver changed");
                    return;
                }
                if (!EnemyRestorationViews.Ensure(ctx, _catalog, snapshot.Views))
                {
                    _currentViewsReady = false;
                    ReportViewWait(8, "official pooled prefab LUT");
                    return;
                }
                if (Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer != ctx.Pointer ||
                    Il2CppMoon.Forsaken.Core.ResourceManager?.Pointer != managerPointer)
                {
                    _currentViewsReady = false;
                    ReportViewWait(2, "current resolver changed");
                    return;
                }
                if (EnemyRestorationWeaponViews.Ensure(ctx, _catalog, snapshot.Weapons))
                {
                    if (!EnemyRestorationKeeperWeaponViews.Ensure(ctx, _catalog))
                    {
                        _currentViewsReady = false;
                        ReportViewWait(64, "official keeper weapon template");
                        return;
                    }
                    if (!EnemyRestorationReactionViews.Ensure(ctx, _catalog))
                    {
                        _currentViewsReady = false;
                        ReportViewWait(128, "official sword parry reaction template");
                        return;
                    }
                    bool current=true;
                    try
                    {
                        for (int i = 0; i < _catalog.Profiles.Length; i++)
                        {
                            var p = _catalog.Profiles[i];
                            var guid=new AssetGuid {Value=p.ViewGuid};
                            if(!ctx.m_assetByGuid.TryGetValue(guid,out var data) || data==null ||
                                !ctx.m_loadedWrappers.TryGetValue(guid,out var wrapper) || wrapper==null ||
                                data.Pointer != snapshot.ViewPointers[i] || wrapper.AssetObject?.Pointer!=data.Pointer) { current=false; break; }
                            if (p.BlockedActions.Length == 0) continue;
                            var weaponGuid = new AssetGuid { Value = p.PrivateWeaponGuid };
                            if (!ctx.m_assetByGuid.TryGetValue(weaponGuid,out var weapon) || weapon == null ||
                                !ctx.m_loadedWrappers.TryGetValue(weaponGuid,out var weaponWrapper) || weaponWrapper == null ||
                                weapon.Pointer != snapshot.WeaponPointers[i] || weaponWrapper.AssetObject?.Pointer != weapon.Pointer) { current=false; break; }
                        }
                    }
                    catch { current=false; }
                    current = current && Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer == ctx.Pointer &&
                        Il2CppMoon.Forsaken.Core.ResourceManager?.Pointer == managerPointer;
                    bool changedReadyManager = _readyViewManager != managerPointer;
                    if (current) _readyViewManager = managerPointer;
                    _currentViewsReady=current;
                    if(!current) { ReportViewWait(4, "global wrapper resolution"); return; }
                    if(_lastReadyViewContext!=ctx.Pointer || changedReadyManager)
                    {
                        _lastReadyViewContext = ctx.Pointer;
                        TrialRepairLog.Write("[enemy-restoration] VIEWS READY profiles=" + _catalog.Profiles.Length +
                            "; manager=" + managerPointer + "; character/private weapon wrappers, keeper weapon and sword parry templates verified; official costume settings retained");
                    }
                }
                else
                {
                    _currentViewsReady = false;
                    ReportViewWait(32, "official private weapon views");
                }
            }
            catch (Exception error) { Fail("Unity view resolution", error); }
        }

        private static void ReportViewWait(int state, string reason)
        {
            if ((_reportedViewWaitStates & state) != 0) return;
            _reportedViewWaitStates |= state;
            TrialRepairLog.Write("[enemy-restoration] VIEWS WAIT: " + reason + "; main-thread retry active");
        }

        private static void Fail(string stage, Exception error)
        {
            _enabled = false;
            _currentViewsReady = false;
            _readyViewManager = IntPtr.Zero;
            if (Interlocked.Exchange(ref _reportedFailure, 1) == 0)
                TrialRepairLog.Write("[enemy-restoration] DISABLED during " + stage + ": " + error);
        }

        // Future spawn code must request the NPC from its current simulation
        // callback frame. This never constructs entities or retains a Frame.
        public static bool TryGetNpc(Frame frame, string key, out AssetRefNpcData npc)
        {
            npc = default;
            if (!IsSimulationViewsReady(frame) || key == null) return false;
            foreach (var profile in _catalog.Profiles)
            {
                if (!string.Equals(profile.Key, key, StringComparison.Ordinal)) continue;
                var asset = frame.DynamicAssetDB.FindAsset(new AssetGuid { Value = profile.NpcGuid });
                var data = asset?.TryCast<NpcData>();
                if (data == null || data.Guid.Value != profile.NpcGuid || data.Path != profile.Path("npc") ||
                    data.EntityDataAssetRef.Id.Value != profile.HostGuid || data.ControllerDataAssetRef.Id.Value != profile.ControllerGuid)
                    return false;
                npc = new AssetRefNpcData { Id = new AssetGuid { Value = profile.NpcGuid } };
                return true;
            }
            return false;
        }

        public static void StopWithoutUnityReads()
        {
            _enabled = false;
            _currentViewsReady = false;
            _readyViewManager = IntPtr.Zero;
            lock (ViewSnapshotGate) ViewSnapshots.Clear();
            EnemyRestorationKeeperWeaponViews.StopWithoutUnityReads();
            EnemyRestorationReactionViews.StopWithoutUnityReads();
            // Do not touch native objects or unpatch hooks during process teardown.
        }
    }
}
