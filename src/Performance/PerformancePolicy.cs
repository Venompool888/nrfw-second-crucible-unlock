using System;

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
        public const string Version = "0.1.0";

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
            PcssSearchSamples = 6,
            PcssMinSamples = 3,
            PcssMaxSamples = 6,
            VolumetricSamples = 12,
            SsrMaxSteps = 20,
            ReflPlaneScalingCap = 0.40f,
            MaxReflPlanesCap = 2,
            SmallObjectsMinScreenHeight = 0.022f,
            SmallObjectsMaxMinDist = 45f,
            ClothDistanceCulling = 28f,
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
            PcssSearchSamples = 5,
            PcssMinSamples = 2,
            PcssMaxSamples = 5,
            VolumetricSamples = 10,
            SsrMaxSteps = 16,
            ReflPlaneScalingCap = 0.35f,
            MaxReflPlanesCap = 2,
            SmallObjectsMinScreenHeight = 0.026f,
            SmallObjectsMaxMinDist = 38f,
            ClothDistanceCulling = 24f,
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
            PcssSearchSamples = 4,
            PcssMinSamples = 2,
            PcssMaxSamples = 4,
            VolumetricSamples = 8,
            SsrMaxSteps = 12,
            ReflPlaneScalingCap = 0.25f,
            MaxReflPlanesCap = 1,
            SmallObjectsMinScreenHeight = 0.030f,
            SmallObjectsMaxMinDist = 30f,
            ClothDistanceCulling = 20f,
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
        /// Computes a clamped depth-hierarchy mip index for FlashLight raymarching, bypassing
        /// the vanilla FSR/DLSS lock to mip 0 whenever internal render height meets minRes.
        /// </summary>
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

            // resDivider=2 -> +1 mip; resDivider=4 -> +2 mip, etc., matching vanilla FlashLightGetDepthMipIndex.
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

        /// <summary>
        /// Blocks Proactive/Panic UnloadUnusedAssets stutters during active/idle gameplay while
        /// preserving Gc, Fader, Teleporter cleanups, and falling back to allow if free RAM is low/unknown.
        /// </summary>
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

        /// <summary>
        /// Caps a resolution enum integer without ever upgrading a lower preset's value.
        /// </summary>
        public static int CapShadowResolution(int current, int maxAllowed)
        {
            if (current <= 0 || maxAllowed <= 0) return current;
            return current > maxAllowed ? maxAllowed : current;
        }

        public static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        public static float ClampFloat(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// Fixed-capacity frame-time ring buffer for zero-allocation FPS / 1% Low / P99 telemetry.
    /// </summary>
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

            // 1% low = average of the slowest 1% of frames (at least 1 frame), matching standard frametime benchmarks.
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
