using System;
using Performance;

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS: " : "FAIL: ") + what);
    if (!ok) failures++;
}

// 1. ParsePreset: valid + malformed / null inputs
Check(PerformancePolicy.ParsePreset("Vanilla") == PerfPreset.Vanilla, "parse vanilla");
Check(PerformancePolicy.ParsePreset("  ultrasmart ") == PerfPreset.UltraSmart, "parse ultrasmart");
Check(PerformancePolicy.ParsePreset("ultra-smart") == PerfPreset.UltraSmart, "parse ultra-smart");
Check(PerformancePolicy.ParsePreset("BalancedPro") == PerfPreset.BalancedPro, "parse balancedpro");
Check(PerformancePolicy.ParsePreset("balanced") == PerfPreset.BalancedPro, "parse balanced alias");
Check(PerformancePolicy.ParsePreset("max_fps") == PerfPreset.MaxFps, "parse max_fps");
Check(PerformancePolicy.ParsePreset(null) == PerfPreset.UltraSmart, "parse null falls back to UltraSmart");
Check(PerformancePolicy.ParsePreset("bogus_value") == PerfPreset.UltraSmart, "parse unknown falls back to UltraSmart");

// 2. Preset profiles sanity (UltraSmart >= BalancedPro >= MaxFps quality ordering)
var ultra = PerformancePolicy.GetProfile(PerfPreset.UltraSmart);
var bal = PerformancePolicy.GetProfile(PerfPreset.BalancedPro);
var max = PerformancePolicy.GetProfile(PerfPreset.MaxFps);
Check(ultra.FlashLightStepsMax >= bal.FlashLightStepsMax && bal.FlashLightStepsMax >= max.FlashLightStepsMax, "flashlight steps monotonic");
Check(ultra.VolumetricSamples >= bal.VolumetricSamples && bal.VolumetricSamples >= max.VolumetricSamples, "volumetric samples monotonic");
Check(ultra.SsrMaxSteps >= bal.SsrMaxSteps && bal.SsrMaxSteps >= max.SsrMaxSteps, "SSR steps monotonic");
Check(ultra.SmallObjectsMinScreenHeight <= bal.SmallObjectsMinScreenHeight && bal.SmallObjectsMinScreenHeight <= max.SmallObjectsMinScreenHeight, "cull threshold monotonic");
Check(ultra.HalfResTransparencyCap >= 0.50f && ultra.HalfResTransparencyCap <= 0.75f, "UltraSmart half-res transparency cap in sweet spot");

// 3. FlashLight depth mip computation (positive + negative/edge inputs)
Check(PerformancePolicy.ComputeFlashLightDepthMip(0, 0, 1440, 1080, 2, 10, true) == 1, "1440p FSR unlocks mip 1");
Check(PerformancePolicy.ComputeFlashLightDepthMip(0, 1, 2160, 1080, 2, 10, true) == 2, "2160p with customOffset=1 yields mip 2");
Check(PerformancePolicy.ComputeFlashLightDepthMip(0, 0, 720, 1080, 2, 10, true) == 0, "720p below minRes stays at baseMip");
Check(PerformancePolicy.ComputeFlashLightDepthMip(1, 0, 1440, 1080, 2, 10, false) == 1, "disabled keeps baseMip");
Check(PerformancePolicy.ComputeFlashLightDepthMip(0, 99, 1440, 1080, 2, 5, true) == 4, "clamped to depthMipCount - 1");
Check(PerformancePolicy.ComputeFlashLightDepthMip(3, -5, 1440, 1080, 2, 1, true) == 0, "depthMipCount <= 1 returns 0");
Check(PerformancePolicy.ComputeFlashLightDepthMip(0, 0, 1440, 1080, 1, 8, true) == 0, "resDivider <= 1 keeps baseMip");

// 4. HalfResTransparencyScale capping (never upgrades, clamps bad inputs)
Check(Math.Abs(PerformancePolicy.ComputeHalfResTransparencyScale(1.0f, 0.60f, true) - 0.60f) < 1e-5f, "caps 1.0 -> 0.60");
Check(Math.Abs(PerformancePolicy.ComputeHalfResTransparencyScale(0.50f, 0.60f, true) - 0.50f) < 1e-5f, "preserves lower vanilla 0.50");
Check(Math.Abs(PerformancePolicy.ComputeHalfResTransparencyScale(1.0f, 0.10f, true) - 0.35f) < 1e-5f, "floor clamps excessive downscale to 0.35");
Check(Math.Abs(PerformancePolicy.ComputeHalfResTransparencyScale(float.NaN, 0.60f, true) - 0.60f) < 1e-5f, "NaN vanilla safely treated as 1.0");
Check(Math.Abs(PerformancePolicy.ComputeHalfResTransparencyScale(0.85f, 0.50f, false) - 0.85f) < 1e-5f, "disabled preserves vanilla 0.85");

// 5. ShouldBlockCleanup full CleanupType x CleanupTrigger + low-RAM safety matrix
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupGc, PerformancePolicy.TriggerActiveGameplay, 16000, 4096, true), "never block GC");
Check(PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupProactive, PerformancePolicy.TriggerActiveGameplay, 16000, 4096, true), "block Proactive in ActiveGameplay when RAM >= 4GB");
Check(PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupPanic, PerformancePolicy.TriggerActiveGameplay, 16000, 4096, true), "block Panic in ActiveGameplay when RAM >= 4GB");
Check(PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupProactive, PerformancePolicy.TriggerIdleGameplay, 8192, 4096, true), "block Proactive in IdleGameplay");
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupProactive, PerformancePolicy.TriggerFader, 16000, 4096, true), "never block Fader unload");
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupPanic, PerformancePolicy.TriggerTeleporter, 16000, 4096, true), "never block Teleporter unload");
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupPanic, PerformancePolicy.TriggerActiveGameplay, 2048, 4096, true), "allow unload when free RAM below threshold");
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupPanic, PerformancePolicy.TriggerActiveGameplay, 0, 4096, true), "allow unload when RAM probe is 0/unknown");
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupPanic, PerformancePolicy.TriggerActiveGameplay, -1, 4096, true), "allow unload when RAM probe is negative");
Check(!PerformancePolicy.ShouldBlockCleanup(PerformancePolicy.CleanupPanic, PerformancePolicy.TriggerActiveGameplay, 16000, 4096, false), "master toggle off never blocks");

// 6. CapShadowResolution (never upgrades)
Check(PerformancePolicy.CapShadowResolution(4096, 2048) == 2048, "4096 capped to 2048");
Check(PerformancePolicy.CapShadowResolution(2048, 2048) == 2048, "2048 unchanged");
Check(PerformancePolicy.CapShadowResolution(1024, 2048) == 1024, "1024 never upgraded to 2048");
Check(PerformancePolicy.CapShadowResolution(256, 512) == 256, "256 never upgraded to 512");

// 7. FrameStatsRing calculations & bad-sample rejection
var ring = new FrameStatsRing(100);
ring.Push(0f);
ring.Push(-0.01f);
ring.Push(float.NaN);
ring.Push(5.0f);
Check(ring.Count == 0, "rejects zero, negative, NaN, and >2s stall samples");
for (int i = 0; i < 99; i++) ring.Push(0.010f); // 99 frames at 10ms (100 fps)
ring.Push(0.050f); // 1 frame at 50ms (20 fps)
var snap = ring.Snapshot();
Check(snap.SampleCount == 100, "ring records 100 valid samples");
Check(Math.Abs(snap.AvgMs - 10.4f) < 0.05f, "avg frame ms = 10.4ms");
Check(Math.Abs(snap.OnePercentLowFps - 20.0f) < 0.1f, "1% low FPS = 20.0");
Check(Math.Abs(snap.MaxMs - 50.0f) < 0.05f, "max ms = 50.0");

// 8. Hook table integrity
Check(PerformancePolicy.Hooks.Length == 5, "5 Harmony hooks declared");
foreach (var h in PerformancePolicy.Hooks)
    Check(!string.IsNullOrWhiteSpace(h.Type) && !string.IsNullOrWhiteSpace(h.Method) && !string.IsNullOrWhiteSpace(h.Return), $"hook spec well-formed: {h.Type}.{h.Method}");

if (failures > 0)
{
    Console.WriteLine($"{failures} check(s) failed");
    return 1;
}
Console.WriteLine("All PerformancePolicyTests passed.");
return 0;
