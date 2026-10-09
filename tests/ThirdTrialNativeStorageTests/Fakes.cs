// Purposefully contains no assembly reference to IL2CPP, Unity or the game. The
// linked production backend accesses these test doubles through its real source.
using Il2CppInterop.Runtime.InteropTypes;
namespace Il2CppInterop.Runtime.InteropTypes
{
    class Il2CppObjectBase
    {
        static long next;
        public IntPtr Pointer { get; } = new IntPtr(Interlocked.Increment(ref next));
        public T TryCast<T>() where T:class => this as T;
        public T Cast<T>() => (T)(object)this;
    }
}
namespace Il2CppQuantum
{
    struct AssetGuid { public long Value; }
    interface IAssetResolutionContext { }
    class AssetObject:Il2CppObjectBase { public AssetGuid Guid; }
    class StaticRef<T> where T:AssetObject
    {
        public AssetGuid Id;
        public T Get(Frame f)=>f.FindAsset<T>(Id);
        public T Get(IAssetResolutionContext context)
        {
            var f=(Frame)context;
            if(typeof(T)==typeof(EnemyWeaponData)&&f.BadWeaponGet==Id.Value)
                return new EnemyWeaponData {Guid=Id} as T;
            return Get(f);
        }
    }
    class AssetRefNpcData:StaticRef<NpcData> { }
    class EntityData:AssetObject { }
    class EntityViewData:AssetObject { }
    class TeamData:AssetObject { }
    class InteractionControllerData:AssetObject { }
    class NpcSpawnerData:AssetObject { }
    class EnemyWeaponData:AssetObject { }
    class ControllerData:AssetObject
    {
        public RuntimeRef<Tree> GetTreeRef(Frame f)=>RuntimeRef<Tree>.From(f,Guid.Value);
    }
    class HumanoidEnemyAiControllerData:ControllerData
    {
        public StaticRef<EnemyWeaponData> Weapon=new(),OffHandItem=new();
        public Tree CreateBehaviorTree(IAssetResolutionContext context)
        {
            var frame=(Frame)context;
            frame.TreeBuilds++;
            if(frame.FailTree==Guid.Value) throw new InvalidOperationException("injected tree build");
            if(frame.EmptyTree==Guid.Value) return new Tree { InstanceMemorySize=0 };
            if(frame.NoRootTree==Guid.Value) return new Tree { InstanceMemorySize=64,RootNode=null };
            return new Tree { InstanceMemorySize=64 };
        }
    }
    class NpcData:AssetObject
    {
        public int CharacterType,CharacterVersion;
        public SerializedNpcAttachments Export=new();
        public void ExportNpcBlock(Frame f,SerializedNpcAttachments target)
        {
            f.Exports++;
            if(f.FailExport==Guid.Value) throw new InvalidOperationException("injected NPC export");
            target.Id=Export.Id;target.NpcData=Export.NpcData;target.EntityData=Export.EntityData;
            target.EntityView=Export.EntityView;target.Controller=Export.Controller;target.TeamData=Export.TeamData;
            target.InteractionController=Export.InteractionController;target.Spawner=Export.Spawner;
        }
    }
    class Tree:Il2CppObjectBase { public AssetGuid OwnerGuid;public int InstanceMemorySize;public Il2CppObjectBase RootNode=new(); }
    class SerializedNpcAttachments:Il2CppObjectBase
    {
        public long Id;
        public AssetRefNpcData NpcData=new();
        public StaticRef<EntityData> EntityData=new();
        public StaticRef<EntityViewData> EntityView=new();
        public StaticRef<ControllerData> Controller=new();
        public StaticRef<TeamData> TeamData=new();
        public StaticRef<InteractionControllerData> InteractionController=new();
        public StaticRef<NpcSpawnerData> Spawner=new();
        public long[] NonzeroIds()=>new[]{NpcData.Id.Value,EntityData.Id.Value,EntityView.Id.Value,Controller.Id.Value,TeamData.Id.Value,InteractionController.Id.Value,Spawner.Id.Value}.Where(x=>x!=0).ToArray();
    }
    // Exposes the native 1-based index as a reference. A nonzero ref must resolve
    // in runtime storage; consulting the static Frame asset DB would mask bugs.
    class RuntimeRef<T> where T:Il2CppObjectBase
    {
        public int Index;
        public long Guid;
        public bool IsDefault=>Index==0;
        public T Get(Frame f)=>Index==0?null:f.InstanceDataStorage.Slot<T>().m_storage[Index-1];
        public static RuntimeRef<T> From(Frame f,long guid)
        {
            if(guid==0) return new RuntimeRef<T>();
            if(!f.InstanceDataStorage.Slot<T>().m_lookup.TryGetValue(guid,out int i))
                throw new InvalidOperationException("raw ref has no runtime dependency: "+typeof(T).Name+"/"+guid);
            return new RuntimeRef<T> { Guid=guid,Index=i };
        }
    }
    class RuntimeNpcAttachments:Il2CppObjectBase
    {
        public long Id;
        public int CharacterType,CharacterVersion;
        public RuntimeRef<NpcData> NpcData;
        public RuntimeRef<EntityData> EntityData;
        public RuntimeRef<EntityViewData> EntityView;
        public RuntimeRef<ControllerData> Controller;
        public RuntimeRef<TeamData> TeamData;
        public RuntimeRef<InteractionControllerData> InteractionController;
        public RuntimeRef<NpcSpawnerData> Spawner;
        public RuntimeRef<Il2CppObjectBase> InstanceData=new(),PatrolRoute=new();
        public static RuntimeNpcAttachments FromSerializedNpcBlock(Frame f,SerializedNpcAttachments r)
        {
            f.BlockBuilds++;
            if(f.FailBlockExport==r.Id) throw new InvalidOperationException("injected runtime block export");
            var b=new RuntimeNpcAttachments { Id=r.Id,NpcData=RuntimeRef<NpcData>.From(f,r.NpcData.Id.Value),
                EntityData=RuntimeRef<EntityData>.From(f,r.EntityData.Id.Value),EntityView=RuntimeRef<EntityViewData>.From(f,r.EntityView.Id.Value),
                Controller=RuntimeRef<ControllerData>.From(f,r.Controller.Id.Value),TeamData=RuntimeRef<TeamData>.From(f,r.TeamData.Id.Value),
                InteractionController=RuntimeRef<InteractionControllerData>.From(f,r.InteractionController.Id.Value),Spawner=RuntimeRef<NpcSpawnerData>.From(f,r.Spawner.Id.Value) };
            var npc=b.NpcData.Get(f);b.CharacterType=npc.CharacterType;b.CharacterVersion=npc.CharacterVersion;
            if(f.BadCharacterType==r.Id)b.CharacterType++;
            if(f.BadCharacterVersion==r.Id)b.CharacterVersion++;
            if(f.NondefaultZeroInteraction==r.Id)b.InteractionController.Index=1;
            if(f.NondefaultZeroSpawner==r.Id)b.Spawner.Index=1;
            if(f.MismatchBlock==r.Id) b.Controller.Index=0;
            return b;
        }
    }
    class NativeSlot<T> where T:Il2CppObjectBase
    {
        public readonly Dictionary<long,int> m_lookup=new();
        public readonly List<T> m_storage=new();
        public void Add(long id,T data)
        {
            if(m_lookup.ContainsKey(id)) throw new InvalidOperationException("native duplicate Add rejected: "+id);
            m_storage.Add(data);m_lookup.Add(id,m_storage.Count);
        }
    }
    class InstanceDataStorageSystem:Il2CppObjectBase
    {
        public readonly NativeSlot<NpcData> m_storageNpcData=new();
        public readonly NativeSlot<ControllerData> m_storageControllerData=new();
        public readonly NativeSlot<EntityData> m_storageEntityData=new();
        public readonly NativeSlot<EntityViewData> m_storageEntityViewData=new();
        public readonly NativeSlot<TeamData> m_storageTeamData=new();
        public readonly NativeSlot<InteractionControllerData> m_storageInteractionControllerData=new();
        public readonly NativeSlot<NpcSpawnerData> m_storageNpcSpawnerData=new();
        public readonly NativeSlot<Tree> m_storageTree=new();
        public readonly NativeSlot<RuntimeNpcAttachments> m_storageRuntimeNpcAttachments=new();
        public int Writes;public long FailBlockAdd,FailTreeAdd;public bool AddBeforeThrow;
        public NativeSlot<T> Slot<T>() where T:Il2CppObjectBase
        {
            object slot=typeof(T)==typeof(NpcData)?m_storageNpcData:typeof(T)==typeof(ControllerData)?m_storageControllerData:
                typeof(T)==typeof(EntityData)?m_storageEntityData:typeof(T)==typeof(EntityViewData)?m_storageEntityViewData:
                typeof(T)==typeof(TeamData)?m_storageTeamData:typeof(T)==typeof(InteractionControllerData)?m_storageInteractionControllerData:
                typeof(T)==typeof(NpcSpawnerData)?m_storageNpcSpawnerData:typeof(T)==typeof(Tree)?m_storageTree:
                typeof(T)==typeof(RuntimeNpcAttachments)?m_storageRuntimeNpcAttachments:throw new InvalidOperationException("unknown fake slot "+typeof(T));
            return (NativeSlot<T>)slot;
        }
        public void Add(AssetObject value)
        {
            switch(value)
            {
                case NpcData x: m_storageNpcData.Add(x.Guid.Value,x);break;
                case ControllerData x: m_storageControllerData.Add(x.Guid.Value,x);break;
                case EntityData x: m_storageEntityData.Add(x.Guid.Value,x);break;
                case EntityViewData x: m_storageEntityViewData.Add(x.Guid.Value,x);break;
                case TeamData x: m_storageTeamData.Add(x.Guid.Value,x);break;
                case InteractionControllerData x: m_storageInteractionControllerData.Add(x.Guid.Value,x);break;
                case NpcSpawnerData x: m_storageNpcSpawnerData.Add(x.Guid.Value,x);break;
                default:throw new InvalidOperationException("unexpected storage asset");
            }
            Writes++;
        }
        public void Add(Tree x)
        {
            if(FailTreeAdd==x.OwnerGuid.Value&&!AddBeforeThrow)throw new InvalidOperationException("injected tree Add");
            m_storageTree.Add(x.OwnerGuid.Value,x);Writes++;
            if(FailTreeAdd==x.OwnerGuid.Value)throw new InvalidOperationException("injected tree Add after insertion");
        }
        public void Add(RuntimeNpcAttachments x)
        {
            if(FailBlockAdd==x.Id&&!AddBeforeThrow)throw new InvalidOperationException("injected block Add");
            m_storageRuntimeNpcAttachments.Add(x.Id,x);Writes++;
            if(FailBlockAdd==x.Id)throw new InvalidOperationException("injected block Add after insertion");
        }
    }
    class Frame:Il2CppObjectBase,IAssetResolutionContext
    {
        public FrameContext Context;
        public DynamicAssetDatabase DynamicAssetDB;
        public Frame() { Context=new FrameContext(this); DynamicAssetDB=new DynamicAssetDatabase(this); }
        public InstanceDataStorageSystem InstanceDataStorage=new();
        public readonly Dictionary<long,AssetObject> Assets=new();
        public long FailTree,EmptyTree,NoRootTree,FailExport,FailBlockExport,MismatchBlock,BadWeaponGet;
        public long BadCharacterType,BadCharacterVersion,NondefaultZeroInteraction,NondefaultZeroSpawner;
        public int TreeBuilds,Exports,BlockBuilds;
        public T FindAsset<T>(AssetGuid id) where T:AssetObject=>Assets.TryGetValue(id.Value,out var asset)?asset as T:null;
    }
    class FrameContext
    {
        public ResourceManager ResourceManager;
        public FrameContext(Frame frame) { ResourceManager=new ResourceManager(frame); }
    }
    class ResourceManager
    {
        readonly Frame frame;
        public ResourceManager(Frame frame) { this.frame=frame; }
        public AssetObject GetAsset(AssetGuid guid)=>frame.Assets.GetValueOrDefault(guid.Value);
    }
    class DynamicAssetDatabase
    {
        readonly Frame frame;
        public DynamicAssetDatabase(Frame frame) { this.frame=frame; }
        public AssetObject FindAsset(AssetGuid guid)=>frame.Assets.GetValueOrDefault(guid.Value);
    }
}
namespace CrucibleUnlock
{
    static class TrialRepairLog { internal static void Write(string message) { } }
}
