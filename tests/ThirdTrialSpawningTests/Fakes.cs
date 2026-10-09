using System.Reflection;
using CrucibleUnlock;
using Il2CppQuantum;
using Il2CppQuantum.StreamingGraph;

namespace HarmonyLib { class Harmony { public string Id; public Harmony(string id) {Id=id;} public void UnpatchSelf() {} } }
namespace Il2CppPhoton.Deterministic
{
    struct FP { public long RawValue {get;set;} }
    struct FPVector3 { public FP X {get;set;} public FP Y {get;set;} public FP Z {get;set;} }
}
namespace Il2CppQuantum
{
    struct AssetGuid { public long Value; }
    struct AssetRefNpcData { public AssetGuid Id; }
    struct EntityRef { public long Raw; }
    enum EnemyDifficulty { Normal }
    class DynamicNpcSpawnerData
    {
        public AssetGuid Guid;
        public EntityRef Spawn(Frame f,EntityRef spawner,AssetRefNpcData npc,Il2CppPhoton.Deterministic.FPVector3 position,
            Il2CppPhoton.Deterministic.FP rotation,EnemyDifficulty difficulty,long patrolRouteId,string faderGroupId) => default;
    }
    class DynamicSpawnerData { public long Guid; }
    class DynamicSpawnerDataRef { public DynamicSpawnerData Data; public DynamicSpawnerData Get(Frame f) => Data; }
    class DynamicSpawnerComponent { public DynamicSpawnerDataRef DynamicSpawnerData=new(); }
    class DynamicNpcSpawnerComponent { public DynamicSpawnerDataRef SpawnerData=new(); public AssetRefNpcData NpcData; }
    class IdentityComponent { public EntityRef Scope; }
    class IntegrationGroupComponent { public NodeRef StreamingNodeDataIndex=new(); public int IntegrationId; }
    class NodeRef { public RuntimeStreamingNode Node; public RuntimeStreamingNode Get(Frame f) => Node; }
    class IntegrationStreamingSystem
    {
        public readonly Dictionary<int,RuntimeStreamingNode> Nodes=new();
        public RuntimeStreamingNode TryGetIntegrationNodeById(Frame f,int id) => Nodes.GetValueOrDefault(id);
    }
    struct FakeBool { public bool Value; public static explicit operator bool(FakeBool b)=>b.Value; }
    class NuggetPointer { public FakeBool Ptr=new(){Value=true}; public List<AssetRefNuggetData> Values=new(); }
    struct AssetRefNuggetData { public AssetGuid Id; }
    class RuntimeEntry { public AssetRefNuggetData Chunk; public NuggetPointer Nuggets=new(); }
    class Frame
    {
        public long Source=ThirdTrialPolicy.PlaylistGuid;
        public bool ThrowSource {get;set;}
        public bool IsVerified {get;set;}=true;
        public bool ThrowNpcGet {get;set;}
        public long PlayerCurrentChunk {get;set;}
        public readonly Dictionary<(long,Type),object> Components=new();
        public readonly List<string> ComponentReads=new();
        public readonly List<RuntimeEntry> Playlist=new();
        public IntegrationStreamingSystem Integration=new();
        public bool Has<T>(EntityRef entity) {ComponentReads.Add("Has:"+typeof(T).Name);return Components.ContainsKey((entity.Raw,typeof(T)));}
        public T Get<T>(EntityRef entity) {ComponentReads.Add("Get:"+typeof(T).Name);if(ThrowNpcGet&&typeof(T)==typeof(DynamicNpcSpawnerComponent))throw new InvalidOperationException("injected diagnostic read failure");return (T)Components[(entity.Raw,typeof(T))];}
        public T GetSystem<T>() where T:class=>Integration as T;
        public List<AssetRefNuggetData> ResolveList(NuggetPointer pointer)=>pointer.Values;
    }
    static class InfiniteDungeonAPI
    {
        public static AssetRefNuggetData GetSourcePlaylist(Frame f)
        { if(f.ThrowSource)throw new InvalidOperationException("no source"); return new(){Id=new(){Value=f.Source}}; }
        public static List<RuntimeEntry> GetRuntimePlaylist(Frame f)=>f.Playlist;
    }
}
namespace Il2CppQuantum.StreamingGraph
{
    enum StreamingNodeKind { Root,World,Area,Chunk,Bucket,Nugget }
    class RuntimeStreamingNode { public AssetGuid Id; public EntityRef Entity; public StreamingNodeKind Kind; public RuntimeStreamingNode Parent; }
}
namespace CrucibleUnlock
{
    static class EnemyRestorationRuntime { public static readonly EnemyRestorationCatalog Catalog=EnemyRestorationCatalog.Load(); }
    static class ThirdTrialRuntime
    {
        public static bool Ready=true;
        public static readonly List<string> Failures=new();
        public static readonly List<string> Lookups=new();
        public static bool IsFrameReady(Frame f)=>Ready;
        public static bool TryGetNpc(Frame f,string key,out AssetRefNpcData npc)
        {Lookups.Add(key);npc=new(){Id=new(){Value=Replacement(key)}};return Ready;}
        public static long Replacement(string key)=>EnemyRestorationRuntime.Catalog.Profiles.Single(p=>p.Key==key).NpcGuid;
        public static void Fail(string reason)=>Failures.Add(reason);
    }
    static class TrialRepairLog
    {
        public static MethodInfo Callback;
        public static MethodInfo PostCallback;
        public static readonly List<string> Messages=new();
        public static void Write(string message)=>Messages.Add(message);
        public static void Patch(HarmonyLib.Harmony owner,Type type,string method,Type[] args,Type patchType,string prefix=null,string postfix=null,Type returnType=null)
        {
            var target=type.GetMethod(method,args);
            if(target==null||target.ReturnType!=returnType)throw new Exception("exact native Spawn signature mismatch");
            Callback=patchType.GetMethod(prefix,BindingFlags.Static|BindingFlags.NonPublic);
            PostCallback=postfix==null?null:patchType.GetMethod(postfix,BindingFlags.Static|BindingFlags.NonPublic);
        }
    }
}
