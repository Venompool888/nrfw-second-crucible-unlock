using System;
using Il2Cpp;
#if HAS_IMGUI
using UnityEngine;
#endif

namespace Performance
{
    public sealed partial class PerformanceMod
    {
#if HAS_IMGUI
        private static GUIStyle _boxStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _btnActiveStyle;

        private static bool TryInitHud() => true;

        public override void OnGUI()
        {
            if (!HudVisible) return;
            try
            {
                EnsureStyles();
                const float x = 14f, y = 14f, w = 404f, h = 556f;
                GUI.Box(new Rect(x, y, w, h), "", _boxStyle);

                float cy = y + 8f;
                GUI.Label(new Rect(x + 10f, cy, w - 20f, 20f), $"NRFW Performance v{PerformancePolicy.Version}  [F9 Hide | F10 A/B | F11 Dump]", _labelStyle);
                cy += 22f;

                var m = FrameRing.Snapshot();
                var asset = MoonRenderPipeline.currentAsset;
                var view = MoonRenderPipeline.currentPipeline?.gameView ?? MoonRenderPipeline.currentPipeline?.currentView;
                int rw = view != null ? view.LastCameraPixelWidth : 0;
                int rh = view != null ? view.LastCameraPixelHeight : 0;
                float rscale = view != null ? view.m_CurrentResolutionScaling : 0f;

                GUI.Label(new Rect(x + 10f, cy, w - 20f, 18f), $"FPS: {m.AvgFps:0.0}  |  1% Low: {m.OnePercentLowFps:0.0}  |  SRP FPS: {MoonRenderPipeline.FPS}", _labelStyle); cy += 18f;
                GUI.Label(new Rect(x + 10f, cy, w - 20f, 18f), $"GPU: {MoonRenderPipeline.frameTimeGPUAvg:0.00} ms (max {MoonRenderPipeline.frameTimeGPUMax:0.00}) | CPU(noWait): {MoonRenderPipeline.frameTimeCPUWithoutPresentWaitAvg:0.00} ms", _labelStyle); cy += 18f;
                GUI.Label(new Rect(x + 10f, cy, w - 20f, 18f), $"Render: {rw}x{rh} (scale {rscale:0.00})  |  Game: {(asset != null ? asset.SelectedQualityPreset.ToString() : "n/a")}", _labelStyle); cy += 18f;
                GUI.Label(new Rect(x + 10f, cy, w - 20f, 18f), $"Active: {(Enabled ? ActivePreset.ToString() : "Disabled")}  |  BlockedUnload: {_blockedUnloads}  |  LadderGuard: {_ladderGuards}", _labelStyle); cy += 24f;

                // Preset buttons
                float bw = 91f;
                DrawPresetBtn(new Rect(x + 10f, cy, bw, 22f), "Vanilla", PerfPreset.Vanilla);
                DrawPresetBtn(new Rect(x + 10f + (bw + 6f), cy, bw, 22f), "UltraSmart", PerfPreset.UltraSmart);
                DrawPresetBtn(new Rect(x + 10f + (bw + 6f) * 2, cy, bw, 22f), "BalancedPro", PerfPreset.BalancedPro);
                DrawPresetBtn(new Rect(x + 10f + (bw + 6f) * 3, cy, bw, 22f), "MaxFps", PerfPreset.MaxFps);
                cy += 28f;

                // Toggle grid (2 columns)
                float tw = 187f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "iMXAO Half-Res", HalfResAo)) { HalfResAo = !HalfResAo; ReapplyCurrentAsset(); }
                if (DrawToggleBtn(new Rect(x + 207f, cy, tw, 22f), "FlashLight Half-Res", HalfResFlashLight)) { HalfResFlashLight = !HalfResFlashLight; ReapplyCurrentAsset(); }
                cy += 25f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "Half-Res Transp", HalfResTransparency)) { HalfResTransparency = !HalfResTransparency; ReapplyCurrentAsset(); }
                if (DrawToggleBtn(new Rect(x + 207f, cy, tw, 22f), "Shadow / Casc / PCSS", OptimizeShadows)) { OptimizeShadows = !OptimizeShadows; ReapplyCurrentAsset(); }
                cy += 25f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "Volumetric LowRes", OptimizeVolumetrics)) { OptimizeVolumetrics = !OptimizeVolumetrics; ReapplyCurrentAsset(); }
                if (DrawToggleBtn(new Rect(x + 207f, cy, tw, 22f), "SSR Steps Cap", OptimizeSsr)) { OptimizeSsr = !OptimizeSsr; ReapplyCurrentAsset(); }
                cy += 25f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "Planar Refl Cap", OptimizeReflections)) { OptimizeReflections = !OptimizeReflections; ReapplyCurrentAsset(); }
                if (DrawToggleBtn(new Rect(x + 207f, cy, tw, 22f), "Cull / Cloth Trim", OptimizeCullingCloth)) { OptimizeCullingCloth = !OptimizeCullingCloth; ReapplyCurrentAsset(); }
                cy += 25f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "Unlock Workers", UnlockWorkers)) { UnlockWorkers = !UnlockWorkers; ReapplyCurrentAsset(); }
                if (DrawToggleBtn(new Rect(x + 207f, cy, tw, 22f), "Block Active Unload", BlockActiveUnloads)) { BlockActiveUnloads = !BlockActiveUnloads; }
                cy += 25f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "Exp: No AssetBlend", ExpDisableAssetBlending)) { ExpDisableAssetBlending = !ExpDisableAssetBlending; ReapplyCurrentAsset(); }
                if (DrawToggleBtn(new Rect(x + 207f, cy, tw, 22f), "Exp: SSR Hi-Z", ExpSsrHiZ)) { ExpSsrHiZ = !ExpSsrHiZ; ReapplyCurrentAsset(); }
                cy += 25f;
                if (DrawToggleBtn(new Rect(x + 10f, cy, tw, 22f), "CPU Stage Prof", CpuProfilerEnabled)) { CpuProfilerEnabled = !CpuProfilerEnabled; if (!CpuProfilerEnabled) CpuStages.Reset(); }
                if (GUI.Button(new Rect(x + 207f, cy, tw, 22f), "Save Config", _btnStyle)) SavePreferences();
                cy += 27f;

                if (GUI.Button(new Rect(x + 10f, cy, w - 20f, 22f), "Dump Detailed Snapshot to Log (F11)", _btnStyle))
                    DumpDetailedSnapshot("HUD");
                cy += 26f;

                // CPU Stage Breakdown (2 columns)
                GUI.Label(new Rect(x + 10f, cy, w - 20f, 18f), CpuProfilerEnabled ? "--- CPU Render Stages (ms/frame EMA) ---" : "--- CPU Stage Profiler OFF ---", _labelStyle);
                cy += 18f;
                if (CpuProfilerEnabled)
                {
                    for (int i = 0; i < CpuStageId.Count; i += 2)
                    {
                        string left = $"{CpuStageId.Names[i],-14}: {CpuStages.GetEmaMs(i),5:0.00} ms";
                        string right = i + 1 < CpuStageId.Count ? $"{CpuStageId.Names[i + 1],-14}: {CpuStages.GetEmaMs(i + 1),5:0.00} ms" : "";
                        GUI.Label(new Rect(x + 10f, cy, tw, 17f), left, _labelStyle);
                        if (right.Length > 0) GUI.Label(new Rect(x + 207f, cy, tw, 17f), right, _labelStyle);
                        cy += 17f;
                    }
                }
            }
            catch { }
        }

        private static void DrawPresetBtn(Rect r, string label, PerfPreset preset)
        {
            bool active = (Enabled ? ActivePreset : PerfPreset.Vanilla) == preset;
            if (GUI.Button(r, label, active ? _btnActiveStyle : _btnStyle))
                SelectPreset(preset);
        }

        private static bool DrawToggleBtn(Rect r, string label, bool on)
            => GUI.Button(r, (on ? "[ON] " : "[off] ") + label, on ? _btnActiveStyle : _btnStyle);

        private static void EnsureStyles()
        {
            if (_boxStyle != null) return;
            _boxStyle = new GUIStyle(GUI.skin.box);
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _labelStyle.normal.textColor = Color.white;
            _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            _btnActiveStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            _btnActiveStyle.normal.textColor = Color.green;
        }
#else
        private static bool TryInitHud() => false;
#endif
    }
}
