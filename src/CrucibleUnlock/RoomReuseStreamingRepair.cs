using System;
using System.Text;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using Il2CppQuantum.StreamingGraph;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    internal static class RoomReuseStreamingRepair
    {
        private static bool _enabled;
        private static int _floor = -1, _errors;
        private static ulong _runEntity;
        private static long _runStart;
        private static string _lastPayloadLog, _lastScopeLog, _lastSkipLog;

        public static void Install(PatchOwner harmony)
        {
            StreamingPayloadInterop.Initialize();
            TrialRepairLog.Patch(harmony, typeof(InfiniteDungeonDirector), "TeleportPlayersTo",
                new[] { typeof(Frame), typeof(FPVector3), typeof(FP), typeof(EntityRef), typeof(int) },
                typeof(RoomReuseStreamingRepair), prefix: nameof(BeforeTeleport));
            TrialRepairLog.Patch(harmony, typeof(InfiniteDungeonDirector), "UpdateStreaming",
                new[] { typeof(Frame), typeof(bool) }, typeof(RoomReuseStreamingRepair), postfix: nameof(AfterStreaming));
            _enabled = true;
        }

        private static bool InRun(Frame frame, string stage)
        {
            if (!_enabled || frame == null || !frame.IsVerified ||
                InfiniteDungeonAPI.GetSourcePlaylist(frame).Id.Value != TrialRepairPolicy.PlaylistGuid) return false;
            var state = InfiniteDungeonAPI.GetDungeonState(frame);
            bool allowed = RoomReusePolicy.IsNativeActiveState((int)state);
            string value = "floor=" + _floor + "; dungeonState=" + state + "; stateValue=" + (int)state + "; allowed=" + allowed;
            if (value != _lastScopeLog)
            {
                _lastScopeLog = value;
                TrialRepairLog.Write("[bossrush-reuse] RUN_SCOPE stage=" + stage + "; " + value);
            }
            return allowed;
        }

        public static void FloorStarted(Frame frame, int index)
        {
            _floor = index;
            _runEntity = InfiniteDungeonAPI.StateEntity(frame).Raw;
            _runStart = InfiniteDungeonAPI.GetRunStartTime(frame).RawValue;
            _lastPayloadLog = null;
        }

        private static void BeforeTeleport(Frame __0, int __4)
        {
            string operation = "scope";
            try
            {
                if (!InRun(__0, "teleport")) return;
                // Native TeleportToNextSpawnPoint has already selected this exact generated entry.
                // Its UpdateStreaming call precedes this method; native ForceReIntegrate follows it.
                operation = "capture-next-entry";
                FloorStarted(__0, __4);
                ApplyDesiredEntry(__0, __4, "teleport", ref operation);
            }
            catch (Exception e) { Error("before-teleport/" + operation, e); }
        }

        private static void AfterStreaming(InfiniteDungeonDirector __instance, Frame __0)
        {
            string operation = "scope";
            try
            {
                if (!InRun(__0, "streaming")) return;
                operation = "run-identity";
                if (!MatchesCurrentFloor(__0, _floor)) { LogSkip("streaming", _floor, "run-or-floor-identity"); return; }
                operation = "native-current-floor";
                var character = __instance.GetAnyEngagedCharacter(__0);
                if (!character.IsValid) { LogSkip("streaming", _floor, "no-engaged-character"); return; }
                // Native GetCurrentFloor#42667 selects playlist[CompletedFloors+1]. Together
                // with IsPlayerInActiveRun this rejects entrance, exit and restart callbacks;
                // StateEntity/RunStart alone can remain unchanged across those transitions.
                int currentIndex = InfiniteDungeonAPI.GetCompletedFloorsForPlayer(__0, character) + 1;
                bool inActiveRun = InfiniteDungeonAPI.IsPlayerInActiveRun(__0, character);
                if (!RoomReusePolicy.ShouldMaintainEntry(_floor, currentIndex, inActiveRun))
                { LogSkip("streaming", _floor, "native-current=" + currentIndex + "; playerActive=" + inActiveRun); return; }
                // Later native streaming updates still consider the previous duplicate chunk first.
                // Keep the selected current entry's desired payload until the next native transition.
                ApplyDesiredEntry(__0, _floor, "streaming", ref operation);
            }
            catch (Exception e) { Error("after-streaming/" + operation, e); }
        }

        internal static bool MatchesCurrentFloor(Frame frame, int index) => index >= 0 && index == _floor &&
            InfiniteDungeonAPI.StateEntity(frame).Raw == _runEntity &&
            InfiniteDungeonAPI.GetRunStartTime(frame).RawValue == _runStart;

        internal static bool IsRepeatedRoom(Frame frame, int index)
        {
            var playlist = InfiniteDungeonAPI.GetRuntimePlaylist(frame);
            if (index <= 0 || index >= playlist.Count) return false;
            return RoomReusePolicy.SelectEntry(frame.IsVerified, true, InfiniteDungeonAPI.GetSourcePlaylist(frame).Id.Value,
                index, playlist.Count, playlist[index - 1].Chunk.Id.Value, playlist[index].Chunk.Id.Value) >= 0;
        }

        private static void ApplyDesiredEntry(Frame frame, int index, string stage, ref string operation)
        {
            operation = "generated-playlist";
            if (!IsRepeatedRoom(frame, index)) { LogSkip(stage, index, "not-repeated-generated-chunk"); return; }
            var entry = InfiniteDungeonAPI.GetRuntimePlaylist(frame)[index];
            operation = "chunk-node";
            var node = StreamingAPI.FindStreamingNode(frame, entry.Chunk.Id);
            if (node == null || node.Children == null) { LogSkip(stage, index, "streaming-node-or-children-missing"); return; }
            operation = "resolve-entry-nuggets";
            var listPointer = entry.Nuggets;
            if (listPointer is null || !(bool)listPointer.Ptr)
            { LogSkip(stage, index, "nugget-list-null-pointer"); return; }
            // TryResolveList's generated out wrapper reserves only one pointer for
            // the native 16-byte QList value and then wraps _list as a boxed object.
            // ResolveList returns a correctly boxed value through runtime_invoke.
            var nuggets = frame.ResolveList(listPointer);
            if (nuggets == null)
            { LogSkip(stage, index, "nugget-list-unresolved"); return; }
            operation = "read-nugget-count";
            int count = nuggets.Count;
            if (count < 1 || count > 64) { LogSkip(stage, index, "nugget-count=" + count); return; }
            var bucketIds = new long[count];
            var nuggetIds = new long[count];
            for (int i = 0; i < count; ++i)
            {
                operation = "resolve-nugget-bucket/" + i;
                nuggetIds[i] = nuggets[i].Id.Value;
                // This API resolves the same native nugget->bucket mapping used by UpdateStreaming.
                var bucket = StreamingAPI.FindNodeForNugget(frame, nuggets[i]);
                if (bucket != null && bucket.Kind == StreamingNodeKind.Bucket) bucketIds[i] = bucket.Id.Value;
            }
            var observation = new StringBuilder("entry=" + index + "; chunk=" + entry.Chunk.Id.Value +
                "; nuggets=[" + string.Join(",", nuggetIds) + "]");
            for (int child = 0; child < node.Children.Length; ++child)
            {
                operation = "select-bucket/" + child;
                var bucket = node.Children[child];
                if (bucket == null || bucket.Kind != StreamingNodeKind.Bucket) continue;
                int selected = RoomReusePolicy.SelectNugget(bucket.Id.Value, bucketIds);
                if (selected < 0) continue;
                operation = "content-mask/" + bucket.Id.Value;
                if (!StreamingPayloadInterop.TryMask(frame, bucket.Id, nuggets[selected].Id, out ulong mask) || mask == 0)
                {
                    observation.Append("; bucket=").Append(bucket.Id.Value).Append(" mask=missing");
                    continue;
                }
                operation = "read-desired/" + bucket.Id.Value;
                bool hadPrior = StreamingPayloadInterop.TryDesired(frame, bucket.Entity, out var prior);
                bool differs = !hadPrior || !prior.Matches(nuggetIds[selected], mask);
                StreamingPayloadValue confirmed = prior;
                if (differs)
                {
                    try
                    {
                        operation = "set-desired/" + bucket.Id.Value;
                        StreamingPayloadInterop.SetDesired(frame, bucket.Entity,
                            StreamingPayloadArgument.Selected(nuggetIds[selected], mask));
                        operation = "confirm-desired/" + bucket.Id.Value;
                        if (!StreamingPayloadInterop.TryDesired(frame, bucket.Entity, out confirmed) ||
                            !confirmed.Matches(nuggetIds[selected], mask))
                            throw new InvalidOperationException("Boss payload readback mismatch.");
                    }
                    catch (Exception writeError)
                    {
                        // Also restore on readback exceptions, not just a mismatched value.
                        _enabled = false;
                        string failedOperation = operation;
                        operation = "restore-after-write-error/" + bucket.Id.Value;
                        try
                        {
                            StreamingPayloadInterop.SetDesired(frame, bucket.Entity,
                                hadPrior ? StreamingPayloadArgument.Present(prior) : default);
                        }
                        catch (Exception restoreError)
                        {
                            throw new AggregateException("Content repair disabled; previous desired state could not be restored after " +
                                failedOperation, writeError, restoreError);
                        }
                        throw new InvalidOperationException("Content repair disabled; previous desired state restored after " +
                            failedOperation, writeError);
                    }
                }
                observation.Append("; bucket=").Append(bucket.Id.Value).Append(" selectedNugget=")
                    .Append(nuggetIds[selected]).Append(" selectedMask=0x").Append(mask.ToString("x"))
                    .Append(" desiredNugget=").Append(confirmed.Nugget)
                    .Append(" desiredMask=0x").Append(confirmed.Mask.ToString("x"));
                operation = "read-integrated/" + bucket.Id.Value;
                if (frame.TryGet<StreamingNodeStateComponent>(bucket.Entity, out var bucketState))
                    observation.Append(" integratedNugget=").Append(bucketState.IntegratedNuggetGuid.Value)
                        .Append(" integratedMask=0x").Append(bucketState.IntegratedContentMask.ToString("x"));
            }
            string value = observation.ToString();
            if (value == _lastPayloadLog) return;
            _lastPayloadLog = value;
            TrialRepairLog.Write("[bossrush-reuse] CONTENT stage=" + stage + "; " + value);
        }

        private static void LogSkip(string stage, int index, string reason)
        {
            string value = "entry=" + index + "; reason=" + reason;
            if (value == _lastSkipLog) return;
            _lastSkipLog = value;
            TrialRepairLog.Write("[bossrush-reuse] SKIP stage=" + stage + "; " + value);
        }

        internal static bool TryReadyNode(Frame frame, RuntimeStreamingNode node, out StreamingNodeStateComponent state)
        {
            state = default;
            return node != null && EntityRefExtensions.ExistsAndNotPendingDestruction(node.Entity, frame) &&
                frame.TryGet(node.Entity, out state) && frame.Has<IntegratedStreamingNodeComponent>(node.Entity) &&
                !frame.Has<PendingIntegrationComponent>(node.Entity) && !frame.Has<PendingDeIntegrationComponent>(node.Entity) &&
                !state.HasPendingForcedReintegrationRequest && !state.HasPendingContentChange;
        }

        private static void Error(string stage, Exception e)
        {
            if (++_errors <= 3) TrialRepairLog.Write("[bossrush-reuse] ERROR " + stage + ": " + e);
            if (_errors >= 3) { _enabled = false; TrialRepairLog.Write("[bossrush-reuse] disabled after errors; restart required"); }
        }
    }
}
