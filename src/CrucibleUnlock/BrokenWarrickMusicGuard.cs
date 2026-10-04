using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    public static class BrokenWarrickMusicGuard
    {
        public const string OwnerId = "NRFW.CrucibleUnlock.BrokenWarrickMusic";
        private static PatchOwner _harmony;
        private static volatile bool _enabled;
        private static Action<string> _log, _error;
        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static void Install(Action<string> log, Action<string> error)
        {
            if (log == null || error == null) throw new ArgumentNullException("log/error");
            if (_harmony != null) throw new InvalidOperationException("Music guard is already installed.");
            var method = typeof(BossBattleZone).GetMethod("PlayPhaseTimeline",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException("PlayPhaseTimeline(Int32)");
            var prior = PatchOwner.GetPatchInfo(method);
            if (prior != null) foreach (var owner in prior.Owners)
                if (owner == OwnerId) throw new InvalidOperationException("Music guard owner already exists.");
            _log = log; _error = error; _enabled = false;
            lock (Seen) Seen.Clear();
            var harmony = new PatchOwner(OwnerId);
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(BrokenWarrickMusicGuard).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                var patches = PatchOwner.GetPatchInfo(method);
                bool found = false;
                if (patches != null) foreach (var owner in patches.Owners) if (owner == OwnerId) found = true;
                if (!found) throw new InvalidOperationException("Music guard patch was not registered.");
                _harmony = harmony; _enabled = true;
                SafeLog(_log, "[warrick-music] installed exact identity plus broken-content guard");
            }
            catch (Exception installError)
            {
                _enabled = false;
                try { harmony.UnpatchSelf(); }
                catch (Exception rollbackError) { throw new AggregateException(installError, rollbackError); }
                throw;
            }
        }

        private static bool Prefix(BossBattleZone __instance, int __0)
        {
            if (!_enabled || __instance == null || __0 < 1) return true;
            string key = null;
            try
            {
                var timelines = __instance.PhaseBattleTimelines;
                if (timelines == null || timelines.Length == 0) return true;
                int index = System.Math.Min(__0 - 1, timelines.Length - 1);
                var timeline = timelines[index];
                if (timeline == null || timeline.name != "bossMusic") return true;
                key = timeline.Pointer.ToString("X") + "/" + __0;
                var candidate = new BrokenWarrickMusicPolicy.Candidate {
                    Phase = __0, PhaseTimelineCount = timelines.Length, TimelineName = timeline.name, Loop = timeline.Loop,
                    FirstId = -999, SecondId = -999
                };
                string identityError = null;
                try { candidate.Scene = __instance.gameObject.scene.path; }
                catch (Exception e) { identityError = "scene:" + e.GetType().Name; }
                try
                {
                    var placeholder = __instance.GetComponent<DirectorZonePlaceholder>();
                    if (placeholder != null)
                    {
                        var guid = placeholder.MoonGuid;
                        candidate.GuidMatches = BrokenWarrickMusicPolicy.MatchesGuid(guid.A, guid.B, guid.C, guid.D);
                    }
                }
                catch (Exception e) { identityError = (identityError ?? "") + " guid:" + e.GetType().Name; }
                var records = timeline.EntityRecords;
                candidate.RecordCount = records == null ? 0 : records.Count;
                if (candidate.RecordCount == 2)
                {
                    var first = records[0]; var second = records[1];
                    if (first != null && second != null)
                    {
                        candidate.FirstId = first.Id.Id; candidate.SecondId = second.Id.Id;
                        var firstItem = first.TimelineItem;
                        var music = firstItem == null ? null : firstItem.TryCast<MusicAnimator>();
                        candidate.FirstIsMusicAnimator = music != null;
                        if (music != null) { candidate.FirstClipIsNull = music.Clip == null; candidate.FirstVolume = music.Volume; }
                        candidate.FirstEndTime = first.EndConstraint.TimeOffset;
                        candidate.SecondItemIsNull = second.TimelineItem == null;
                        candidate.SecondEndEvent = second.EndConstraint.EventId;
                        candidate.SecondEndTarget = second.EndConstraint.ConstrainedTo.Id;
                        candidate.SecondEndTime = second.EndConstraint.TimeOffset;
                    }
                }
                bool skip = BrokenWarrickMusicPolicy.ShouldSkip(candidate);
                if (skip)
                {
                    // Native StopPhaseTimeline only clears these caches when the previous
                    // timeline is valid. Clear explicitly even when it was already null.
                    __instance.StopPhaseTimeline();
                    __instance.m_currentBattleTimeline = null;
                    __instance.m_currentBattleTimelineIndex = -1;
                }
                if (FirstReport(key)) SafeLog(_log,
                    "[warrick-music] candidate=" + key + " phase=" + __0 + " name=" + candidate.TimelineName +
                    " scene=" + (candidate.Scene ?? "<unavailable>") + " guidMatch=" + candidate.GuidMatches +
                    " records=" + candidate.RecordCount + " ids=" + candidate.FirstId + "," + candidate.SecondId +
                    " child0Music=" + candidate.FirstIsMusicAnimator + " child0ClipNull=" + candidate.FirstClipIsNull +
                    " child1Null=" + candidate.SecondItemIsNull + " loop=" + candidate.Loop +
                    " firstVolume=" + candidate.FirstVolume + " firstEndTime=" + candidate.FirstEndTime +
                    " secondEndEvent=" + candidate.SecondEndEvent + " secondEndTarget=" + candidate.SecondEndTarget +
                    " secondEndTime=" + candidate.SecondEndTime + " brokenStructure=" + BrokenWarrickMusicPolicy.HasBrokenStructure(candidate) +
                    " identity=" + BrokenWarrickMusicPolicy.HasIdentity(candidate) + " skipped=" + skip +
                    " identityReadError=" + (identityError ?? "none"));
                return !skip;
            }
            catch (Exception e)
            {
                if (FirstReport((key ?? __instance.Pointer.ToString("X") + "/" + __0) + "/error"))
                    SafeLog(_error, "[warrick-music] read/cleanup failed; original runs: " + e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        private static bool FirstReport(string key) { lock (Seen) return Seen.Add(key); }
        private static void SafeLog(Action<string> callback, string message) { try { callback?.Invoke(message); } catch { } }
        public static void Dispose()
        {
            _enabled = false;
            var harmony = _harmony;
            if (harmony == null) return;
            harmony.UnpatchSelf(); _harmony = null;
            SafeLog(_log, "[warrick-music] own patch removed");
        }
    }
}
