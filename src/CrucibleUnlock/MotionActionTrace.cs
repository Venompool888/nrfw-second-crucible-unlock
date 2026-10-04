using System;
using System.Collections.Generic;
using Il2CppMoon;
using Il2CppMoon.Forsaken;

namespace CrucibleUnlock
{
    public static class MotionActionTrace
    {
        private static int _consecutiveFailures;
        private static int _crossfadeFailures;
        private static bool _disabled;
        public static void Reset() { _consecutiveFailures = 0; _crossfadeFailures = 0; _disabled = false; }
        // Caller must validate the current CharacterView and call on the Unity main thread.
        // Old ActionView/Playback references are deliberately never cached or revisited.
        public static MotionActionSnapshot Read(CharacterView view)
        {
            var result = new MotionActionSnapshot { state = _disabled ? "disabled_after_read_faults" : "current_only" };
            if (_disabled) { result.active_clips = new MotionActiveClipSnapshot[0]; result.consecutive_failures = _consecutiveFailures; return result; }
            var clips = new List<MotionActiveClipSnapshot>();
            try
            {
                if (view == null) { result.state = "no_view"; return result; }
                var animator = view.CharacterAnimator;
                if (animator == null) { result.state = "no_animator"; return result; }
                var layers = animator.m_layers;
                if (layers == null) { result.state = "no_layers"; return result; }
                int layerCount = layers.Count;
                if (layerCount > 16) result.truncated = true;
                for (int li = 0; li < System.Math.Min(layerCount, 16); ++li)
                {
                    try
                    {
                        var layer = layers[li];
                        if (layer == null) continue;
                        var current = layer.m_currentActiveAnimation;
                        var active = layer.m_activeAnimations;
                        if (active == null) continue;
                        int count = active.Count;
                        result.observed_active_count += count;
                        if (count > 16) result.truncated = true;
                        for (int ai = 0; ai < System.Math.Min(count, 16); ++ai)
                        {
                            if (clips.Count >= 16) { result.truncated = true; break; }
                            var snapshot = new MotionActiveClipSnapshot { layer_index = li };
                            string stage = "active_entry";
                            try
                            {
                                var a = active[ai];
                                if (a == null) continue;
                                snapshot.active_identity = a.Pointer.ToInt64();
                                snapshot.current = current != null && current.Pointer == a.Pointer;
                                stage = "time";
                                snapshot.time = a.Time;
                                stage = "weight";
                                snapshot.weight = a.Weight;
                                stage = "playing";
                                snapshot.playing = a.IsPlaying;
                                stage = "stop_requested";
                                snapshot.stop_requested = a.StopRequested;
                                stage = "orphaned";
                                snapshot.orphaned = a.IsOrphaned;
                                // IL2CPP represents a boxed empty Nullable<float> as a null wrapper.
                                // This optional override must not suppress core animation observations.
                                snapshot.crossfade_state = _crossfadeFailures >= 3 ? "disabled_after_read_faults" : "not_read";
                                if (_crossfadeFailures < 3)
                                {
                                    try
                                    {
                                        var crossfade = a.CroffadeTimeOverride;
                                        if (crossfade == null || !crossfade.HasValue) snapshot.crossfade_state = "no_override";
                                        else { snapshot.crossfade_override = crossfade.Value; snapshot.crossfade_state = "observed"; }
                                    }
                                    catch (Exception e)
                                    {
                                        ++_crossfadeFailures;
                                        snapshot.crossfade_state = "read_fault";
                                        snapshot.crossfade_fault = e.GetType().Name + ": " + e.Message;
                                    }
                                }
                                stage = "animation";
                                var animation = a.Animation;
                                stage = "animation_cast";
                                var moon = animation == null ? null : animation.TryCast<MoonAnimation>();
                                if (moon != null)
                                {
                                    snapshot.animation_identity = moon.Pointer.ToInt64();
                                    stage = "animation_name";
                                    snapshot.animation_name = moon.name;
                                    stage = "blend_configuration";
                                    snapshot.simple_crossfade = moon.m_simpleCrossFadeDuration;
                                    snapshot.layer_blend_in = moon.m_layerBlendInDuration;
                                    snapshot.layer_blend_out = moon.m_layerBlendOutDuration;
                                    snapshot.recenter_extraction = moon.m_recenterRootMotionExtraction;
                                    var clip = moon.TryCast<ClipAnimation>();
                                    if (clip != null)
                                    {
                                        stage = "clip_configuration";
                                        snapshot.keep_last_frame = clip.KeepLastFrame;
                                        var asset = clip.Clip;
                                        if (asset != null) snapshot.clip_name = asset.name;
                                    }
                                }
                            }
                            catch (Exception e) { snapshot.fault = stage + ": " + e.GetType().Name + ": " + e.Message; result.fault = (result.fault ?? "") + " clip:" + snapshot.fault; }
                            clips.Add(snapshot);
                        }
                    }
                    catch (Exception e) { result.fault = (result.fault ?? "") + " layer" + li + ":" + e.GetType().Name; }
                }
            }
            catch (Exception e) { result.fault = (result.fault ?? "") + " animator:" + e.GetType().Name; }
            finally
            {
                result.active_clips = clips.ToArray();
                result.omitted_count = System.Math.Max(0, result.observed_active_count - clips.Count);
                if (result.fault == null) _consecutiveFailures = 0;
                else if (++_consecutiveFailures >= 3) { _disabled = true; result.state = "disabled_after_read_faults"; }
                else result.state = "partial_read_fault";
                result.consecutive_failures = _consecutiveFailures;
            }
            return result;
        }
    }
}
