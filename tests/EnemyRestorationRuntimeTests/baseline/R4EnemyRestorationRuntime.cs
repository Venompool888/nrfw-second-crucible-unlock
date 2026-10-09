using System;
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
                _catalog = catalog;
                _lastReadyViewContext = IntPtr.Zero;
                _reportedFailure = 0;
                _reportedViewWaitStates = 0;
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
                // Prove ownership before any writes or global wrapper lookups.
                // A retired resolver must not disable the current world's module.
                if (Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer != ctx.Pointer)
                {
                    _currentViewsReady = false;
                    ReportViewWait(2, "current resolver changed");
                    return;
                }
                if (EnemyRestorationViews.Ensure(ctx, _catalog))
                {
                    bool current=true;
                    try
                    {
                        foreach(var p in _catalog.Profiles)
                        {
                            var guid=new AssetGuid {Value=p.ViewGuid};
                            if(!ctx.m_assetByGuid.TryGetValue(guid,out var data) || data==null ||
                                !ctx.m_loadedWrappers.TryGetValue(guid,out var wrapper) || wrapper==null ||
                                wrapper.AssetObject?.Pointer!=data.Pointer) { current=false; break; }
                        }
                    }
                    catch { current=false; }
                    current = current && Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer == ctx.Pointer;
                    _currentViewsReady=current;
                    if(!current) { ReportViewWait(4, "global wrapper resolution"); return; }
                    if(_lastReadyViewContext!=ctx.Pointer)
                    {
                        _lastReadyViewContext = ctx.Pointer;
                        TrialRepairLog.Write("[enemy-restoration] VIEWS READY profiles=" + _catalog.Profiles.Length +
                            "; retained prefab GUID/name pairs verified; no inherited donor costume");
                    }
                }
                else
                {
                    _currentViewsReady = false;
                    ReportViewWait(8, "official pooled prefab LUT");
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
