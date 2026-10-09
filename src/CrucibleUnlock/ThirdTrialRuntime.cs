using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    internal static class ThirdTrialRuntime
    {
        private static volatile bool _enabled;
        private static ThirdTrialCatalog _catalog;
        private static readonly HashSet<IntPtr> ReadyStorages = new();
        private static readonly Dictionary<IntPtr,InfiniteDungeonPlaylist> OwnedPlaylists = new();
        internal static bool Enabled => _enabled;
        internal static void Install()
        {
            _catalog=ThirdTrialCatalog.LoadEmbedded();
            if(!_catalog.Stages.Select(x=>x.ProfileKey).SequenceEqual(EnemyRestorationRuntime.Catalog.Profiles.Select(x=>x.Key)))
                throw new InvalidOperationException("Third trial profile order does not match restored catalog.");
            var owner=new PatchOwner("NRFW.CrucibleUnlock.ThirdTrialRuntime");
            try
            {
                // InitializeContentSystem exports shared static caches with native
                // duplicate-key insertion. Run after that export, never ahead of it
                // from StartOfFrameSystem.OnInit, so Team and other shared refs are
                // adopted by pointer rather than pre-inserted then exported twice.
                TrialRepairLog.Patch(owner,typeof(InitializeContentSystem),"OnInit",new[]{typeof(Frame)},typeof(ThirdTrialRuntime),postfix:nameof(AfterInit));
                TrialRepairLog.Patch(owner,typeof(InfiniteDungeonRoguelitePlaylistRuleDirector),"UpdateActiveRun",new[]{typeof(Frame)},
                    typeof(ThirdTrialRuntime),prefix:nameof(BeforeRuleUpdate));
                _enabled=true;
            }
            catch { _enabled=false; owner.UnpatchSelf(); throw; }
        }
        private static bool BeforeRuleUpdate(Frame __0)
        {
            if(__0==null) return true;
            if(_enabled && EnemyRestorationRuntime.CurrentViewsReady) return true;
            // This native method only advances enemy clear/progress. Dungeon death,
            // disengage and exit handling remain in the separate main director.
            try
            {
                return ThirdTrialFailurePolicy.AllowRuleUpdate(InfiniteDungeonAPI.GetSourcePlaylist(__0).Id.Value,false);
            }
            catch
            {
                // If no source can be read, its identity is unknown. Let the native
                // director's own active-source guard handle it; never freeze all trials.
                return true;
            }
        }
        private static void AfterInit(Frame __0)
        {
            if(!_enabled || __0==null) return;
            try
            {
                EnemyRestorationRuntime.EnsureSimulationAssets(__0);
                ThirdTrialStoragePolicy.Ensure(EnemyRestorationRuntime.Catalog,new ThirdTrialStorageBackend(__0));
                EnsurePlaylist(__0);
                EnemyRestorationRuntime.PublishSimulationViews(__0, requireStorage: true);
                ReadyStorages.Add(__0.InstanceDataStorage.Pointer);
                TrialRepairLog.Write("[third-trial] SIMULATION READY: 10 profiles, 10 private behavior trees, default NPC blocks, 10 distinct arenas + native reward; source="+ThirdTrialPolicy.PlaylistGuid);
            }
            catch(Exception error) { Fail("Initialization failed: "+error); }
        }

        private static void EnsurePlaylist(Frame f)
        {
            var found=f.DynamicAssetDB.FindAsset(new AssetGuid {Value=ThirdTrialPolicy.PlaylistGuid});
            if(found!=null)
            {
                if(!OwnedPlaylists.ContainsKey(found.Pointer) || !MatchesPlaylist(found.TryCast<InfiniteDungeonPlaylist>()))
                    throw new InvalidOperationException("Private playlist GUID collision.");
                return;
            }
            var original=f.Context.ResourceManager.GetAsset(new AssetGuid {Value=ThirdTrialPolicy.TemplatePlaylistGuid})?.TryCast<InfiniteDungeonPlaylist>()
                ?? throw new InvalidOperationException("Second trial playlist template missing.");
            var serializer=f.Context.AssetSerializer;
            var copies=IAssetSerializerExtensions.AssetsFromByteArray(serializer,IAssetSerializerExtensions.AssetToByteArray(serializer,original));
            var copy=copies?.Length==1?copies[0]?.TryCast<InfiniteDungeonPlaylist>():null;
            if(copy==null || copy.Pointer==original.Pointer || copy.Entries==null || copy.Entries.Count!=8)
                throw new InvalidOperationException("Playlist deep clone failed.");
            var reward=copy.Entries[copy.Entries.Count-1];
            if(!reward.RandomizedFromType || (int)reward.FloorType!=4) throw new InvalidOperationException("Second trial reward changed.");
            var entries=new Il2CppSystem.Collections.Generic.List<InfiniteDungeonPlaylistEntry>();
            foreach(var stage in _catalog.Stages)
            {
                // Resolve every static dependency before registering this private source.
                RequireAsset<InfiniteDungeonFloorData>(f,stage.FloorGuid);
                foreach(long nugget in stage.EntryNuggetGuids) RequireAsset<NuggetData>(f,nugget);
                var nuggets=new Il2CppStructArray<AssetRefNuggetData>(stage.EntryNuggetGuids.Length);
                for(int i=0;i<nuggets.Length;i++) nuggets[i]=new AssetRefNuggetData { Id=new AssetGuid {Value=stage.EntryNuggetGuids[i]} };
                entries.Add(new InfiniteDungeonPlaylistEntry { RandomizedFromType=false, FloorType=(InfiniteDungeonFloorType)2,
                    SpecificFloor=new AssetRefInfiniteDungeonFloorData {Id=new AssetGuid {Value=stage.FloorGuid}}, Nuggets=nuggets });
            }
            RequireAsset<WorldSpawnPointData>(f,ThirdTrialPolicy.SpawnPointGuid);
            entries.Add(reward);
            copy.Entries=entries;
            copy.PlaylistType=(InfiniteDungeonPlaylistType)1;
            copy.SpawnPoint=new AssetRefWorldSpawnPointData {Id=new AssetGuid {Value=ThirdTrialPolicy.SpawnPointGuid}};
            copy.Path=ThirdTrialPolicy.Path;
            copy.Guid=new AssetGuid {Value=ThirdTrialPolicy.PlaylistGuid};
            if(!MatchesPlaylist(copy)) throw new InvalidOperationException("Prepared private playlist mismatch.");
            copy.Guid=default;
            // Add may throw after inserting. Retain the attempted private object,
            // never claim readiness or delete its cache-backed dependencies on failure.
            OwnedPlaylists.Add(copy.Pointer,copy);
            f.AddAsset(copy,new AssetGuid {Value=ThirdTrialPolicy.PlaylistGuid});
            var readback=f.DynamicAssetDB.FindAsset(new AssetGuid {Value=ThirdTrialPolicy.PlaylistGuid})?.TryCast<InfiniteDungeonPlaylist>();
            if(readback?.Pointer!=copy.Pointer || !MatchesPlaylist(readback))
                throw new InvalidOperationException("Private playlist Add readback failed.");
        }
        private static T RequireAsset<T>(Frame f,long guid) where T:AssetObject
        {
            return f.FindAsset<AssetObject>(new AssetGuid {Value=guid})?.TryCast<T>()
                ?? throw new InvalidOperationException("Missing playlist dependency "+typeof(T).Name+":"+guid);
        }
        private static bool MatchesPlaylist(InfiniteDungeonPlaylist p)
        {
            if(p==null || p.Guid.Value!=ThirdTrialPolicy.PlaylistGuid || p.Path!=ThirdTrialPolicy.Path || (int)p.PlaylistType!=1 ||
                p.SpawnPoint.Id.Value!=ThirdTrialPolicy.SpawnPointGuid || !p.DisableLootDrops || p.DisableHealingItemsUsage ||
                p.DisableExperienceGain || p.RespiteRoomRange.X!=5 || p.RespiteRoomRange.Y!=5 || p.Entries?.Count!=11) return false;
            for(int i=0;i<10;i++)
            {
                var e=p.Entries[i]; var s=_catalog.Stages[i];
                if(e.RandomizedFromType || (int)e.FloorType!=2 || e.SpecificFloor.Id.Value!=s.FloorGuid || e.Nuggets==null || e.Nuggets.Length!=s.EntryNuggetGuids.Length) return false;
                for(int j=0;j<e.Nuggets.Length;j++) if(e.Nuggets[j].Id.Value!=s.EntryNuggetGuids[j]) return false;
            }
            return p.Entries[10].RandomizedFromType && (int)p.Entries[10].FloorType==4;
        }
        internal static bool IsFrameReady(Frame frame)
        {
            if(!_enabled || !EnemyRestorationRuntime.IsSimulationViewsReady(frame) || frame?.InstanceDataStorage==null || !ReadyStorages.Contains(frame.InstanceDataStorage.Pointer)) return false;
            var playlist=frame.DynamicAssetDB.FindAsset(new AssetGuid {Value=ThirdTrialPolicy.PlaylistGuid})?.TryCast<InfiniteDungeonPlaylist>();
            if(playlist==null || !OwnedPlaylists.ContainsKey(playlist.Pointer) || !MatchesPlaylist(playlist)) return false;
            foreach(var profile in EnemyRestorationRuntime.Catalog.Profiles)
            {
                var npc=frame.DynamicAssetDB.FindAsset(new AssetGuid {Value=profile.NpcGuid})?.TryCast<NpcData>();
                if(npc==null || frame.InstanceDataStorage.Resolve(new NpcDataId {InternalId=profile.NpcGuid}).Get(frame)?.Pointer!=npc.Pointer) return false;
                var block=frame.InstanceDataStorage.Resolve(new RuntimeNpcAttachmentsId {InternalId=profile.NpcGuid}).Get(frame);
                if(block?.Id!=profile.NpcGuid || block.NpcData.Get(frame)?.Pointer!=npc.Pointer) return false;
                var controller=frame.DynamicAssetDB.FindAsset(new AssetGuid {Value=profile.ControllerGuid})?.TryCast<ControllerData>();
                if(controller==null || controller.GetTreeRef(frame).Get(frame)?.OwnerGuid.Value!=profile.ControllerGuid) return false;
            }
            return true;
        }
        internal static InfiniteDungeonPlaylist ResolveReadyPlaylist(Frame f) => IsFrameReady(f)?f.DynamicAssetDB.FindAsset(new AssetGuid {Value=ThirdTrialPolicy.PlaylistGuid}).Cast<InfiniteDungeonPlaylist>():null;
        internal static bool TryGetNpc(Frame f,string key,out AssetRefNpcData npc)
        {
            npc=default;
            return IsFrameReady(f) && EnemyRestorationRuntime.TryGetNpc(f,key,out npc);
        }
        internal static void Fail(string reason)
        {
            if(!_enabled) return;
            _enabled=false;
            TrialRepairLog.Write("[third-trial] UNAVAILABLE; third entrance sealed, original NPC spawn blocked: "+reason);
        }
        internal static void StopWithoutNativeReads() => _enabled=false;
    }
}
