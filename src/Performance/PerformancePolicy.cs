using System;
using System.Diagnostics;

namespace Performance
{
    public enum PerfPreset
    {
        Vanilla = 0,
        UltraSmart = 1,
        BalancedPro = 2,
        MaxFps = 3,
    }

    public readonly struct HookSpec
    {
        public string Type { get; }
        public string Method { get; }
        public string Return { get; }
        public string[] Args { get; }

        public HookSpec(string type, string method, string ret, params string[] args)
        {
            Type = type;
            Method = method;
            Return = ret;
            Args = args;
        }
    }

    public readonly struct PresetProfile
    {
        public PerfPreset Preset { get; init; }
        public int FlashLightStepsMax { get; init; }
        public int FlashLightMinHeight { get; init; }
        public int FlashLightDivider { get; init; }
        public float HalfResTransparencyCap { get; init; }
        public int MainLightShadowMaxRes { get; init; }
        public int OtherLightShadowMaxRes { get; init; }
        public int ShadowCascadeMaxCount { get; init; }
        public float ShadowCasterMinAreaMin { get; init; }
        public int PcssSearchSamples { get; init; }
        public int PcssMinSamples { get; init; }
        public int PcssMaxSamples { get; init; }
        public int VolumetricSamples { get; init; }
        public int SsrMaxSteps { get; init; }
        public float ReflPlaneScalingCap { get; init; }
        public int MaxReflPlanesCap { get; init; }
        public float SmallObjectsMinScreenHeight { get; init; }
        public float SmallObjectsMaxMinDist { get; init; }
        public float ClothDistanceCulling { get; init; }
    }

    /// <summary>
    /// Pure policy, preset tables, hook contracts, and math/telemetry helpers for NRFWPerformance.
    /// Zero Unity or Il2CppInterop dependencies so Level 1 tests run anywhere.
    /// </summary>
    public static class PerformancePolicy
    {
        public const string Version = "0.2.0";

        // UberGCLogic.CleanupType
        public const int CleanupGc = 0;
        public const int CleanupProactive = 1;
        public const int CleanupPanic = 2;

        // UberGCLogic.CleanupTrigger
        public const int TriggerFader = 0;
        public const int TriggerIdleGameplay = 1;
        public const int TriggerActiveGameplay = 2;
        public const int TriggerTeleporter = 3;

        // MoonRenderPipelineAsset.LimitWorkers
        public const int LimitWorkersNever = 0;

        // MoonRenderPipelineAsset.IMXAOShadingRate
        public const int AoRateFull = 0;
        public const int AoRateHalf = 1;

        public static readonly HookSpec[] Hooks =
        {
            new HookSpec("Il2Cpp.MoonRenderPipelineAsset", "UpdateQualityPreset", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "FlashLightGetDepthMipIndex", "System.Int32", "System.Int32"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "HalfResTransparencyScale", "System.Single"),
            new HookSpec(
                "Il2CppUberShader.optimizations.gc.UberGCLogic",
                "ShouldRunCleanup",
                "System.Boolean",
                "Il2CppUberShader.optimizations.gc.UberGCLogic+CleanupType",
                "Il2CppUberShader.optimizations.gc.UberGCLogic+CleanupTrigger",
                "Il2CppUberShader.optimizations.gc.UberGCLogic+CleanupOutsideOfGameplayTrigger"),
            new HookSpec("Il2CppQuantum.LadderData", "KickDown", "System.Boolean", "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef"),
            // CPU stage profiler hooks (no ref-struct parameters)
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "Render", "Void", "UnityEngine.Rendering.ScriptableRenderContext", "UnityEngine.Camera", "UnityEngine.Camera"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "Cull", "System.Boolean"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "CullShadowCasters", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "DrawVisibleGeometryPart1", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "UpdateLightsPart2", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "UpdateLightsPart3", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "DrawVisibleGeometryPart2", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "PostProcessing", "Void"),
            new HookSpec("Il2Cpp.MoonRenderPipelineView", "DrawUI", "System.Boolean"),
            new HookSpec("Il2CppMoon.AnimationControl.MoonClothUpdate", "TickLate", "Void", "Il2CppMoon.TheLoop.ViewFrame"),
        };

        private static readonly PresetProfile UltraSmartProfile = new()
        {
            Preset = PerfPreset.UltraSmart,
            FlashLightStepsMax = 10,
            FlashLightMinHeight = 1080,
            FlashLightDivider = 2,
            HalfResTransparencyCap = 0.60f,
            MainLightShadowMaxRes = 2048,
            OtherLightShadowMaxRes = 256,
            ShadowCascadeMaxCount = 3,
            ShadowCasterMinAreaMin = 0.0025f,
            PcssSearchSamples = 6,
            PcssMinSamples = 3,
            PcssMaxSamples = 6,
            VolumetricSamples = 12,
            SsrMaxSteps = 20,
            ReflPlaneScalingCap = 0.35f,
            MaxReflPlanesCap = 1,
            SmallObjectsMinScreenHeight = 0.028f,
            SmallObjectsMaxMinDist = 35f,
            ClothDistanceCulling = 24f,
        };

        private static readonly PresetProfile BalancedProProfile = new()
        {
            Preset = PerfPreset.BalancedPro,
            FlashLightStepsMax = 8,
            FlashLightMinHeight = 1080,
            FlashLightDivider = 2,
            HalfResTransparencyCap = 0.50f,
            MainLightShadowMaxRes = 2048,
            OtherLightShadowMaxRes = 256,
            ShadowCascadeMaxCount = 3,
            ShadowCasterMinAreaMin = 0.0040f,
            PcssSearchSamples = 5,
            PcssMinSamples = 2,
            PcssMaxSamples = 5,
            VolumetricSamples = 10,
            SsrMaxSteps = 16,
            ReflPlaneScalingCap = 0.30f,
            MaxReflPlanesCap = 1,
            SmallObjectsMinScreenHeight = 0.035f,
            SmallObjectsMaxMinDist = 30f,
            ClothDistanceCulling = 20f,
        };

        private static readonly PresetProfile MaxFpsProfile = new()
        {
            Preset = PerfPreset.MaxFps,
            FlashLightStepsMax = 6,
            FlashLightMinHeight = 900,
            FlashLightDivider = 2,
            HalfResTransparencyCap = 0.50f,
            MainLightShadowMaxRes = 2048,
            OtherLightShadowMaxRes = 256,
            ShadowCascadeMaxCount = 2,
            ShadowCasterMinAreaMin = 0.0065f,
            PcssSearchSamples = 4,
            PcssMinSamples = 2,
            PcssMaxSamples = 4,
            VolumetricSamples = 8,
            SsrMaxSteps = 12,
            ReflPlaneScalingCap = 0.25f,
            MaxReflPlanesCap = 0,
            SmallObjectsMinScreenHeight = 0.045f,
            SmallObjectsMaxMinDist = 24f,
            ClothDistanceCulling = 16f,
        };

        public static PresetProfile GetProfile(PerfPreset preset) => preset switch
        {
            PerfPreset.BalancedPro => BalancedProProfile,
            PerfPreset.MaxFps => MaxFpsProfile,
            _ => UltraSmartProfile,
        };

        public static PerfPreset ParsePreset(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return PerfPreset.UltraSmart;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "vanilla": return PerfPreset.Vanilla;
                case "ultrasmart":
                case "ultra_smart":
                case "ultra-smart": return PerfPreset.UltraSmart;
                case "balancedpro":
                case "balanced_pro":
                case "balanced-pro":
                case "balanced": return PerfPreset.BalancedPro;
                case "maxfps":
                case "max_fps":
                case "max-fps": return PerfPreset.MaxFps;
                default: return PerfPreset.UltraSmart;
            }
        }

        /// <summary>
        /// Only recapture the vanilla SRP snapshot on initialization, asset pointer change, or
        /// when UpdateQualityPreset is actually about to apply a new preset (Selected != Current).
        /// Prevents per-frame UpdateQualityPreset calls from overwriting vanilla with mod values.
        /// </summary>
        public static bool ShouldRecaptureVanillaSnapshot(
            int selectedPreset,
            int currentPreset,
            bool hasSnapshot,
            bool assetPointerChanged)
        {
            if (!hasSnapshot || assetPointerChanged) return true;
            return selectedPreset != currentPreset;
        }

        public static int ComputeFlashLightDepthMip(
            int baseMip,
            int customOffset,
            int pixelHeight,
            int minRes,
            int resDivider,
            int depthMipCount,
            bool forceHalfResEnabled)
        {
            if (depthMipCount <= 1) return 0;
            int maxMip = depthMipCount - 1;
            if (!forceHalfResEnabled || resDivider <= 1 || pixelHeight < minRes)
                return Clamp(baseMip, 0, maxMip);

            int divOffset = Math.Max(1, resDivider - 1);
            int target = Math.Max(0, customOffset) + divOffset;
            return Clamp(target, 0, maxMip);
        }

        public static float ComputeHalfResTransparencyScale(float vanillaScale, float capScale, bool enabled)
        {
            if (float.IsNaN(vanillaScale) || float.IsInfinity(vanillaScale) || vanillaScale <= 0f)
                vanillaScale = 1.0f;
            if (!enabled) return ClampFloat(vanillaScale, 0.25f, 1.0f);
            float clampedCap = ClampFloat(capScale, 0.35f, 1.0f);
            return ClampFloat(Math.Min(vanillaScale, clampedCap), 0.35f, 1.0f);
        }

        public static bool ShouldBlockCleanup(
            int cleanupType,
            int cleanupTrigger,
            long availableRamMb,
            long minFreeRamMb,
            bool blockActiveUnloads)
        {
            if (!blockActiveUnloads) return false;
            if (cleanupType != CleanupProactive && cleanupType != CleanupPanic) return false;
            if (cleanupTrigger != TriggerActiveGameplay && cleanupTrigger != TriggerIdleGameplay) return false;
            if (minFreeRamMb < 512) minFreeRamMb = 512;
            if (availableRamMb <= 0 || availableRamMb < minFreeRamMb) return false;
            return true;
        }

        public static int CapShadowResolution(int current, int maxAllowed)
        {
            if (current <= 0 || maxAllowed <= 0) return current;
            return current > maxAllowed ? maxAllowed : current;
        }

        public static int CapCascadeCount(int current, int maxAllowed)
        {
            int clampedMax = Clamp(maxAllowed, 2, 4);
            if (current <= 0) return clampedMax;
            int clampedCur = Clamp(current, 1, 4);
            return clampedCur > clampedMax ? clampedMax : clampedCur;
        }

        public static float RaiseMinArea(float current, float floor)
        {
            if (float.IsNaN(current) || float.IsInfinity(current) || current < 0f) current = 0f;
            float clampedFloor = ClampFloat(floor, 0f, 0.05f);
            return current < clampedFloor ? clampedFloor : current;
        }

        public static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        public static float ClampFloat(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);
    }

    public static class CpuStageId
    {
        public const int RenderTotal = 0;
        public const int Cull = 1;
        public const int ShadowCull = 2;
        public const int GBufferP1 = 3;
        public const int CsmL2 = 4;
        public const int OtherLightsL3 = 5;
        public const int LightFwdP2 = 6;
        public const int PostFx = 7;
        public const int Ui = 8;
        public const int Cloth = 9;
        public const int Count = 10;

        public static readonly string[] Names =
        {
            "RenderTotal", "Cull", "ShadowCull", "GBuffer(P1)", "CSM(L2)",
            "OtherLights(L3)", "Light+Fwd(P2)", "PostFX", "UI", "Cloth",
        };
    }

    /// <summary>
    /// Zero-allocation per-stage CPU tick accumulator with exponential moving average smoothing.
    /// </summary>
    public sealed class CpuStageTracker
    {
        private readonly long[] _frameTicks = new long[CpuStageId.Count];
        private readonly float[] _emaMs = new float[CpuStageId.Count];
        private readonly float _alpha;
        private readonly double _ticksToMs;

        public CpuStageTracker(float alpha = 0.08f, long ticksPerSecond = 0)
        {
            _alpha = PerformancePolicy.ClampFloat(alpha, 0.01f, 1.0f);
            long freq = ticksPerSecond > 0 ? ticksPerSecond : Stopwatch.Frequency;
            _ticksToMs = 1000.0 / Math.Max(1L, freq);
        }

        public void AddElapsed(int stageIndex, long startTicks, long endTicks)
        {
            if ((uint)stageIndex >= CpuStageId.Count) return;
            long delta = endTicks - startTicks;
            if (delta > 0) _frameTicks[stageIndex] += delta;
        }

        public void EndFrame()
        {
            for (int i = 0; i < CpuStageId.Count; i++)
            {
                float ms = (float)(_frameTicks[i] * _ticksToMs);
                _frameTicks[i] = 0;
                _emaMs[i] = _emaMs[i] == 0f ? ms : (_emaMs[i] * (1f - _alpha) + ms * _alpha);
            }
        }

        public void Reset()
        {
            Array.Clear(_frameTicks, 0, _frameTicks.Length);
            Array.Clear(_emaMs, 0, _emaMs.Length);
        }

        public float GetEmaMs(int stageIndex)
            => (uint)stageIndex < CpuStageId.Count ? _emaMs[stageIndex] : 0f;
    }

    public sealed class FrameStatsRing
    {
        private readonly float[] _samples;
        private readonly float[] _scratch;
        private int _head;
        private int _count;

        public FrameStatsRing(int capacity = 240)
        {
            if (capacity < 16) capacity = 16;
            _samples = new float[capacity];
            _scratch = new float[capacity];
        }

        public int Count => _count;

        public void Push(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0.0001f || deltaSeconds > 2.0f)
                return;
            _samples[_head] = deltaSeconds * 1000f;
            _head = (_head + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public FrameMetrics Snapshot()
        {
            if (_count == 0) return default;
            double sum = 0.0;
            float maxMs = 0f;
            for (int i = 0; i < _count; i++)
            {
                float ms = _samples[i];
                _scratch[i] = ms;
                sum += ms;
                if (ms > maxMs) maxMs = ms;
            }
            Array.Sort(_scratch, 0, _count);
            double avgMs = sum / _count;
            double avgFps = avgMs > 0.0 ? 1000.0 / avgMs : 0.0;

            int slowCount = Math.Max(1, _count / 100);
            double slowSum = 0.0;
            for (int i = _count - slowCount; i < _count; i++)
                slowSum += _scratch[i];
            double onePctMs = slowSum / slowCount;
            double onePctFps = onePctMs > 0.0 ? 1000.0 / onePctMs : 0.0;
            int p99Index = Math.Min(_count - 1, (int)Math.Floor((_count - 1) * 0.99));
            return new FrameMetrics
            {
                SampleCount = _count,
                AvgMs = (float)avgMs,
                AvgFps = (float)avgFps,
                OnePercentLowFps = (float)onePctFps,
                P99Ms = _scratch[p99Index],
                MaxMs = maxMs,
            };
        }
    }

    public struct FrameMetrics
    {
        public int SampleCount;
        public float AvgMs;
        public float AvgFps;
        public float OnePercentLowFps;
        public float P99Ms;
        public float MaxMs;
    }
}
