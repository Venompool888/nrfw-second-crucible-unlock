using System;
using System.Diagnostics;
using HarmonyLib;
using PatchOwner = HarmonyLib.Harmony;
using Il2CppQuantum;

namespace CrucibleUnlock
{
    internal static class BossRushProgressionRepair
    {
        private static PatchOwner _harmony;
        private static bool _enabled;
        private static int _errors, _floor = -1;
        private static long _nextObservation;
        private static string _lastObservation;

        public static void Install()
        {
            var h = new PatchOwner("NRFW.CrucibleUnlock.BossRushProgression");
            try
            {
                TrialRepairLog.Patch(h, typeof(InfiniteDungeonBossRushPlaylistRuleDirector), "OnCerimCryptFloorStarted",
                    new[] { typeof(Frame), typeof(int) }, typeof(BossRushProgressionRepair), postfix: nameof(FloorStarted));
                TrialRepairLog.Patch(h, typeof(InfiniteDungeonBossRushPlaylistRuleDirector), "UpdateActiveRun",
                    new[] { typeof(Frame) }, typeof(BossRushProgressionRepair), prefix: nameof(RefreshIntegration), postfix: nameof(Observe));
                RoomReuseStreamingRepair.Install(h);
                _harmony = h; _enabled = true;
                TrialRepairLog.Write("[bossrush-progress] installed native SetFloor bridge, reused-room content selection and integration-change binding");
            }
            catch { _enabled = false; h.UnpatchSelf(); throw; }
        }

        private static bool Active(InfiniteDungeonBossRushPlaylistRuleDirector director, Frame frame)
        {
            if (!_enabled || director == null || frame == null || !frame.IsVerified || !director.IsDirectorActive(frame)) return false;
            var playlist = InfiniteDungeonAPI.GetSourcePlaylist(frame);
            return playlist.Id.Value == TrialRepairPolicy.PlaylistGuid;
        }

        private static void FloorStarted(InfiniteDungeonBossRushPlaylistRuleDirector __instance, Frame __0, int __1)
        {
            try
            {
                if (!Active(__instance, __0)) return;
                var playlist = InfiniteDungeonAPI.GetRuntimePlaylist(__0);
                if (!TrialRepairPolicy.ShouldSetFloor(__0.IsVerified, true, TrialRepairPolicy.PlaylistGuid, __1, playlist.Count))
                { TrialRepairLog.Write("[bossrush-progress] floor skipped index=" + __1 + "; count=" + playlist.Count); return; }
                // Exactly once per native floor-start callback, never per update.
                // Native SetFloor resets this director's state and binds the current streaming node.
                InfiniteDungeonFloorEnemiesKilledClearConditionAPI.SetFloor(__0, __1, __instance);
                RoomReuseStreamingRepair.FloorStarted(__0, __1);
                _floor = __1; _nextObservation = 0; _lastObservation = null;
                TrialRepairLog.Write("[bossrush-progress] SET_FLOOR index=" + __1 + "; count=" + playlist.Count +
                    "; director=" + __instance.Pointer + "; source=" + TrialRepairPolicy.PlaylistGuid + "; verified=true");
                Snapshot(__instance, __0);
            }
            catch (Exception e) { Error("SetFloor", e); }
        }

        private static void RefreshIntegration(InfiniteDungeonBossRushPlaylistRuleDirector __instance, Frame __0)
        {
            try
            {
                if (!Active(__instance, __0) || !RoomReuseStreamingRepair.MatchesCurrentFloor(__0, _floor) ||
                    !RoomReuseStreamingRepair.IsRepeatedRoom(__0, _floor)) return;
                var entry = InfiniteDungeonAPI.GetRuntimePlaylist(__0)[_floor];
                var node = StreamingAPI.FindStreamingNode(__0, entry.Chunk.Id);
                if (!RoomReuseStreamingRepair.TryReadyNode(__0, node, out var actual) ||
                    !TryReadClearState(__instance, __0, out var prior)) return;
                if (!RoomReusePolicy.ShouldRebind(true, true, prior.CurrentFloorChunkEntity.Raw,
                    prior.ChunkIntegrationId, node.Entity.Raw, actual.LatestIntegrationId)) return;
                // SetFloor ran before asynchronous reintegration finished. Native Resolve stops once
                // an ID is cached, so reset only when the real node identity/integration has changed.
                InfiniteDungeonFloorEnemiesKilledClearConditionAPI.SetFloor(__0, _floor, __instance);
                InfiniteDungeonFloorEnemiesKilledClearConditionAPI.ResolveCurrentFloorIntegrationId(__0, __instance);
                _lastObservation = null;
                TrialRepairLog.Write("[bossrush-progress] REBIND floor=" + _floor + "; oldNode=" + prior.CurrentFloorChunkEntity.Raw +
                    "; node=" + node.Entity.Raw + "; oldIntegration=" + prior.ChunkIntegrationId +
                    "; integration=" + actual.LatestIntegrationId + "; reason=streaming-integration-changed");
            }
            catch (Exception e) { Error("integration-binding", e); }
        }

        private static void Observe(InfiniteDungeonBossRushPlaylistRuleDirector __instance, Frame __0)
        {
            if (!_enabled) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextObservation) return;
            _nextObservation = now + Stopwatch.Frequency;
            try { if (Active(__instance, __0)) Snapshot(__instance, __0); }
            catch (Exception e) { Error("observe", e); }
        }

        private static void Snapshot(InfiniteDungeonBossRushPlaylistRuleDirector director, Frame frame)
        {
            // Do not call UpdateCheckFloorClear a second time; the game's update owns it.
            bool finished = InfiniteDungeonAPI.IsCurrentRoomFinished(frame);
            string value = "floor=" + _floor + "; finished=" + finished;
            if (TryReadClearState(director, frame, out var state))
            {
                value += "; integration=" + state.ChunkIntegrationId + "; node=" + state.CurrentFloorChunkEntity.Raw +
                    "; peak=" + state.PeakRelevantEnemies + "; living=" + state.LivingEnemies +
                    "; bossEntity=" + state.ActiveBossFightEntity.Raw;
                if (frame.TryGet<StreamingNodeStateComponent>(state.CurrentFloorChunkEntity, out var actual))
                    value += "; actualIntegration=" + actual.LatestIntegrationId + "; previousIntegration=" + actual.PreviousIntegrationId +
                        "; pendingReintegration=" + (bool)actual.HasPendingForcedReintegrationRequest +
                        "; pendingContent=" + (bool)actual.HasPendingContentChange;
            }
            else value += "; clearState=missing";
            if (value == _lastObservation) return;
            _lastObservation = value;
            TrialRepairLog.Write("[bossrush-progress] STATE " + value);
        }

        private static bool TryReadClearState(InfiniteDungeonBossRushPlaylistRuleDirector director, Frame frame,
            out InfiniteDungeonFloorEnemiesKilledClearConditionAPI.DirectorState state)
        {
            state = default;
            var collection = QtnVerifiedData.Get(frame)?.CrucibleFloorClearDirectorState;
            if (collection != null)
            {
                var ids = collection.DirectorIds;
                var states = collection.States;
                for (int i = 0; i < ids.Count && i < states.Count && i < 64; ++i)
                {
                    if (ids[i].Value != director.Id.Value) continue;
                    state = states[i];
                    return true;
                }
            }
            return false;
        }

        private static void Error(string stage, Exception e)
        {
            if (++_errors <= 3) TrialRepairLog.Write("[bossrush-progress] ERROR " + stage + ": " + e.GetType().Name + ": " + e.Message);
            if (_errors >= 3) { _enabled = false; TrialRepairLog.Write("[bossrush-progress] disabled after errors; restart required"); }
        }
    }
}
