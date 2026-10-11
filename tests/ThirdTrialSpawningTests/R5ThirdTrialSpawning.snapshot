using System;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using Il2CppQuantum.StreamingGraph;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // The official dynamic spawner chooses a source NPC and all of its position,
    // difficulty, patrol and ownership arguments before entering Spawn. Replacing
    // only npcDataRef keeps those native decisions and native entity lifecycle.
    internal static class ThirdTrialSpawning
    {
        private const string OwnerId = "NRFW.CrucibleUnlock.ThirdTrialSpawning";
        private static PatchOwner _harmony;
        private static ThirdTrialCatalog _catalog;
        private static volatile bool _enabled;

        internal static void Install(Action<string> log, Action<string> error)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (error == null) throw new ArgumentNullException(nameof(error));
            if (_harmony != null) throw new InvalidOperationException("Third-trial spawn hook is already installed.");
            var catalog = ThirdTrialCatalog.LoadEmbedded();
            if (catalog == null || catalog.Stages == null || catalog.Stages.Length != 10)
                throw new InvalidOperationException("Third-trial spawn catalog requires ten ordinary rooms.");
            var harmony = new PatchOwner(OwnerId);
            try
            {
                TrialRepairLog.Patch(harmony, typeof(DynamicNpcSpawnerData), "Spawn",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(AssetRefNpcData), typeof(FPVector3),
                        typeof(FP), typeof(EnemyDifficulty), typeof(long), typeof(string) },
                    typeof(ThirdTrialSpawning), prefix: nameof(BeforeSpawn), returnType: typeof(EntityRef));
                _catalog = catalog;
                _harmony = harmony;
                _enabled = true;
                log("[third-spawn] native DynamicNpcSpawnerData.Spawn hook installed; 10 arena layers scoped");
            }
            catch (Exception installError)
            {
                _enabled = false;
                _catalog = null;
                _harmony = null;
                try { harmony.UnpatchSelf(); }
                catch (Exception rollbackError) { throw new AggregateException(installError, rollbackError); }
                throw;
            }
        }

        // Harmony's ordinal arguments are the native Spawn arguments. In particular
        // __2 is an 8-byte AssetRefNpcData value, never an Il2Cpp array wrapper.
        private static bool BeforeSpawn(Frame __0, EntityRef __1, ref AssetRefNpcData __2, ref EntityRef __result)
        {
            if (!_enabled || __0 == null) return true;
            bool privateSource = false;
            try
            {
                privateSource = InfiniteDungeonAPI.GetSourcePlaylist(__0).Id.Value == ThirdTrialPolicy.PlaylistGuid;
                if (!privateSource) return true;

                // Every normal spawn in this source must carry an integration group.
                // A missing group cannot establish safe ownership, so fail closed.
                if (!__0.Has<IntegrationGroupComponent>(__1) || !__0.Has<DynamicSpawnerComponent>(__1))
                    return Block(ref __result, "missing spawner or integration component");
                var group = __0.Get<IntegrationGroupComponent>(__1);
                var spawner = __0.Get<DynamicSpawnerComponent>(__1);
                var owner = group.StreamingNodeDataIndex.Get(__0);
                if (owner == null) return Block(ref __result, "integration streaming node missing");

                // RuntimeStreamingNodeRef is the source of ownership even while the
                // next room is preloaded and GetCurrentFloor still names this room.
                RuntimeStreamingNode nugget = null, bucket = null, chunk = null;
                var node = owner;
                for (int depth = 0; node != null && depth < 16; ++depth, node = node.Parent)
                {
                    if (node.Kind == StreamingNodeKind.Chunk) { chunk = node; break; }
                    if (node.Kind == StreamingNodeKind.Bucket) bucket = node;
                    if (node.Kind == StreamingNodeKind.Nugget) nugget = node;
                }
                if (chunk == null) return Block(ref __result, "spawner chunk ancestry missing");
                ThirdTrialStage stage = null;
                foreach (var candidate in _catalog.Stages)
                    if (candidate.ChunkGuid == chunk.Id.Value) { stage = candidate; break; }
                if (stage == null) return true; // Third-trial reward / entrance content.
                // The group may point at a parent node; where nugget/bucket nodes
                // occur in its ancestry, require the manifest identities too.
                if ((bucket != null && bucket.Id.Value != stage.BucketGuid) ||
                    (nugget != null && nugget.Id.Value != stage.NuggetGuid))
                    return Block(ref __result, "selected arena spawned from unexpected content: bucket=" +
                        (bucket?.Id.Value ?? 0) + "; nugget=" + (nugget?.Id.Value ?? 0));

                long spawnerGuid = spawner.DynamicSpawnerData.Get(__0)?.Guid ?? 0;
                // The native index is keyed by the integration ID assigned to
                // this content group. This checks the current ID-to-node mapping
                // directly, without assuming the node-state field has already
                // been refreshed for every possible owner kind.
                var integrationSystem = __0.GetSystem<IntegrationStreamingSystem>();
                var mappedOwner = integrationSystem?.TryGetIntegrationNodeById(__0, group.IntegrationId);
                bool integrationMapsToOwner = mappedOwner != null && mappedOwner.Id.Value == owner.Id.Value &&
                    mappedOwner.Entity.Raw == owner.Entity.Raw;
                bool entryMatches = GeneratedEntryMatches(__0, stage);
                var decision = ThirdTrialSpawningPolicy.Decide(ThirdTrialPolicy.PlaylistGuid,
                    ThirdTrialPolicy.PlaylistGuid, chunk.Id.Value, stage.ChunkGuid,
                    bucket?.Id.Value ?? 0, stage.BucketGuid, nugget?.Id.Value ?? 0, stage.NuggetGuid,
                    group.IntegrationId, integrationMapsToOwner,
                    spawnerGuid, stage.SpawnerGuids, entryMatches);
                if (decision == ThirdTrialSpawnDecision.Pass) return true;
                if (decision == ThirdTrialSpawnDecision.Block)
                    return Block(ref __result, "combat spawner scope mismatch; chunk=" + stage.ChunkGuid +
                        "; nugget=" + stage.NuggetGuid + "; spawner=" + spawnerGuid +
                        "; integration=" + group.IntegrationId + "; mappedOwner=" + integrationMapsToOwner +
                        "; generatedEntry=" + entryMatches);
                if (!ThirdTrialRuntime.IsFrameReady(__0) ||
                    !ThirdTrialRuntime.TryGetNpc(__0, stage.ProfileKey, out var replacement))
                    return Block(ref __result, "restored NPC not ready for " + stage.ProfileKey);
                __2 = replacement;
                return true;
            }
            catch (Exception exception)
            {
                if (!privateSource) return true;
                return Block(ref __result, "native spawn scope error: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static bool GeneratedEntryMatches(Frame frame, ThirdTrialStage stage)
        {
            var playlist = InfiniteDungeonAPI.GetRuntimePlaylist(frame);
            if (playlist == null) return false;
            int matches = 0;
            for (int i = 0; i < playlist.Count; ++i)
            {
                var entry = playlist[i];
                if (entry.Chunk.Id.Value != stage.ChunkGuid) continue;
                ++matches;
                var pointer = entry.Nuggets;
                if (pointer == null || !(bool)pointer.Ptr) return false;
                var nuggets = frame.ResolveList(pointer);
                if (nuggets == null || nuggets.Count != stage.EntryNuggetGuids.Length) return false;
                var actual = new long[nuggets.Count];
                for (int j = 0; j < actual.Length; ++j) actual[j] = nuggets[j].Id.Value;
                if (!ThirdTrialSpawningPolicy.SameNuggets(stage.EntryNuggetGuids, actual)) return false;
            }
            return matches == 1;
        }

        private static bool Block(ref EntityRef result, string reason)
        {
            result = default;
            try { ThirdTrialRuntime.Fail("[third-spawn] blocked source NPC: " + reason); }
            catch { } // A diagnostic failure must not invoke the original NPC Spawn.
            return false;
        }

        internal static void StopWithoutNativeReads() => _enabled = false;
        internal static void Dispose()
        {
            _enabled = false;
            var harmony = _harmony;
            if (harmony == null) return;
            harmony.UnpatchSelf();
            _harmony = null;
            _catalog = null;
        }
    }
}
