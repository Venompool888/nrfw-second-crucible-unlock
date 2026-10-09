using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppQuantum;
namespace HarmonyLib
{
    class Harmony {public string Id;public Harmony(string id){Id=id;}public void UnpatchSelf(){} }
}
namespace Il2CppSystem.Collections.Generic {class List<T>:System.Collections.Generic.List<T>{} }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    class Il2CppStructArray<T>
    {
        readonly T[] items;public int Length=>items.Length;
        public Il2CppStructArray(int size){items=new T[size];}
        public T this[int i]{get=>items[i];set=>items[i]=value;}
    }
}
namespace Il2CppQuantum
{
    enum InfiniteDungeonPlaylistType {Roguelite=1,BossRush=2,Horde=3}
    enum InfiniteDungeonFloorType {Arena=2,Boss=3,Reward=4}
    struct AssetRefNuggetData {public AssetGuid Id;}
    struct AssetRefInfiniteDungeonFloorData {public AssetGuid Id;}
    struct AssetRefWorldSpawnPointData {public AssetGuid Id;}
    struct IntVector2 {public int X,Y;}
    class InfiniteDungeonFloorData:AssetObject{}
    class NuggetData:AssetObject{}
    class WorldSpawnPointData:AssetObject{}
    class InfiniteDungeonPlaylistEntry
    {
        public bool RandomizedFromType;public InfiniteDungeonFloorType FloorType;
        public AssetRefInfiniteDungeonFloorData SpecificFloor;public Il2CppStructArray<AssetRefNuggetData> Nuggets;
    }
    class InfiniteDungeonPlaylist:AssetObject
    {
        public string Path;public Il2CppSystem.Collections.Generic.List<InfiniteDungeonPlaylistEntry> Entries=new();
        public InfiniteDungeonPlaylistType PlaylistType;public AssetRefWorldSpawnPointData SpawnPoint;
        public bool DisableLootDrops,DisableHealingItemsUsage,DisableExperienceGain;
        public IntVector2 RespiteRoomRange;public long LootDropModifierRaw;public int BoonsPerShop;
        public Dictionary<string,int> ExtraLootSettings=new();
        public InfiniteDungeonPlaylist Copy()
        {
            var c=new InfiniteDungeonPlaylist{Guid=Guid,Path=Path,PlaylistType=PlaylistType,SpawnPoint=SpawnPoint,
                DisableLootDrops=DisableLootDrops,DisableHealingItemsUsage=DisableHealingItemsUsage,DisableExperienceGain=DisableExperienceGain,
                RespiteRoomRange=RespiteRoomRange,LootDropModifierRaw=LootDropModifierRaw,BoonsPerShop=BoonsPerShop,
                ExtraLootSettings=new Dictionary<string,int>(ExtraLootSettings)};
            foreach(var e in Entries)
            {
                var n=e.Nuggets==null?null:new Il2CppStructArray<AssetRefNuggetData>(e.Nuggets.Length);
                if(n!=null)for(int i=0;i<n.Length;i++)n[i]=e.Nuggets[i];
                c.Entries.Add(new InfiniteDungeonPlaylistEntry{RandomizedFromType=e.RandomizedFromType,FloorType=e.FloorType,SpecificFloor=e.SpecificFloor,Nuggets=n});
            }
            return c;
        }
    }
    class FakeDynamicDB
    {
        public readonly Dictionary<long,AssetObject> Assets=new();
        public AssetObject FindAsset(AssetGuid guid)=>Assets.TryGetValue(guid.Value,out var a)?a:null;
    }
    class FakeResourceManager
    {
        public readonly Dictionary<long,AssetObject> Assets=new();
        public AssetObject GetAsset(AssetGuid id)=>Assets.TryGetValue(id.Value,out var a)?a:null;
    }
    class FakeSerializer
    {
        public InfiniteDungeonPlaylist Original;public bool Fail,ReturnOriginal,WrongCount,WrongReward;
        public int Writes,Reads;
    }
    class FakeContext {public FakeResourceManager ResourceManager=new();public FakeSerializer AssetSerializer=new();}
    static class IAssetSerializerExtensions
    {
        public static byte[] AssetToByteArray(FakeSerializer serializer,AssetObject original)
        {
            serializer.Writes++;if(serializer.Fail)throw new InvalidOperationException("serializer write failure");
            serializer.Original=(InfiniteDungeonPlaylist)original;return new byte[]{1};
        }
        public static AssetObject[] AssetsFromByteArray(FakeSerializer serializer,byte[] data)
        {
            serializer.Reads++;if(serializer.Fail)throw new InvalidOperationException("serializer read failure");
            var copy=serializer.ReturnOriginal?serializer.Original:serializer.Original.Copy();
            if(serializer.WrongCount)copy.Entries.RemoveAt(0);
            if(serializer.WrongReward)copy.Entries[^1].FloorType=InfiniteDungeonFloorType.Boss;
            return new AssetObject[]{copy};
        }
    }
    struct NpcDataId {public long InternalId;}
    struct RuntimeNpcAttachmentsId {public long InternalId;}
    partial class InstanceDataStorageSystem
    {
        public readonly List<string> Events=new();
        public RuntimeRef<NpcData> Resolve(NpcDataId id)=>m_storageNpcData.m_lookup.TryGetValue(id.InternalId,out int i)?new RuntimeRef<NpcData>{Guid=id.InternalId,Index=i}:new();
        public RuntimeRef<RuntimeNpcAttachments> Resolve(RuntimeNpcAttachmentsId id)=>m_storageRuntimeNpcAttachments.m_lookup.TryGetValue(id.InternalId,out int i)?new RuntimeRef<RuntimeNpcAttachments>{Guid=id.InternalId,Index=i}:new();
    }
    partial class Frame
    {
        public readonly FakeDynamicDB DynamicAssetDB=new();public readonly FakeContext Context=new();
        public long SourcePlaylist;public bool FailPlaylistAdd,FailPlaylistAfterAdd,WrongPlaylistReadback,DifferentPointerReadback;
        public int PlaylistAdds,PrivateAssetsPrepared;
        public int OfficialContentExports,OfficialPooledExports;
        public void AddAsset(AssetObject value,AssetGuid guid)
        {
            if(InstanceDataStorage.m_storageRuntimeNpcAttachments.m_storage.Count!=10||InstanceDataStorage.m_storageTree.m_storage.Count!=10)
                throw new InvalidOperationException("playlist before complete storage");
            InstanceDataStorage.Events.Add("playlist");
            if(FailPlaylistAdd)throw new InvalidOperationException("playlist Add failure");
            if(DynamicAssetDB.Assets.ContainsKey(guid.Value))throw new InvalidOperationException("dynamic DB duplicate");
            value.Guid=guid;DynamicAssetDB.Assets.Add(guid.Value,value);Assets[guid.Value]=value;PlaylistAdds++;
            if(WrongPlaylistReadback)((InfiniteDungeonPlaylist)value).Path="wrong-readback";
            if(DifferentPointerReadback)DynamicAssetDB.Assets[guid.Value]=((InfiniteDungeonPlaylist)value).Copy();
            if(FailPlaylistAfterAdd)throw new InvalidOperationException("playlist Add after insertion failure");
        }
    }
    class StartOfFrameSystem {public void OnInit(Frame frame){frame.InstanceDataStorage.Events.Add("official-start");} }
    class InitializeContentSystem
    {
        public void OnInit(Frame frame)
        {
            frame.InstanceDataStorage.Events.Add("official-content-begin");
            // Native ExportRandomAssets Add is deliberately NON-idempotent, even
            // if the exact same static Team object was already added by a mod.
            foreach(var team in frame.Assets.Values.OfType<TeamData>())frame.InstanceDataStorage.Add(team);
            frame.OfficialContentExports++;
            // Represents further unrelated official/pooled content exports that
            // must remain reachable after Team export succeeds.
            frame.InstanceDataStorage.Add(new NpcSpawnerData{Guid=new(){Value=883344455566L}});
            frame.OfficialPooledExports++;
            frame.InstanceDataStorage.Events.Add("official-content-complete");
        }
    }
    class InfiniteDungeonRoguelitePlaylistRuleDirector {public void UpdateActiveRun(Frame frame){} }
    static class InfiniteDungeonAPI {public static AssetRefInfiniteDungeonFloorData GetSourcePlaylist(Frame f)=>new(){Id=new(){Value=f.SourcePlaylist}};}
}
namespace CrucibleUnlock
{
    static partial class TrialRepairLog
    {
        internal sealed record Hook(Type TargetType,string Method,Type PatchType,string Prefix,string Postfix);
        internal static readonly List<Hook> Hooks=new();
        internal static void Patch(HarmonyLib.Harmony owner,Type type,string name,Type[] args,Type patchType,string prefix=null,string postfix=null)
        {
            if(type.GetMethod(name,args)==null)throw new MissingMethodException(name);
            Hooks.Add(new Hook(type,name,patchType,prefix,postfix));
        }
        internal static void Dispatch(Type type,string method,Frame frame,Action original)
        {
            original();
            foreach(var hook in Hooks.Where(h=>h.TargetType==type&&h.Method==method&&h.Postfix!=null))
                hook.PatchType.GetMethod(hook.Postfix,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{frame});
        }
    }
    static class EnemyRestorationRuntime
    {
        public static EnemyRestorationCatalog Catalog;
        public static bool CurrentViewsReady;
        public static Action<Frame> Prepare;
        public static void EnsureSimulationAssets(Frame frame){Prepare(frame);}
        public static bool TryGetNpc(Frame f,string key,out AssetRefNpcData npc)
        {
            var p=Catalog.Profiles.FirstOrDefault(x=>x.Key==key);npc=p==null?null:new AssetRefNpcData{Id=new(){Value=p.NpcGuid}};return p!=null;
        }
    }
}
