using System;
using System.Threading;
using Il2Cpp;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Initialization owns simulation writes. Unity's resolution callback owns
    // presentation writes. No polling of a cached Verified frame, and no spawns.
    internal static class EnemyRestorationRuntime
    {
        private static EnemyRestorationCatalog _catalog;
        private static volatile bool _enabled;
        private static IntPtr _lastReadyViewContext;
        private static int _reportedFailure;
        private static volatile bool _currentViewsReady;
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
                TrialRepairLog.Patch(owner, typeof(RuntimeAssetResolutionContext), "Update", Type.EmptyTypes,
                    typeof(EnemyRestorationRuntime), prefix: nameof(BeforeResolutionUpdate));
                TrialRepairLog.Patch(owner, typeof(Il2CppMoon.Playmode.InitializeQuantumAssetDatabase), "InitializeResourceManager",
                    new[] { typeof(Il2CppMoon.TheLoop.ViewFrame) }, typeof(EnemyRestorationRuntime), prefix: nameof(BeforeResourceManagerReset));
                _catalog = catalog;
                _lastReadyViewContext = IntPtr.Zero;
                _reportedFailure = 0;
                _currentViewsReady = false;
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

        private static void BeforeResourceManagerReset()
        {
            _currentViewsReady=false;
            _lastReadyViewContext=IntPtr.Zero;
        }

        private static void AfterSimulationInit(Frame __0)
        {
            if (!_enabled || __0 == null) return;
            try
            {
                var assets = EnemyRestorationRegistration.Register(_catalog, new EnemyRestorationNativeBackend(__0));
                TrialRepairLog.Write("[enemy-restoration] SIMULATION READY profiles=" + _catalog.Profiles.Length +
                    "; assets=" + assets.Count + "; independent hosts/controllers/views/NPCs; no spawn changes");
            }
            catch (Exception error) { Fail("simulation initialization", error); }
        }

        private static void BeforeResolutionUpdate(RuntimeAssetResolutionContext __instance)
        {
            if (!_enabled || __instance == null) return;
            try
            {
                if(_lastReadyViewContext!=__instance.Pointer) _currentViewsReady=false;
                if (EnemyRestorationViews.Ensure(__instance, _catalog))
                {
                    // A retired context can still receive an Update after the global
                    // resolver switches worlds. Check that exact resolver on this Unity
                    // callback before publishing a managed readiness flag to simulation.
                    bool current=true;
                    try
                    {
                        foreach(var p in _catalog.Profiles)
                        {
                            var guid=new AssetGuid {Value=p.ViewGuid};
                            if(!__instance.m_assetByGuid.TryGetValue(guid,out var data) || data==null ||
                                !__instance.m_loadedWrappers.TryGetValue(guid,out var wrapper) || wrapper==null ||
                                wrapper.AssetObject?.Pointer!=data.Pointer) { current=false; break; }
                        }
                    }
                    catch { current=false; } // Retired context / new resolver not loaded yet; retry on its own Update.
                    _currentViewsReady=current;
                    if(!current) return;
                    if(_lastReadyViewContext!=__instance.Pointer)
                    {
                        _lastReadyViewContext = __instance.Pointer;
                        TrialRepairLog.Write("[enemy-restoration] VIEWS READY profiles=" + _catalog.Profiles.Length +
                            "; retained prefab GUID/name pairs verified; no inherited donor costume");
                    }
                }
            }
            catch (Exception error) { Fail("Unity view resolution", error); }
        }

        private static void Fail(string stage, Exception error)
        {
            _enabled = false;
            _currentViewsReady = false;
            if (Interlocked.Exchange(ref _reportedFailure, 1) == 0)
                TrialRepairLog.Write("[enemy-restoration] DISABLED during " + stage + ": " + error);
        }

        // Future spawn code must request the NPC from its current simulation
        // callback frame. This never constructs entities or retains a Frame.
        public static bool TryGetNpc(Frame frame, string key, out AssetRefNpcData npc)
        {
            npc = default;
            if (!CurrentViewsReady || frame == null || key == null) return false;
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
            // Do not touch native objects or unpatch hooks during process teardown.
        }
    }
}
