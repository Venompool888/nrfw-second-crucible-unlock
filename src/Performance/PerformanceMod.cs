using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppMoon.AnimationControl;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using Il2CppUberShader.optimizations.gc;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: MelonInfo(typeof(Performance.PerformanceMod), "NRFW Performance", Performance.PerformancePolicy.Version, "NRFW research")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
[assembly: MelonAdditionalDependencies("0Harmony")]

namespace Performance
{
    public sealed partial class PerformanceMod : MelonMod
    {
        private static readonly ConcurrentQueue<string> PendingLogs = new();
        private static string _logPath;

        // Preferences
        private static MelonPreferences_Entry<bool> _prefEnabled;
        private static MelonPreferences_Entry<string> _prefPreset;
        private static MelonPreferences_Entry<bool> _prefShowHud;
        private static MelonPreferences_Entry<bool> _prefUnlockWorkers;
        private static MelonPreferences_Entry<bool> _prefBlockActiveUnloads;
        private static MelonPreferences_Entry<long> _prefMinFreeRamMb;
        private static MelonPreferences_Entry<bool> _prefGuardLadder;
        private static MelonPreferences_Entry<bool> _prefHalfResAo;
        private static MelonPreferences_Entry<bool> _prefHalfResFlashLight;
        private static MelonPreferences_Entry<bool> _prefHalfResTransparency;
        private static MelonPreferences_Entry<bool> _prefOptimizeShadows;
        private static MelonPreferences_Entry<bool> _prefOptimizeVolumetrics;
        private static MelonPreferences_Entry<bool> _prefOptimizeSsr;
        private static MelonPreferences_Entry<bool> _prefOptimizeReflections;
        private static MelonPreferences_Entry<bool> _prefOptimizeCullingCloth;
        private static MelonPreferences_Entry<bool> _prefExpDisableAssetBlending;
        private static MelonPreferences_Entry<bool> _prefExpSsrHiZ;
        private static MelonPreferences_Entry<bool> _prefCpuProfilerEnabled;
        private static MelonPreferences_Entry<int> _prefLogIntervalSeconds;

        // Runtime state
        internal static bool Enabled = true;
        internal static PerfPreset ActivePreset = PerfPreset.UltraSmart;
        private static PerfPreset _lastNonVanillaPreset = PerfPreset.UltraSmart;
        internal static bool UnlockWorkers = true;
        internal static bool BlockActiveUnloads = true;
        internal static long MinFreeRamMb = 1200;
        internal static bool GuardLadderKickDown = true;
        internal static bool HalfResAo = true;
        internal static bool HalfResFlashLight = true;
        internal static bool HalfResTransparency = true;
        internal static bool OptimizeShadows = true;
        internal static bool OptimizeVolumetrics = true;
        internal static bool OptimizeSsr = true;
        internal static bool OptimizeReflections = true;
        internal static bool OptimizeCullingCloth = true;
        internal static bool ExpDisableAssetBlending;
        internal static bool ExpSsrHiZ;
        internal static bool CpuProfilerEnabled = true;
        internal static bool HudVisible;

        private static IntPtr _lastAssetPtr;
        private static bool _hasVanillaSnapshot;
        private static VanillaAssetSnapshot _vanilla;

        private static long _blockedUnloads;
        private static long _cleanupChecksLogged;
        private static long _ladderGuards;
        private static int _ladderErrors;
        private static int _physicsCompIndex = -1;
        private static bool _physicsCompLookupTried;

        private static readonly FrameStatsRing FrameRing = new(240);
        internal static readonly CpuStageTracker CpuStages = new(0.08f);
        private static double _lastRamCheckTime = -10.0;
        private static long _cachedFreeRamMb = -1;
        private static double _lastStatsLogTime;
        private static double _pendingSettledReportAt = -1.0;
        private static string _pendingSettledTag;

        private static bool _f9Down, _f10Down, _f11Down;
        private static bool _hudAvailable;

        private struct VanillaAssetSnapshot
        {
            public MoonRenderPipelineAsset.QualityPreset QualityPreset;
            public MoonRenderPipelineAsset.LimitWorkers LimitWorkerThreadCnt;
            public int WorkerThreadCnt;
            public MoonRenderPipelineAsset.IMXAOShadingRate AoShadingRate;
            public int FlashLightResDivider;
            public int FlashLightResDividerMinRes;
            public int FlashLightStepsMax;
            public float HalfResTransparencyScale;
            public bool ExcludeParticleShadowCasters;
            public MoonRenderPipelineAsset.ShadowResolution MainLightShadowMapRes;
            public MoonRenderPipelineAsset.TexResolution OtherLightShadowMapRes;
            public int ShadowCascadeCount;
            public float ShadowCasterMinArea;
            public int PcssSearchSamples;
            public int PcssMinSamples;
            public int PcssMaxSamples;
            public bool VolumetricUseLowResDepth;
            public int VolumetricSamples;
            public int SsrMaxSteps;
            public bool SsrUseHiZ;
            public float ReflPlaneTexScaling;
            public int MaxNumReflPlaneTextures;
            public float SmallObjectsMinScreenHeight;
            public float SmallObjectsMinDistance;
            public bool AssetBlendingPassEnabled;
            public float ClothDistanceCullingThreshold;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public override void OnInitializeMelon()
        {
            var userDir = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
            _logPath = Path.Combine(userDir, "NRFWPerformance.log");
            RotateLogFile(_logPath, Path.Combine(userDir, "NRFWPerformance.prev.log"));
            InitPreferences();

            var harmony = new HarmonyLib.Harmony("NRFW.Performance");
            InstallHook(harmony, typeof(MoonRenderPipelineAsset), "UpdateQualityPreset", Type.EmptyTypes,
                prefix: nameof(BeforeUpdateQualityPreset), postfix: nameof(AfterUpdateQualityPreset));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "FlashLightGetDepthMipIndex", new[] { typeof(int) },
                postfix: nameof(AfterFlashLightGetDepthMipIndex));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "HalfResTransparencyScale", Type.EmptyTypes,
                postfix: nameof(AfterHalfResTransparencyScale));
            InstallHook(harmony, typeof(UberGCLogic), "ShouldRunCleanup",
                new[] { typeof(UberGCLogic.CleanupType), typeof(UberGCLogic.CleanupTrigger), typeof(UberGCLogic.CleanupOutsideOfGameplayTrigger) },
                postfix: nameof(AfterShouldRunCleanup));
            InstallHook(harmony, typeof(LadderData), "KickDown",
                new[] { typeof(Frame), typeof(EntityRef) },
                prefix: nameof(BeforeLadderKickDown));

            // Zero-alloc CPU stage profiler hooks
            InstallHook(harmony, typeof(MoonRenderPipelineView), "Render",
                new[] { typeof(ScriptableRenderContext), typeof(Camera), typeof(Camera) },
                prefix: nameof(BeforeRenderTotal), postfix: nameof(AfterRenderTotal));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "Cull", Type.EmptyTypes,
                prefix: nameof(BeforeCull), postfix: nameof(AfterCull));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "CullShadowCasters", Type.EmptyTypes,
                prefix: nameof(BeforeShadowCull), postfix: nameof(AfterShadowCull));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "DrawVisibleGeometryPart1", Type.EmptyTypes,
                prefix: nameof(BeforeGBufferP1), postfix: nameof(AfterGBufferP1));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "UpdateLightsPart2", Type.EmptyTypes,
                prefix: nameof(BeforeCsmL2), postfix: nameof(AfterCsmL2));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "UpdateLightsPart3", Type.EmptyTypes,
                prefix: nameof(BeforeOtherLightsL3), postfix: nameof(AfterOtherLightsL3));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "DrawVisibleGeometryPart2", Type.EmptyTypes,
                prefix: nameof(BeforeLightFwdP2), postfix: nameof(AfterLightFwdP2));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "PostProcessing", Type.EmptyTypes,
                prefix: nameof(BeforePostFx), postfix: nameof(AfterPostFx));
            InstallHook(harmony, typeof(MoonRenderPipelineView), "DrawUI", Type.EmptyTypes,
                prefix: nameof(BeforeDrawUi), postfix: nameof(AfterDrawUi));
            InstallHook(harmony, typeof(MoonClothUpdate), "TickLate", new[] { typeof(ViewFrame) },
                prefix: nameof(BeforeCloth), postfix: nameof(AfterCloth));

            _hudAvailable = TryInitHud();
            Log($"init v{PerformancePolicy.Version}; enabled={Enabled}; preset={ActivePreset}; minFreeRamMb={MinFreeRamMb}; hud={HudVisible}; imgui={_hudAvailable}; freeRamMb={GetAvailableRamMb()}");
            FlushLogs();
        }

        private static void RotateLogFile(string logPath, string prevPath)
        {
            try
            {
                if (File.Exists(logPath))
                    File.Move(logPath, prevPath, overwrite: true);
            }
            catch { }
        }

        private static void InitPreferences()
        {
            var cat = MelonPreferences.CreateCategory("NRFWPerformance", "NRFW Performance");
            _prefEnabled = cat.CreateEntry("enabled", true, description: "Master enable switch for NRFWPerformance.");
            _prefPreset = cat.CreateEntry("preset", "UltraSmart", description: "Optimization preset: Vanilla, UltraSmart, BalancedPro, MaxFps.");
            _prefShowHud = cat.CreateEntry("show_hud_on_start", false, description: "Show F9 performance HUD on game start.");
            _prefUnlockWorkers = cat.CreateEntry("unlock_worker_threads", true, description: "Remove hardcoded 8-thread cap on Unity Job workers.");
            _prefBlockActiveUnloads = cat.CreateEntry("block_active_unloads", true, description: "Defer Proactive/Panic UnloadUnusedAssets during active gameplay when free RAM is above min_free_ram_mb.");
            _prefMinFreeRamMb = cat.CreateEntry("min_free_ram_mb", 1200L, description: "Minimum available system RAM (MB) required before blocking active-gameplay asset unloads.");
            _prefGuardLadder = cat.CreateEntry("guard_ladder_kickdown", true, description: "Skip LadderData.KickDown when streamed entity lacks PhysicsComponent to avoid exception trace storms.");
            _prefHalfResAo = cat.CreateEntry("half_res_ao", true, description: "Use native Half-res temporal iMXAO shading rate.");
            _prefHalfResFlashLight = cat.CreateEntry("half_res_flashlight", true, description: "Allow half-res depth raymarching for FlashLight even with FSR/DLSS active.");
            _prefHalfResTransparency = cat.CreateEntry("half_res_transparency", true, description: "Cap HalfResTransparencyScale to profile value (0.50-0.60) using bilateral depth upscaling.");
            _prefOptimizeShadows = cat.CreateEntry("optimize_shadows", true, description: "Exclude particle shadow casters, cap shadow atlas resolution/cascades, raise shadow min area, and tune PCSS samples.");
            _prefOptimizeVolumetrics = cat.CreateEntry("optimize_volumetrics", true, description: "Enable low-res depth and tuned sample count for VolumetricRenderer.");
            _prefOptimizeSsr = cat.CreateEntry("optimize_ssr", true, description: "Tune SSRMaxSteps to profile value.");
            _prefOptimizeReflections = cat.CreateEntry("optimize_reflections", true, description: "Cap ReflectionPlane texture scaling and max plane count.");
            _prefOptimizeCullingCloth = cat.CreateEntry("optimize_culling_cloth", true, description: "Tighten small object culling screen height and cloth distance culling.");
            _prefExpDisableAssetBlending = cat.CreateEntry("experimental_disable_asset_blending", false, description: "Experimental: disable AssetBlending full-screen GBuffer blend pass.");
            _prefExpSsrHiZ = cat.CreateEntry("experimental_ssr_hiz", false, description: "Experimental: enable Hi-Z acceleration on SSR.");
            _prefCpuProfilerEnabled = cat.CreateEntry("cpu_profiler_enabled", true, description: "Measure per-stage CPU times for F9 HUD and F11 dumps.");
            _prefLogIntervalSeconds = cat.CreateEntry("log_stats_interval_seconds", 30, description: "Interval in seconds for periodic performance stats log lines (0 disables).");

            Enabled = _prefEnabled.Value;
            ActivePreset = PerformancePolicy.ParsePreset(_prefPreset.Value);
            if (ActivePreset != PerfPreset.Vanilla) _lastNonVanillaPreset = ActivePreset;
            HudVisible = _prefShowHud.Value;
            UnlockWorkers = _prefUnlockWorkers.Value;
            BlockActiveUnloads = _prefBlockActiveUnloads.Value;
            // Migrate old v0.1.0 default 4096 down to 1200 if unchanged, or keep user's custom value
            if (_prefMinFreeRamMb.Value == 4096L) _prefMinFreeRamMb.Value = 1200L;
            MinFreeRamMb = Math.Max(512L, _prefMinFreeRamMb.Value);
            GuardLadderKickDown = _prefGuardLadder.Value;
            HalfResAo = _prefHalfResAo.Value;
            HalfResFlashLight = _prefHalfResFlashLight.Value;
            HalfResTransparency = _prefHalfResTransparency.Value;
            OptimizeShadows = _prefOptimizeShadows.Value;
            OptimizeVolumetrics = _prefOptimizeVolumetrics.Value;
            OptimizeSsr = _prefOptimizeSsr.Value;
            OptimizeReflections = _prefOptimizeReflections.Value;
            OptimizeCullingCloth = _prefOptimizeCullingCloth.Value;
            ExpDisableAssetBlending = _prefExpDisableAssetBlending.Value;
            ExpSsrHiZ = _prefExpSsrHiZ.Value;
            CpuProfilerEnabled = _prefCpuProfilerEnabled.Value;
        }

        internal static void SavePreferences()
        {
            _prefEnabled.Value = Enabled;
            _prefPreset.Value = ActivePreset.ToString();
            _prefShowHud.Value = HudVisible;
            _prefUnlockWorkers.Value = UnlockWorkers;
            _prefBlockActiveUnloads.Value = BlockActiveUnloads;
            _prefMinFreeRamMb.Value = MinFreeRamMb;
            _prefGuardLadder.Value = GuardLadderKickDown;
            _prefHalfResAo.Value = HalfResAo;
            _prefHalfResFlashLight.Value = HalfResFlashLight;
            _prefHalfResTransparency.Value = HalfResTransparency;
            _prefOptimizeShadows.Value = OptimizeShadows;
            _prefOptimizeVolumetrics.Value = OptimizeVolumetrics;
            _prefOptimizeSsr.Value = OptimizeSsr;
            _prefOptimizeReflections.Value = OptimizeReflections;
            _prefOptimizeCullingCloth.Value = OptimizeCullingCloth;
            _prefExpDisableAssetBlending.Value = ExpDisableAssetBlending;
            _prefExpSsrHiZ.Value = ExpSsrHiZ;
            _prefCpuProfilerEnabled.Value = CpuProfilerEnabled;
            MelonPreferences.Save();
            Log($"saved preferences preset={ActivePreset} enabled={Enabled}");
        }

        private static void InstallHook(HarmonyLib.Harmony harmony, Type targetType, string methodName, Type[] args, string prefix = null, string postfix = null)
        {
            try
            {
                var method = targetType.GetMethod(methodName, BindingFlagsForHook, null, args, null);
                if (method == null)
                {
                    Log($"HOOK MISSING: {targetType.FullName}.{methodName}");
                    return;
                }
                var pre = prefix != null ? new HarmonyMethod(typeof(PerformanceMod), prefix) : null;
                var post = postfix != null ? new HarmonyMethod(typeof(PerformanceMod), postfix) : null;
                harmony.Patch(method, prefix: pre, postfix: post);
                Log($"hook ok: {targetType.Name}.{methodName}");
            }
            catch (Exception e)
            {
                Log($"HOOK FAILED: {targetType.Name}.{methodName}: {e.GetType().Name}: {e.Message}");
            }
        }

        private const System.Reflection.BindingFlags BindingFlagsForHook =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly;

        public override void OnUpdate()
        {
            if (CpuProfilerEnabled) CpuStages.EndFrame();
            FrameRing.Push(Time.unscaledDeltaTime);

            var asset = MoonRenderPipeline.currentAsset;
            if (asset != null && asset.Pointer != _lastAssetPtr)
            {
                _lastAssetPtr = asset.Pointer;
                _hasVanillaSnapshot = false;
                EnsureApplied(asset, forceRecapture: true);
            }

            PollHotkeys();

            double now = Time.realtimeSinceStartupAsDouble;
            if (_pendingSettledReportAt > 0.0 && now >= _pendingSettledReportAt)
            {
                _pendingSettledReportAt = -1.0;
                DumpDetailedSnapshot(_pendingSettledTag ?? "AB-settled");
            }

            int interval = _prefLogIntervalSeconds != null ? _prefLogIntervalSeconds.Value : 30;
            if (interval > 0)
            {
                if (_lastStatsLogTime <= 0.0) _lastStatsLogTime = now;
                else if (now - _lastStatsLogTime >= interval)
                {
                    _lastStatsLogTime = now;
                    Log(FormatSummaryLine("periodic"));
                }
            }

            FlushLogs();
        }

        private static void PollHotkeys()
        {
            bool f9 = (GetAsyncKeyState(0x78) & 0x8000) != 0;
            if (f9 && !_f9Down)
            {
                HudVisible = !HudVisible;
                Log($"F9 HUD={(HudVisible ? "ON" : "OFF")}");
            }
            _f9Down = f9;

            bool f10 = (GetAsyncKeyState(0x79) & 0x8000) != 0;
            if (f10 && !_f10Down)
                ToggleVanillaAb();
            _f10Down = f10;

            bool f11 = (GetAsyncKeyState(0x7A) & 0x8000) != 0;
            if (f11 && !_f11Down)
                DumpDetailedSnapshot("F11");
            _f11Down = f11;
        }

        internal static void ToggleVanillaAb()
        {
            var prev = ActivePreset;
            if (!Enabled || ActivePreset == PerfPreset.Vanilla)
            {
                Enabled = true;
                ActivePreset = _lastNonVanillaPreset == PerfPreset.Vanilla ? PerfPreset.UltraSmart : _lastNonVanillaPreset;
            }
            else
            {
                _lastNonVanillaPreset = ActivePreset;
                ActivePreset = PerfPreset.Vanilla;
            }
            ReapplyCurrentAsset();
            FrameRing.Clear();
            CpuStages.Reset();
            _pendingSettledReportAt = Time.realtimeSinceStartupAsDouble + 5.0;
            _pendingSettledTag = $"AB-settled({ActivePreset})";
            Log($"AB-switch: {prev} -> {ActivePreset} (reset window; auto-snapshot in 5s)");
        }

        internal static void SelectPreset(PerfPreset preset)
        {
            var prev = ActivePreset;
            if (preset != PerfPreset.Vanilla)
            {
                _lastNonVanillaPreset = preset;
                Enabled = true;
            }
            ActivePreset = preset;
            ReapplyCurrentAsset();
            FrameRing.Clear();
            CpuStages.Reset();
            _pendingSettledReportAt = Time.realtimeSinceStartupAsDouble + 5.0;
            _pendingSettledTag = $"preset-settled({ActivePreset})";
            Log($"preset-switch: {prev} -> {ActivePreset} (auto-snapshot in 5s)");
        }

        internal static void ReapplyCurrentAsset()
        {
            var asset = MoonRenderPipeline.currentAsset;
            if (asset != null) EnsureApplied(asset, forceRecapture: false);
        }

        // Bug #1 fix: UpdateQualityPreset runs every frame. Only recapture vanilla when Selected != Current or first see.
        private static void BeforeUpdateQualityPreset(MoonRenderPipelineAsset __instance, out bool __state)
        {
            if (__instance == null)
            {
                __state = false;
                return;
            }
            __state = PerformancePolicy.ShouldRecaptureVanillaSnapshot(
                (int)__instance.SelectedQualityPreset,
                (int)__instance.CurrentQualityPreset,
                _hasVanillaSnapshot,
                __instance.Pointer != _lastAssetPtr);
        }

        private static void AfterUpdateQualityPreset(MoonRenderPipelineAsset __instance, bool __state)
        {
            if (!__state || __instance == null) return;
            _lastAssetPtr = __instance.Pointer;
            _hasVanillaSnapshot = false;
            EnsureApplied(__instance, forceRecapture: true);
        }

        private static void EnsureApplied(MoonRenderPipelineAsset asset, bool forceRecapture)
        {
            if (asset == null) return;
            if (!_hasVanillaSnapshot || forceRecapture)
            {
                _vanilla = CaptureSnapshot(asset);
                _hasVanillaSnapshot = true;
                Log($"captured vanilla SRP preset={_vanilla.QualityPreset} workers={_vanilla.LimitWorkerThreadCnt}/{_vanilla.WorkerThreadCnt} ao={_vanilla.AoShadingRate} flDiv={_vanilla.FlashLightResDivider}/{_vanilla.FlashLightResDividerMinRes}/{_vanilla.FlashLightStepsMax} halfTrans={_vanilla.HalfResTransparencyScale:0.00} mainShadow={_vanilla.MainLightShadowMapRes}/cas={_vanilla.ShadowCascadeCount}/minArea={_vanilla.ShadowCasterMinArea:0.0000} pcss={_vanilla.PcssSearchSamples}/{_vanilla.PcssMinSamples}/{_vanilla.PcssMaxSamples} volLow={_vanilla.VolumetricUseLowResDepth}/{_vanilla.VolumetricSamples} ssr={_vanilla.SsrMaxSteps}/hiz={_vanilla.SsrUseHiZ} refl={_vanilla.ReflPlaneTexScaling:0.00}x{_vanilla.MaxNumReflPlaneTextures} smallCull={_vanilla.SmallObjectsMinScreenHeight:0.000}@{_vanilla.SmallObjectsMinDistance:0.0}m clothDist={_vanilla.ClothDistanceCullingThreshold:0.0}");
            }

            RestoreSnapshot(asset, _vanilla);
            if (!Enabled || ActivePreset == PerfPreset.Vanilla) return;

            ApplyProfile(asset, _vanilla, PerformancePolicy.GetProfile(ActivePreset));
        }

        private static VanillaAssetSnapshot CaptureSnapshot(MoonRenderPipelineAsset a)
        {
            return new VanillaAssetSnapshot
            {
                QualityPreset = a.SelectedQualityPreset,
                LimitWorkerThreadCnt = a.LimitWorkerThreadCnt,
                WorkerThreadCnt = a.WorkerThreadCnt,
                AoShadingRate = a.iMXAOShadingRate,
                FlashLightResDivider = a.FlashLightResDivider,
                FlashLightResDividerMinRes = a.FlashLightResDividerMinRes,
                FlashLightStepsMax = a.FlashLightStepsMax,
                HalfResTransparencyScale = a.HalfResTransparencyScale,
                ExcludeParticleShadowCasters = a.ExcludePareticleSystemShadowCasters,
                MainLightShadowMapRes = a.MainLightShadowMapResolution,
                OtherLightShadowMapRes = a.ShadowMapResolution,
                ShadowCascadeCount = a.ShadowCascadeCount,
                ShadowCasterMinArea = a.ShadowCasterMinArea,
                PcssSearchSamples = a.PCSSSearchNumSamples,
                PcssMinSamples = a.PCSSMinNumSamples,
                PcssMaxSamples = a.PCSSMaxNumSamples,
                VolumetricUseLowResDepth = a.VolumetricUseLowResDepth,
                VolumetricSamples = a.VolumetricSamples,
                SsrMaxSteps = a.SSRMaxSteps,
                SsrUseHiZ = a.SSRUseHiZ,
                ReflPlaneTexScaling = a.ReflPlaneTexScaling,
                MaxNumReflPlaneTextures = a.MaxNumReflPlaneTextures,
                SmallObjectsMinScreenHeight = a.SmallObjectsCullingRelativeScreenHeight,
                SmallObjectsMinDistance = a.SmallObjectsCullingMinDistance,
                AssetBlendingPassEnabled = a.AssetBlendingPassEnabled,
                ClothDistanceCullingThreshold = MoonClothUpdate.DistanceCullingThreshold,
            };
        }

        private static void RestoreSnapshot(MoonRenderPipelineAsset a, in VanillaAssetSnapshot v)
        {
            a.LimitWorkerThreadCnt = v.LimitWorkerThreadCnt;
            a.WorkerThreadCnt = v.WorkerThreadCnt;
            a.iMXAOShadingRate = v.AoShadingRate;
            a.FlashLightResDivider = v.FlashLightResDivider;
            a.FlashLightResDividerMinRes = v.FlashLightResDividerMinRes;
            a.FlashLightStepsMax = v.FlashLightStepsMax;
            a.HalfResTransparencyScale = v.HalfResTransparencyScale;
            a.ExcludePareticleSystemShadowCasters = v.ExcludeParticleShadowCasters;
            a.MainLightShadowMapResolution = v.MainLightShadowMapRes;
            a.ShadowMapResolution = v.OtherLightShadowMapRes;
            a.ShadowCascadeCount = v.ShadowCascadeCount;
            a.ShadowCasterMinArea = v.ShadowCasterMinArea;
            a.PCSSSearchNumSamples = v.PcssSearchSamples;
            a.PCSSMinNumSamples = v.PcssMinSamples;
            a.PCSSMaxNumSamples = v.PcssMaxSamples;
            a.VolumetricUseLowResDepth = v.VolumetricUseLowResDepth;
            a.VolumetricSamples = v.VolumetricSamples;
            a.SSRMaxSteps = v.SsrMaxSteps;
            a.SSRUseHiZ = v.SsrUseHiZ;
            a.ReflPlaneTexScaling = v.ReflPlaneTexScaling;
            a.MaxNumReflPlaneTextures = v.MaxNumReflPlaneTextures;
            a.SmallObjectsCullingRelativeScreenHeight = v.SmallObjectsMinScreenHeight;
            a.SmallObjectsCullingMinDistance = v.SmallObjectsMinDistance;
            a.AssetBlendingPassEnabled = v.AssetBlendingPassEnabled;
            MoonClothUpdate.DistanceCullingThreshold = v.ClothDistanceCullingThreshold;
        }

        private static void ApplyProfile(MoonRenderPipelineAsset a, in VanillaAssetSnapshot v, in PresetProfile p)
        {
            if (UnlockWorkers)
                a.LimitWorkerThreadCnt = (MoonRenderPipelineAsset.LimitWorkers)PerformancePolicy.LimitWorkersNever;

            if (HalfResAo && a.iMXAOShadingRate == MoonRenderPipelineAsset.IMXAOShadingRate.Full)
                a.iMXAOShadingRate = (MoonRenderPipelineAsset.IMXAOShadingRate)PerformancePolicy.AoRateHalf;

            if (HalfResFlashLight)
            {
                a.FlashLightResDivider = Math.Max(a.FlashLightResDivider, p.FlashLightDivider);
                a.FlashLightResDividerMinRes = Math.Min(a.FlashLightResDividerMinRes, p.FlashLightMinHeight);
                a.FlashLightStepsMax = Math.Min(a.FlashLightStepsMax, p.FlashLightStepsMax);
            }

            if (HalfResTransparency)
                a.HalfResTransparencyScale = PerformancePolicy.ComputeHalfResTransparencyScale(v.HalfResTransparencyScale, p.HalfResTransparencyCap, true);

            if (OptimizeShadows)
            {
                a.ExcludePareticleSystemShadowCasters = true;
                a.MainLightShadowMapResolution = (MoonRenderPipelineAsset.ShadowResolution)PerformancePolicy.CapShadowResolution(
                    (int)v.MainLightShadowMapRes, p.MainLightShadowMaxRes);
                a.ShadowMapResolution = (MoonRenderPipelineAsset.TexResolution)PerformancePolicy.CapShadowResolution(
                    (int)v.OtherLightShadowMapRes, p.OtherLightShadowMaxRes);
                a.ShadowCascadeCount = PerformancePolicy.CapCascadeCount(v.ShadowCascadeCount, p.ShadowCascadeMaxCount);
                a.ShadowCasterMinArea = PerformancePolicy.RaiseMinArea(v.ShadowCasterMinArea, p.ShadowCasterMinAreaMin);
                a.PCSSSearchNumSamples = Math.Min(v.PcssSearchSamples, p.PcssSearchSamples);
                a.PCSSMinNumSamples = Math.Min(v.PcssMinSamples, p.PcssMinSamples);
                a.PCSSMaxNumSamples = Math.Min(v.PcssMaxSamples, p.PcssMaxSamples);
            }

            if (OptimizeVolumetrics)
            {
                a.VolumetricUseLowResDepth = true;
                a.VolumetricSamples = Math.Min(v.VolumetricSamples, p.VolumetricSamples);
            }

            if (OptimizeSsr)
                a.SSRMaxSteps = Math.Min(v.SsrMaxSteps, p.SsrMaxSteps);

            if (ExpSsrHiZ)
                a.SSRUseHiZ = true;

            if (OptimizeReflections)
            {
                a.ReflPlaneTexScaling = Math.Min(v.ReflPlaneTexScaling, p.ReflPlaneScalingCap);
                a.MaxNumReflPlaneTextures = Math.Min(v.MaxNumReflPlaneTextures, p.MaxReflPlanesCap);
            }

            if (OptimizeCullingCloth)
            {
                a.SmallObjectsCullingRelativeScreenHeight = Math.Max(v.SmallObjectsMinScreenHeight, p.SmallObjectsMinScreenHeight);
                a.SmallObjectsCullingMinDistance = Math.Min(v.SmallObjectsMinDistance, p.SmallObjectsMaxMinDist);
                if (MoonClothUpdate.EnableDistanceCulling)
                    MoonClothUpdate.DistanceCullingThreshold = Math.Min(v.ClothDistanceCullingThreshold, p.ClothDistanceCulling);
            }

            if (ExpDisableAssetBlending)
                a.AssetBlendingPassEnabled = false;
        }

        private static void AfterFlashLightGetDepthMipIndex(MoonRenderPipelineView __instance, int __0, ref int __result)
        {
            if (!Enabled || ActivePreset == PerfPreset.Vanilla || !HalfResFlashLight || __instance == null) return;
            var asset = MoonRenderPipeline.currentAsset;
            if (asset == null) return;
            var profile = PerformancePolicy.GetProfile(ActivePreset);
            __result = PerformancePolicy.ComputeFlashLightDepthMip(
                __result,
                __0,
                __instance.LastCameraPixelHeight,
                profile.FlashLightMinHeight,
                asset.FlashLightResDivider,
                __instance.DepthHierarchyMipCount,
                forceHalfResEnabled: true);
        }

        private static void AfterHalfResTransparencyScale(ref float __result)
        {
            if (!Enabled || ActivePreset == PerfPreset.Vanilla || !HalfResTransparency) return;
            var profile = PerformancePolicy.GetProfile(ActivePreset);
            __result = PerformancePolicy.ComputeHalfResTransparencyScale(__result, profile.HalfResTransparencyCap, true);
        }

        private static void AfterShouldRunCleanup(
            UberGCLogic.CleanupType __0,
            UberGCLogic.CleanupTrigger __1,
            ref bool __result)
        {
            if (!__result || !Enabled || ActivePreset == PerfPreset.Vanilla || !BlockActiveUnloads) return;
            long freeRamMb = GetAvailableRamMb();
            bool block = PerformancePolicy.ShouldBlockCleanup((int)__0, (int)__1, freeRamMb, MinFreeRamMb, BlockActiveUnloads);
            if (block)
            {
                __result = false;
                _blockedUnloads++;
            }
            long checkIdx = ++_cleanupChecksLogged;
            if (checkIdx <= 15 || (block && (_blockedUnloads % 25) == 0))
                Log($"cleanup check #{checkIdx}: decision={(block ? "BLOCK" : "ALLOW")} type={__0} trigger={__1} freeRamMb={freeRamMb} minRam={MinFreeRamMb} blockedTotal={_blockedUnloads}");
        }

        private static bool BeforeLadderKickDown(Frame __0, EntityRef __1, ref bool __result)
        {
            if (!Enabled || ActivePreset == PerfPreset.Vanilla || !GuardLadderKickDown) return true;
            if (__0 == null || !__1.IsValid) return true;
            try
            {
                if (!_physicsCompLookupTried)
                {
                    _physicsCompLookupTried = true;
                    _physicsCompIndex = ComponentTypeId.GetComponentIndex("PhysicsComponent");
                    if (_physicsCompIndex < 0)
                        _physicsCompIndex = ComponentTypeId.GetComponentIndex(Il2CppInterop.Runtime.Il2CppType.Of<PhysicsComponent>());
                    Log($"LadderKickDown guard PhysicsComponent index={_physicsCompIndex}");
                }
                if (_physicsCompIndex >= 0 && !__0.Has(__1, _physicsCompIndex))
                {
                    __result = false;
                    long count = ++_ladderGuards;
                    if (count <= 10 || (count % 25) == 0)
                        Log($"guarded LadderData.KickDown missing PhysicsComponent entity={__1.Raw} count={count}");
                    return false;
                }
            }
            catch (Exception e)
            {
                if (++_ladderErrors <= 3)
                    Log($"LadderKickDown guard error: {e.GetType().Name}: {e.Message}");
            }
            return true;
        }

        // --- Zero-alloc CPU Stage Profiler Hooks ---
        private static bool IsGameView(MoonRenderPipelineView v)
            => CpuProfilerEnabled && v != null && v.ViewType == MoonRenderPipelineView.Type.Game;

        private static void BeforeRenderTotal(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterRenderTotal(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.RenderTotal, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeCull(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterCull(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.Cull, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeShadowCull(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterShadowCull(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.ShadowCull, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeGBufferP1(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterGBufferP1(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.GBufferP1, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeCsmL2(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterCsmL2(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.CsmL2, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeOtherLightsL3(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterOtherLightsL3(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.OtherLightsL3, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeLightFwdP2(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterLightFwdP2(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.LightFwdP2, __state, Stopwatch.GetTimestamp()); }

        private static void BeforePostFx(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterPostFx(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.PostFx, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeDrawUi(MoonRenderPipelineView __instance, out long __state)
            => __state = IsGameView(__instance) ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterDrawUi(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.Ui, __state, Stopwatch.GetTimestamp()); }

        private static void BeforeCloth(out long __state)
            => __state = CpuProfilerEnabled ? Stopwatch.GetTimestamp() : 0L;
        private static void AfterCloth(long __state)
        { if (__state != 0L) CpuStages.AddElapsed(CpuStageId.Cloth, __state, Stopwatch.GetTimestamp()); }

        private static long GetAvailableRamMb()
        {
            double now = 0.0;
            try { now = Time.realtimeSinceStartupAsDouble; } catch { }
            if (_cachedFreeRamMb >= 0 && now > 0.0 && (now - _lastRamCheckTime) < 2.0)
                return _cachedFreeRamMb;

            _lastRamCheckTime = now;
            try
            {
                var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref mem))
                {
                    _cachedFreeRamMb = (long)(mem.ullAvailPhys / (1024UL * 1024UL));
                    return _cachedFreeRamMb;
                }
            }
            catch { }
            return _cachedFreeRamMb;
        }

        internal static string FormatSummaryLine(string tag)
        {
            var m = FrameRing.Snapshot();
            var asset = MoonRenderPipeline.currentAsset;
            var view = MoonRenderPipeline.currentPipeline?.gameView ?? MoonRenderPipeline.currentPipeline?.currentView;
            int w = view != null ? view.LastCameraPixelWidth : 0;
            int h = view != null ? view.LastCameraPixelHeight : 0;
            float scale = view != null ? view.m_CurrentResolutionScaling : 0f;
            string qPreset = asset != null ? asset.SelectedQualityPreset.ToString() : "n/a";
            return $"{tag}: preset={(Enabled ? ActivePreset.ToString() : "Disabled")} gamePreset={qPreset} fps={m.AvgFps:0.0}(1%low={m.OnePercentLowFps:0.0}) srpFps={MoonRenderPipeline.FPS} gpuAvg={MoonRenderPipeline.frameTimeGPUAvg:0.00}ms gpuMax={MoonRenderPipeline.frameTimeGPUMax:0.00}ms cpuNoPresent={MoonRenderPipeline.frameTimeCPUWithoutPresentWaitAvg:0.00}ms render={w}x{h}({scale:0.00}) workersMax={MoonRenderPipeline.m_MaxWorkerThreadCnt} blockedUnloads={_blockedUnloads} ladderGuards={_ladderGuards} freeRamMb={GetAvailableRamMb()}";
        }

        internal static string FormatCpuStagesLine()
        {
            if (!CpuProfilerEnabled) return "off";
            var sb = new StringBuilder();
            for (int i = 0; i < CpuStageId.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(CpuStageId.Names[i]).Append('=').Append(CpuStages.GetEmaMs(i).ToString("0.00")).Append("ms");
            }
            return sb.ToString();
        }

        internal static void DumpDetailedSnapshot(string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine(FormatSummaryLine(tag));
            var asset = MoonRenderPipeline.currentAsset;
            if (asset != null)
            {
                sb.AppendLine($"  SRP: LimitWorkers={asset.LimitWorkerThreadCnt} WorkerCnt={asset.WorkerThreadCnt} AO={asset.iMXAOShadingRate}/{asset.iMXAOGlobalSampleQualityPreset} FlashLight=div{asset.FlashLightResDivider}/min{asset.FlashLightResDividerMinRes}/steps{asset.FlashLightStepsMax} HalfTrans={asset.HalfResTransparencyEnabled}/{asset.HalfResTransparencyScale:0.00}");
                sb.AppendLine($"  Shadows: ExclParticles={asset.ExcludePareticleSystemShadowCasters} MainRes={asset.MainLightShadowMapResolution} OtherRes={asset.ShadowMapResolution} Cascades={asset.ShadowCascadeCount} MinArea={asset.ShadowCasterMinArea:0.0000} PCSS={asset.PCSSSearchNumSamples}/{asset.PCSSMinNumSamples}/{asset.PCSSMaxNumSamples}");
                sb.AppendLine($"  Effects: VolLowRes={asset.VolumetricUseLowResDepth}/samples={asset.VolumetricSamples} SSR={asset.SSRMaxSteps}/HiZ={asset.SSRUseHiZ} Refl={asset.ReflPlaneTexScaling:0.00}x{asset.MaxNumReflPlaneTextures} SmallCull={asset.SmallObjectsCullingRelativeScreenHeight:0.000}@{asset.SmallObjectsCullingMinDistance:0.0}m ClothDist={MoonClothUpdate.DistanceCullingThreshold:0.0}m AssetBlend={asset.AssetBlendingPassEnabled}");
            }
            sb.AppendLine($"  CPUStages: {FormatCpuStagesLine()}");
            Log(sb.ToString().TrimEnd());
            FlushLogs();
        }

        private static void Log(string line)
        {
            string stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
            PendingLogs.Enqueue(stamped);
            MelonLogger.Msg(line);
        }

        private static void FlushLogs()
        {
            if (string.IsNullOrEmpty(_logPath) || PendingLogs.IsEmpty) return;
            try
            {
                using var sw = new StreamWriter(_logPath, append: true, Encoding.UTF8);
                while (PendingLogs.TryDequeue(out var line))
                    sw.WriteLine(line);
            }
            catch { }
        }
    }
}
