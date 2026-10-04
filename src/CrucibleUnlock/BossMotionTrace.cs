using System;
using System.Diagnostics;
using System.Threading;
using Il2CppMoon.Forsaken;
using UnityEngine;

namespace CrucibleUnlock
{
    // No Harmony patches. Discovers and samples Unity view objects on the loader main thread only.
    public static class BossMotionTrace
    {
        public const long TargetNpcGuid = 2680182020363611965L;
        public const long TargetInstanceGuid = -195804871L;
        private static MotionTraceSink _sink;
        private static Action<string> _log, _error;
        private static volatile bool _enabled;
        private static NpcInstance _npc;
        private static CharacterView _view;
        private static ulong _entity;
        private static int _thread, _lastFrame = -1;
        private static long _nextScan;
        private static bool _ambiguityReported;
        private static int _errorCount;
        private static bool _firstSampleReported, _boundaryReady;
        private static int _threadWarning;
        private static long _nextSummary;
        private static long _nextCandidateDiagnostic;
        private static bool _eligibleDiagnosticReported;
        private static IntPtr _viewId, _avatarId, _pelvisId, _skeletonId;

        public static void Install(MotionTraceSink sink, Action<string> log, Action<string> error)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (_sink != null) throw new InvalidOperationException("Boss motion trace already installed.");
            _sink = sink; _log = log; _error = error; _errorCount = 0;
            _thread = Thread.CurrentThread.ManagedThreadId;
            _nextScan = 0; _nextSummary = 0; _nextCandidateDiagnostic = 0; _threadWarning = 0; _lastFrame = -1; _ambiguityReported = false;
            _boundaryReady = false; _viewId = _avatarId = _pelvisId = _skeletonId = IntPtr.Zero;
            _eligibleDiagnosticReported = false;
            _npc = null; _view = null; _entity = 0; _firstSampleReported = false;
            MotionPresentationTrace.Reset();
            MotionActionTrace.Reset();
            _enabled = true;
            Safe(_log, "[boss-motion] hook-free main-thread discovery enabled; raw/interpolated/simulation-frame/teleport unknown");
        }

        private static bool Valid(NpcInstance npc, out CharacterView view, out ulong entity)
        {
            view = null; entity = 0;
            if (npc == null || !npc.IsBoundToEntity) return false;
            var data = npc.InstanceData;
            if (data == null || data.Guid != TargetInstanceGuid || data.NpcData.Id.Value != TargetNpcGuid) return false;
            view = npc.SpawnedEntityView;
            if (view == null || !view.IsBoundToEntity) return false;
            // NpcInstance.EntityRef is the spawner; the child view owns the NPC entity.
            entity = view.EntityRef.Raw;
            return entity != 0;
        }

        private static void Lost(string reason)
        {
            if (_entity != 0)
            {
                var r = Record("target_lost"); r.note = reason; _sink.TryWrite(r);
                Safe(_log, "[boss-motion] target lost entity=" + _entity + "; " + reason);
            }
            _npc = null; _view = null; _entity = 0; _firstSampleReported = false; _boundaryReady = false;
            _viewId = _avatarId = _pelvisId = _skeletonId = IntPtr.Zero;
            MotionPresentationTrace.Reset();
            MotionActionTrace.Reset();
            _nextScan = System.Math.Min(_nextScan, Stopwatch.GetTimestamp() + Stopwatch.Frequency);
        }

        private static void Discover()
        {
            // Main-thread scan: 1s while unbound, 5s while bound for ambiguity checks.
            long now = Stopwatch.GetTimestamp();
            if (now < _nextScan) return;
            _nextScan = now + Stopwatch.Frequency * (_entity == 0 ? 1 : 5);
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<NpcInstance>();
                NpcInstance match = null; CharacterView view = null; ulong entity = 0;
                int count = 0, total = 0, npcGuidMatches = 0, doubleGuidMatches = 0;
                bool diagnoseCandidates = now >= _nextCandidateDiagnostic;
                foreach (var npc in found)
                {
                    ++total;
                    if (npc != null)
                    {
                        var d = npc.InstanceData;
                        if (d != null && d.NpcData.Id.Value == TargetNpcGuid)
                        {
                            ++npcGuidMatches;
                            if (d.Guid == TargetInstanceGuid)
                            {
                                ++doubleGuidMatches;
                                bool eligible = Valid(npc, out _, out _);
                                if (diagnoseCandidates || (eligible && !_eligibleDiagnosticReported)) CandidateDiagnostic(npc);
                                if (eligible) _eligibleDiagnosticReported = true;
                            }
                        }
                    }
                    if (!Valid(npc, out var candidateView, out var candidateEntity)) continue;
                    ++count;
                    if (count == 1) { match = npc; view = candidateView; entity = candidateEntity; }
                }
                if (diagnoseCandidates && doubleGuidMatches > 0) _nextCandidateDiagnostic = now + 30 * Stopwatch.Frequency;
                if (count != 1)
                {
                    if (now >= _nextSummary)
                    {
                        _nextSummary = now + 30 * Stopwatch.Frequency;
                        string summary = "NPCs=" + total + "; npc_guid_matches=" + npcGuidMatches + "; double_guid_matches=" + doubleGuidMatches + "; valid_bound_matches=" + count;
                        var diagnostic = Record("discovery_summary"); diagnostic.note = summary; _sink.TryWrite(diagnostic);
                        Safe(_log, "[boss-motion] discovery " + summary);
                    }
                    Lost(count > 1 ? "multiple exact bound candidates; refusing ambiguous target" : "no exact bound candidate");
                    if (count > 1 && !_ambiguityReported)
                    {
                        _ambiguityReported = true;
                        var r = Record("discovery_error"); r.note = "multiple exact bound candidates: " + count; _sink.TryWrite(r);
                        Safe(_error, "[boss-motion] multiple exact bound candidates: " + count);
                    }
                    if (count == 0) _ambiguityReported = false;
                    return;
                }
                _ambiguityReported = false;
                if (_npc != null && _entity == entity && _npc.Pointer == match.Pointer) { _view = view; return; }
                Lost("target replaced");
                _npc = match; _view = view; _entity = entity;
                _boundaryReady = false;
                Safe(_log, "[boss-motion] target found entity=" + entity);
            }
            catch (Exception ex) { Lost("discovery failed"); Failed("discovery", ex); }
        }

        private static void CandidateDiagnostic(NpcInstance npc)
        {
            bool npcBound = npc.IsBoundToEntity;
            ulong spawner = npc.EntityRef.Raw;
            var view = npc.SpawnedEntityView;
            bool present = view != null;
            bool viewBound = present && view.IsBoundToEntity;
            ulong child = present ? view.EntityRef.Raw : 0;
            string reason = !npcBound ? "npc_not_bound" : !present ? "no_child_view" : !viewBound ? "child_view_not_bound" : child == 0 ? "child_entity_zero" : "eligible";
            string text = "npc_bound=" + npcBound + "; spawner_raw=" + spawner + "; view_present=" + present + "; view_bound=" + viewBound + "; view_raw=" + child + "; reason=" + reason + "; NpcInstance.EntityRef is spawner, not child";
            var record = _sink.NewRecord("candidate_diagnostic", unchecked((long)child));
            record.unity_frame = Time.frameCount; record.note = text;
            _sink.TryWrite(record);
            Safe(_log, "[boss-motion] candidate " + text);
        }

        // Called by the loader on the Unity main thread. All frames preserve short stand-up jumps.
        public static void LateUpdate()
        {
            if (!_enabled) return;
            if (_sink.Error != null) { _enabled = false; Safe(_error, "[boss-motion] sink failed; sampling disabled: " + _sink.Error); return; }
            if (Thread.CurrentThread.ManagedThreadId != _thread)
            {
                if (Interlocked.Exchange(ref _threadWarning, 1) == 0)
                {
                    const string text = "LateUpdate thread differs from installation thread; no Unity reads performed";
                    Safe(_error, "[boss-motion] " + text);
                    try { var diagnostic = _sink.NewRecord("thread_mismatch"); diagnostic.note = text; _sink.TryWrite(diagnostic); } catch { }
                }
                return;
            }
            try
            {
                int frame = Time.frameCount;
                if (_lastFrame == frame) return;
                _lastFrame = frame;
                Discover();
                if (_npc == null) return;
                if (!Valid(_npc, out var currentView, out var currentEntity) || currentEntity != _entity)
                { Lost("cached binding invalid"); return; }
                _view = currentView;
                var avatar = _view.CharacterAvatarView;
                var avatarRoot = avatar == null ? null : avatar.Root;
                var pelvis = avatar == null ? null : avatar.Pelvis;
                var skeleton = avatar == null ? null : avatar.SkeletonGroup;
                if (!Boundary(avatarRoot, pelvis, skeleton)) return;
                var r = Record("sample");
                r.root = Position(_view.transform);
                r.visual = Position(_view.VisualTransform);
                r.avatar = Position(avatarRoot);
                if (avatar != null)
                {
                    if (pelvis != null) { r.pelvis = Copy(pelvis.position); r.pelvis_local = Copy(pelvis.localPosition); }
                    if (skeleton != null) { r.skeleton = Copy(skeleton.position); r.skeleton_local = Copy(skeleton.localPosition); }
                }
                var animator = _view.CharacterAnimator;
                if (animator != null) { r.animator = Position(animator.transform); r.root_velocity = Copy(animator.RootVelocity); }
                r.previous_ideal = Copy(_view.m_previousFrameIdealPosition);
                r.position_error = Copy(_view.m_positionError);
                var b = _view.CurrentBaseActionLayerActionView;
                var a = _view.CurrentActionLayerActionView;
                if (b != null) { r.base_action = b.BakedViewName; r.base_action_time = b.ActionViewTime; }
                if (a != null) { r.layer_action = a.BakedViewName; r.layer_action_time = a.ActionViewTime; }
                r.presentation = MotionPresentationTrace.Read(_view, _entity);
                r.animation_state = MotionActionTrace.Read(_view);
                r.note = "Unity main-thread LateUpdate snapshot; ordering versus animation evaluation unknown; raw/interpolated/simulation-frame/teleport unknown; active camera ownership unknown";
                if (_sink.TryWrite(r) && !_firstSampleReported)
                {
                    _firstSampleReported = true;
                    Safe(_log, "[boss-motion] first position sample queued; entity=" + _entity + " frame=" + frame);
                }
            }
            catch (Exception ex) { try { Lost("sample failed"); } catch { _npc = null; _view = null; _entity = 0; } Failed("late-update", ex); }
        }

        private static bool Boundary(Transform avatar, Transform pelvis, Transform skeleton)
        {
            // IDs are compared only; never used to dereference native memory.
            IntPtr view = _view.Pointer, av = avatar == null ? IntPtr.Zero : avatar.Pointer;
            IntPtr pe = pelvis == null ? IntPtr.Zero : pelvis.Pointer, sk = skeleton == null ? IntPtr.Zero : skeleton.Pointer;
            if (_viewId != view || _avatarId != av || _pelvisId != pe || _skeletonId != sk)
            {
                _viewId = view; _avatarId = av; _pelvisId = pe; _skeletonId = sk;
                _boundaryReady = false; _firstSampleReported = false;
                MotionPresentationTrace.Reset();
                MotionActionTrace.Reset();
            }
            if (!_boundaryReady)
            {
                var record = Record("target_bound");
                record.note = "exact target or view/rig identity boundary; view=" + view + "; avatar=" + av + "; pelvis=" + pe + "; skeleton=" + sk + "; raw/interpolated/frame/teleport unknown";
                _boundaryReady = _sink.TryWrite(record);
            }
            return _boundaryReady;
        }

        private static MotionTraceRecord Record(string kind)
        {
            var r = _sink.NewRecord(kind, unchecked((long)_entity));
            r.unity_frame = Time.frameCount;
            return r;
        }
        private static MotionVec3 Copy(Vector3 p) => new MotionVec3 { x = p.x, y = p.y, z = p.z };
        private static MotionVec3 Position(Transform t) => t == null ? null : Copy(t.position);
        private static void Failed(string stage, Exception ex)
        {
            if (Interlocked.Increment(ref _errorCount) > 3) return;
            var text = stage + ": " + ex.GetType().Name + ": " + ex.Message;
            Safe(_error, "[boss-motion] " + text);
            try { var r = _sink.NewRecord(stage == "discovery" ? "discovery_error" : "sampling_error"); r.note = text; _sink.TryWrite(r); } catch { }
        }
        private static void Safe(Action<string> callback, string text) { try { callback?.Invoke(text); } catch { } }
        // Safe during application shutdown: does not unpatch or touch Unity/IL2CPP objects.
        public static void StopSampling() { _enabled = false; }
        public static void Dispose()
        {
            _enabled = false;
            _npc = null; _view = null; _entity = 0; _sink = null;
        }
    }
}
