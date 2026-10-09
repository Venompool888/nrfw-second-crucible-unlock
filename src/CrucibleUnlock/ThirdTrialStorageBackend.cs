using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppQuantum;

namespace CrucibleUnlock
{
    // Created and discarded within OnInit. Retains no Frame or component pointer.
    internal sealed class ThirdTrialStorageBackend : IThirdTrialStorageBackend
    {
        private readonly Frame _frame;
        private readonly InstanceDataStorageSystem _storage;
        private readonly Dictionary<long, SerializedNpcAttachments> _raw = new();
        private sealed class Ownership
        {
            public readonly Dictionary<long, Tree> Trees = new();
            public readonly Dictionary<long, RuntimeNpcAttachments> Blocks = new();
        }
        // Strong wrappers retain only these plugin-owned objects. The storage system
        // itself is never cached; its current pointer is checked on every callback.
        private static readonly Dictionary<IntPtr, Ownership> Owners = new();
        private readonly Ownership _owned;
        public ThirdTrialStorageBackend(Frame frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _storage = frame.InstanceDataStorage ?? throw new InvalidOperationException("Runtime storage missing.");
            if (!Owners.TryGetValue(_storage.Pointer,out _owned))
                Owners.Add(_storage.Pointer,_owned=new Ownership());
        }

        private T Asset<T>(long id) where T : AssetObject
        {
            var a = _frame.FindAsset<AssetObject>(new AssetGuid { Value=id })?.TryCast<T>();
            if(a==null || a.Guid.Value!=id) throw new InvalidOperationException("Missing dependency " + typeof(T).Name + ": " + id);
            return a;
        }
        private static void Check<T>(long id, T expected, T existing) where T : Il2CppObjectBase
        {
            if(expected==null || (existing!=null && existing.Pointer!=expected.Pointer))
                throw new InvalidOperationException("Foreign runtime cache collision: " + id);
        }
        private static void Ensure<T>(long id, T data, Func<long,T> lookup, Action<T> add) where T : Il2CppObjectBase
        {
            var existing=lookup(id); Check(id,data,existing);
            if(existing==null) add(data);
            if(lookup(id)?.Pointer!=data.Pointer) throw new InvalidOperationException("Runtime cache Add readback failed: " + id);
        }
        private NpcData Npc(long id) { var s=_storage.m_storageNpcData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private ControllerData Controller(long id) { var s=_storage.m_storageControllerData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private EntityData Host(long id) { var s=_storage.m_storageEntityData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private EntityViewData View(long id) { var s=_storage.m_storageEntityViewData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private TeamData Team(long id) { var s=_storage.m_storageTeamData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private InteractionControllerData Interaction(long id) { var s=_storage.m_storageInteractionControllerData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private NpcSpawnerData Spawner(long id) { var s=_storage.m_storageNpcSpawnerData; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private Tree Tree(long id) { var s=_storage.m_storageTree; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }
        private RuntimeNpcAttachments Block(long id) { var s=_storage.m_storageRuntimeNpcAttachments; return s.m_lookup.TryGetValue(id,out int i)?s.m_storage[i-1]:null; }

        public void Preflight(EnemyRestorationProfile p)
        {
            var npc=Asset<NpcData>(p.NpcGuid);
            Check(p.NpcGuid,npc,Npc(p.NpcGuid));
            Check(p.HostGuid,Asset<EntityData>(p.HostGuid),Host(p.HostGuid));
            Check(p.ControllerGuid,Asset<ControllerData>(p.ControllerGuid),Controller(p.ControllerGuid));
            // ControllerData also derives EntityData. Native Create instantiates
            // the controller separately and resolves its GUID in that base slot.
            Check(p.ControllerGuid,Asset<EntityData>(p.ControllerGuid),Host(p.ControllerGuid));
            var view=Asset<EntityViewData>(p.ViewGuid);
            var cachedView=View(p.ViewGuid);
            if(cachedView!=null && cachedView.Pointer!=view.Pointer)
            {
                // Capture each resolution source before the strict check seals
                // the entrance. Same GUID/path does not prove object ownership.
                string sources;
                try
                {
                    var guid=new AssetGuid { Value=p.ViewGuid };
                    var dynamicView=_frame.DynamicAssetDB.FindAsset(guid);
                    var resourceView=_frame.Context.ResourceManager.GetAsset(guid);
                    sources="; dynamic="+(dynamicView?.Pointer.ToString()??"null")+
                        "; resource="+(resourceView?.Pointer.ToString()??"null");
                }
                catch(Exception error) { sources="; source probe failed="+error.GetType().Name; }
                TrialRepairLog.Write("[third-trial] VIEW IDENTITY CONFLICT profile="+p.Key+"; guid="+p.ViewGuid+
                    "; frame="+view.Pointer+"; storage="+cachedView.Pointer+sources);
            }
            Check(p.ViewGuid,view,cachedView);
            var raw=new SerializedNpcAttachments();
            npc.ExportNpcBlock(_frame,raw);
            raw.Id=p.NpcGuid;
            raw.NpcData=new AssetRefNpcData { Id=new AssetGuid { Value=p.NpcGuid } };
            if(raw.EntityData.Id.Value!=p.HostGuid || raw.Controller.Id.Value!=p.ControllerGuid ||
                raw.EntityView.Id.Value!=p.ViewGuid || raw.TeamData.Id.Value!=p.TeamGuid)
                throw new InvalidOperationException("Exported attachment graph mismatch: " + p.Key);
            if(raw.TeamData.Id.Value!=0) Check(raw.TeamData.Id.Value,Asset<TeamData>(raw.TeamData.Id.Value),Team(raw.TeamData.Id.Value));
            if(raw.InteractionController.Id.Value!=0) Check(raw.InteractionController.Id.Value,Asset<InteractionControllerData>(raw.InteractionController.Id.Value),Interaction(raw.InteractionController.Id.Value));
            if(raw.Spawner.Id.Value!=0) Check(raw.Spawner.Id.Value,Asset<NpcSpawnerData>(raw.Spawner.Id.Value),Spawner(raw.Spawner.Id.Value));
            var ai=Asset<HumanoidEnemyAiControllerData>(p.ControllerGuid);
            if(ai.Weapon.Id.Value!=(p.BlockedActions.Length>0?p.PrivateWeaponGuid:p.WeaponGuid) || ai.OffHandItem.Id.Value!=p.OffhandGuid ||
                ai.Weapon.Get(_frame.Cast<IAssetResolutionContext>())?.Pointer!=Asset<EnemyWeaponData>(ai.Weapon.Id.Value).Pointer)
                throw new InvalidOperationException("Controller weapon dependency mismatch: "+p.Key);
            if(ai.OffHandItem.Id.Value!=0 && ai.OffHandItem.Get(_frame.Cast<IAssetResolutionContext>())?.Pointer!=Asset<EnemyWeaponData>(ai.OffHandItem.Id.Value).Pointer)
                throw new InvalidOperationException("Controller offhand dependency mismatch: "+p.Key);
            var tree=Tree(p.ControllerGuid);
            if(tree!=null && (!_owned.Trees.TryGetValue(p.ControllerGuid,out var ownedTree) || tree.Pointer!=ownedTree.Pointer || tree.OwnerGuid.Value!=p.ControllerGuid))
                throw new InvalidOperationException("Foreign behavior tree collision: " + p.Key);
            var block=Block(p.NpcGuid);
            if(block!=null && (!_owned.Blocks.TryGetValue(p.NpcGuid,out var ownedBlock) || block.Pointer!=ownedBlock.Pointer))
                throw new InvalidOperationException("Foreign attachment block collision: " + p.Key);
            _raw.Add(p.NpcGuid,raw);
        }

        public void EnsureDependencies(EnemyRestorationProfile p)
        {
            Ensure(p.ViewGuid,Asset<EntityViewData>(p.ViewGuid),View,x=>_storage.Add(x));
            Ensure(p.HostGuid,Asset<EntityData>(p.HostGuid),Host,x=>_storage.Add(x));
            Ensure(p.ControllerGuid,Asset<ControllerData>(p.ControllerGuid),Controller,x=>_storage.Add(x));
            Ensure(p.ControllerGuid,Asset<EntityData>(p.ControllerGuid),Host,x=>_storage.Add(x));
            Ensure(p.NpcGuid,Asset<NpcData>(p.NpcGuid),Npc,x=>_storage.Add(x));
            var r=_raw[p.NpcGuid];
            if(r.TeamData.Id.Value!=0) Ensure(r.TeamData.Id.Value,Asset<TeamData>(r.TeamData.Id.Value),Team,x=>_storage.Add(x));
            if(r.InteractionController.Id.Value!=0) Ensure(r.InteractionController.Id.Value,Asset<InteractionControllerData>(r.InteractionController.Id.Value),Interaction,x=>_storage.Add(x));
            if(r.Spawner.Id.Value!=0) Ensure(r.Spawner.Id.Value,Asset<NpcSpawnerData>(r.Spawner.Id.Value),Spawner,x=>_storage.Add(x));
            if(Tree(p.ControllerGuid)==null)
            {
                var ai=Asset<HumanoidEnemyAiControllerData>(p.ControllerGuid);
                var tree=ai.CreateBehaviorTree(_frame.Cast<IAssetResolutionContext>());
                if(tree==null || tree.RootNode==null || tree.InstanceMemorySize<=0) throw new InvalidOperationException("Behavior tree build failed: " + p.Key);
                tree.OwnerGuid=new AssetGuid { Value=p.ControllerGuid };
                _owned.Trees[p.ControllerGuid]=tree;
                _storage.Add(tree);
                TrialRepairLog.Write("[third-trial] TREE READY profile="+p.Key+"; owner="+p.ControllerGuid+"; memory="+tree.InstanceMemorySize);
            }
            var actualTree=Asset<ControllerData>(p.ControllerGuid).GetTreeRef(_frame).Get(_frame);
            if(actualTree?.Pointer!=_owned.Trees[p.ControllerGuid].Pointer || actualTree.OwnerGuid.Value!=p.ControllerGuid)
                throw new InvalidOperationException("Behavior tree readback failed: " + p.Key);
        }

        public object BuildAttachments(EnemyRestorationProfile p)
        {
            var block=Block(p.NpcGuid);
            if(block!=null) return block;
            var created=RuntimeNpcAttachments.FromSerializedNpcBlock(_frame,_raw[p.NpcGuid]);
            if(!Matches(p,created)) throw new InvalidOperationException("Resolved attachment graph mismatch: " + p.Key);
            return created;
        }
        private bool Matches(EnemyRestorationProfile p,RuntimeNpcAttachments block)
        {
            var npc=Asset<NpcData>(p.NpcGuid);
            if(block==null || block.Id!=p.NpcGuid || block.Controller.Get(_frame)?.Pointer!=Asset<ControllerData>(p.ControllerGuid).Pointer ||
                block.EntityData.Get(_frame)?.Pointer!=Asset<EntityData>(p.HostGuid).Pointer ||
                block.EntityView.Get(_frame)?.Pointer!=Asset<EntityViewData>(p.ViewGuid).Pointer ||
                block.NpcData.Get(_frame)?.Pointer!=npc.Pointer ||
                block.CharacterType!=npc.CharacterType || block.CharacterVersion!=npc.CharacterVersion ||
                (int)block.CharacterType!=p.CharacterType ||
                block.TeamData.Get(_frame)?.Pointer!=Asset<TeamData>(p.TeamGuid).Pointer) return false;
            var r=_raw[p.NpcGuid];
            if(r.InteractionController.Id.Value!=0 && block.InteractionController.Get(_frame)?.Pointer!=Asset<InteractionControllerData>(r.InteractionController.Id.Value).Pointer) return false;
            if(r.InteractionController.Id.Value==0 && !block.InteractionController.IsDefault) return false;
            if(r.Spawner.Id.Value!=0 && block.Spawner.Get(_frame)?.Pointer!=Asset<NpcSpawnerData>(r.Spawner.Id.Value).Pointer) return false;
            if(r.Spawner.Id.Value==0 && !block.Spawner.IsDefault) return false;
            return block.InstanceData.IsDefault && block.PatrolRoute.IsDefault;
        }
        public void EnsureAttachments(EnemyRestorationProfile p,object attachments)
        {
            var block=(RuntimeNpcAttachments)attachments;
            if(!Matches(p,block)) throw new InvalidOperationException("Invalid attachment block: " + p.Key);
            _owned.Blocks[p.NpcGuid]=block;
            Ensure(p.NpcGuid,block,Block,x=>_storage.Add(x));
        }
        public bool IsReady(EnemyRestorationProfile p)
        {
            return _owned.Blocks.TryGetValue(p.NpcGuid,out var block) && Block(p.NpcGuid)?.Pointer==block.Pointer && Matches(p,block) &&
                Host(p.ControllerGuid)?.Pointer==Asset<ControllerData>(p.ControllerGuid).Pointer &&
                _owned.Trees.TryGetValue(p.ControllerGuid,out var tree) && Tree(p.ControllerGuid)?.Pointer==tree.Pointer;
        }
    }
}
