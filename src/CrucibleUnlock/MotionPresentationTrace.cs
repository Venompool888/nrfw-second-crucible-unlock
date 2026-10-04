using System;
using System.Diagnostics;
using Il2CppMoon.Forsaken;
using UnityEngine;

namespace CrucibleUnlock
{
    // Main-thread getter observations only. No hooks, Frame reads, setters or camera control.
    internal static class MotionPresentationTrace
    {
        private static IntPtr _target;
        private static ulong _entity;
        private static Camera _camera;
        private static Transform _leapTarget;
        private static long _refreshCamera;
        private static int _failures;
        private static bool _disabled;
        private static string _lastError;

        internal static MotionPresentationSnapshot Read(CharacterView view, ulong entity)
        {
            var s = new MotionPresentationSnapshot { state = "observed", camera_ownership = "unknown; active camera animator does not establish Boss ownership" };
            try
            {
                if (view == null) { s.state = "no_view"; return s; }
                var id = view.Pointer;
                if (_target != id || _entity != entity)
                {
                    _target = id; _entity = entity; _failures = 0; _disabled = false; _lastError = null;
                    _camera = null; _leapTarget = null; _refreshCamera = 0;
                }
                if (_disabled) { s.state = "disabled_after_3_consecutive_failures"; s.error = _lastError; return s; }
            }
            catch (Exception ex) { Fail(s, "target", ex); return s; }

            bool failed = false;
            try
            {
                s.root_rotation = Rotation(view.transform);
                s.visual_rotation = Rotation(view.VisualTransform);
                var avatar = view.CharacterAvatarView;
                var animator = view.CharacterAnimator;
                if (animator != null) s.animator_rotation = Rotation(animator.transform);
                if (avatar != null)
                {
                    var root = avatar.Root;
                    if (root != null) { s.avatar_rotation = Rotation(root); s.avatar_lossy_scale = Vec(root.lossyScale); }
                    s.skeleton_rotation = Rotation(avatar.SkeletonGroup);
                    var pelvis = avatar.Pelvis;
                    if (pelvis != null)
                    {
                        s.pelvis_local_rotation = Quat(pelvis.localRotation);
                        var parent = pelvis.parent;
                        if (parent != null) { s.pelvis_parent_identity = parent.Pointer.ToInt64(); s.pelvis_parent_rotation = Rotation(parent); }
                    }
                }
            }
            catch (Exception ex) { failed = true; Append(s, "geometry", ex); }
            try
            {
                var action = view.CurrentActionLayerActionView;
                var leap = action == null ? null : action.TryCast<LeapAttackView>();
                bool currentLeap = leap != null;
                if (currentLeap)
                {
                    var target = leap.LeapTargetCameraTarget;
                    _leapTarget = target == null ? null : target.Transform;
                }
                var targetTransform = _leapTarget;
                s.leap_target_state = targetTransform == null ? "not_observed" : currentLeap ? "current_leap_target" : "last_leap_transform_alive";
                if (targetTransform != null)
                {
                    s.leap_target_identity = targetTransform.Pointer.ToInt64();
                    var position = targetTransform.position;
                    s.leap_target_position = Vec(position);
                    s.leap_target_local_position = Vec(targetTransform.localPosition);
                    var parent = targetTransform.parent;
                    if (parent != null)
                    {
                        s.leap_target_parent_identity = parent.Pointer.ToInt64();
                        s.leap_target_parent_position = Vec(parent.position);
                    }
                    var root = view.transform;
                    if (root != null)
                    {
                        var boss = root.position;
                        double dx = position.x - boss.x, dy = position.y - boss.y, dz = position.z - boss.z;
                        s.leap_target_distance_from_boss = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    }
                }
            }
            catch (Exception ex) { failed = true; s.leap_target_state = "read_fault"; _leapTarget = null; Append(s, "leap_target", ex); }
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (_camera == null || now >= _refreshCamera)
                {
                    _camera = Camera.main; _refreshCamera = now + Stopwatch.Frequency;
                }
                var camera = _camera;
                s.camera_state = camera == null ? "no_main_camera" : "observed";
                if (camera != null)
                {
                    s.camera_identity = camera.Pointer.ToInt64();
                    var t = camera.transform;
                    s.camera_position = t == null ? null : Vec(t.position);
                    s.camera_rotation = Rotation(t);
                    s.camera_fov = camera.fieldOfView;
                    s.camera_orthographic = camera.orthographic;
                    s.camera_orthographic_size = camera.orthographicSize;
                }
            }
            catch (Exception ex) { failed = true; s.camera_state = "read_fault"; _camera = null; _refreshCamera = 0; Append(s, "camera", ex); }
            // Independent catches preserve geometry even if one optional animator getter fails.
            try
            {
                var zoom = PlayerCameraZoomAnimator.Active;
                s.camera_zoom_active = zoom != null;
                if (zoom != null) { s.camera_zoom_identity = zoom.Pointer.ToInt64(); s.camera_current_zoom = zoom.CurrentZoom; }
            }
            catch (Exception ex) { failed = true; Append(s, "camera_zoom", ex); }
            try
            {
                var focus = PlayerCameraFocusAnimator.Active;
                s.camera_focus_active = focus != null;
                if (focus != null)
                {
                    s.camera_focus_identity = focus.Pointer.ToInt64(); s.camera_focus_weight = focus.CurrentWeight;
                    var point = focus.FocusPoint;
                    if (point != null) { s.camera_focus_point_identity = point.Pointer.ToInt64(); s.camera_focus_point = Vec(point.position); }
                }
            }
            catch (Exception ex) { failed = true; Append(s, "camera_focus", ex); }
            try
            {
                var cinematic = PlayerCameraCinematicAnimator.Active;
                s.camera_cinematic_active = cinematic != null;
                if (cinematic != null) s.camera_cinematic_identity = cinematic.Pointer.ToInt64();
            }
            catch (Exception ex) { failed = true; Append(s, "camera_cinematic", ex); }
            if (failed)
            {
                _lastError = s.error;
                if (++_failures >= 3) { _disabled = true; s.state = "disabled_after_3_consecutive_failures"; }
                else s.state = "partial_observation";
            }
            else _failures = 0;
            return s;
        }
        internal static void Reset()
        {
            _target = IntPtr.Zero; _entity = 0; _camera = null; _leapTarget = null; _refreshCamera = 0;
            _failures = 0; _disabled = false; _lastError = null;
        }
        private static void Fail(MotionPresentationSnapshot s, string stage, Exception ex)
        {
            Append(s, stage, ex); _lastError = s.error;
            _disabled = ++_failures >= 3;
            s.state = _disabled ? "disabled_after_3_consecutive_failures" : "partial_observation";
        }
        private static void Append(MotionPresentationSnapshot s, string stage, Exception ex)
        { s.error = (s.error == null ? "" : s.error + "; ") + stage + ": " + ex.GetType().Name + ": " + ex.Message; }
        private static MotionQuat Rotation(Transform t) => t == null ? null : Quat(t.rotation);
        private static MotionQuat Quat(Quaternion q) => new MotionQuat { x = q.x, y = q.y, z = q.z, w = q.w };
        private static MotionVec3 Vec(Vector3 v) => new MotionVec3 { x = v.x, y = v.y, z = v.z };
    }
}
