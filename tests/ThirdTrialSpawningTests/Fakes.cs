using System.Reflection;
using System.Runtime.InteropServices;
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
    static class NativeRecords
    {
        static int next;
        public static readonly Dictionary<int,DynamicSpawnerData> Data=new();
        public static readonly Dictionary<int,RuntimeStreamingNode> Nodes=new();
        public static int Index()=>Interlocked.Increment(ref next);
    }
    struct DynamicSpawnerDataRef
    {
        public int m_index; public static int SIZE=>4;
        public DynamicSpawnerData Data {get=>NativeRecords.Data.GetValueOrDefault(m_index);set{m_index=NativeRecords.Index();NativeRecords.Data[m_index]=value;}}
        public DynamicSpawnerData Get(Frame f)=>Data;
    }
    class DynamicSpawnerComponent { public static int SIZE=>32; public DynamicSpawnerDataRef DynamicSpawnerData; }
    class DynamicNpcSpawnerComponent { public static int SIZE=>80; public DynamicSpawnerDataRef SpawnerData {get;set;} public AssetRefNpcData NpcData; }
    [StructLayout(LayoutKind.Explicit,Size=24)]
    struct IdentityComponent
    {
        [FieldOffset(0)] public long ScopeGen;
        [FieldOffset(8)] public EntityRef Scope;
        [FieldOffset(16)] public long Guid;
        public static int SIZE=>24;
    }
    static class NativeLayout { public static int GroupSize {get;set;}=8; }
    struct IntegrationGroupComponent { public int IntegrationId; public NodeRef StreamingNodeDataIndex; public static int SIZE=>NativeLayout.GroupSize; }
    struct NodeRef
    {
        public int m_index;
        public RuntimeStreamingNode Node {get=>NativeRecords.Nodes.GetValueOrDefault(m_index);set{m_index=NativeRecords.Index();NativeRecords.Nodes[m_index]=value;}}
        public RuntimeStreamingNode Get(Frame f)=>Node;
    }
    static unsafe class EntityRefExtensions
    { public static void* Get<T>(EntityRef entity,Frame frame)=>(void*)frame.RawPointer(entity,typeof(T)); }
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
        public static int GenericGets,NativeGets;
        public static readonly List<Frame> AllocatedFrames=new();
        public Frame()=>AllocatedFrames.Add(this);
        public long Source=ThirdTrialPolicy.PlaylistGuid;
        public bool ThrowSource {get;set;}
        public bool IsVerified {get;set;}=true;
        public bool ThrowNpcGet {get;set;}
        public bool NullDynamicGet {get;set;}
        public long PlayerCurrentChunk {get;set;}
        public readonly Dictionary<(long,Type),object> Components=new();
        public readonly List<string> ComponentReads=new();
        public readonly HashSet<(long,Type)> NullRawPointers=new();
        public readonly List<RuntimeEntry> Playlist=new();
        public IntegrationStreamingSystem Integration=new();
        public bool Has<T>(EntityRef entity) {ComponentReads.Add("Has:"+typeof(T).Name);return Components.ContainsKey((entity.Raw,typeof(T)));}
        public T Get<T>(EntityRef entity)
        {
            ComponentReads.Add("Get:"+typeof(T).Name);
            GenericGets++;
            if(typeof(T)==typeof(IntegrationGroupComponent)||typeof(T)==typeof(DynamicSpawnerComponent)||typeof(T)==typeof(DynamicNpcSpawnerComponent))
                throw new NullReferenceException("Modeled unsupported Frame.Get AOT specialization: "+typeof(T).Name);
            return (T)Components[(entity.Raw,typeof(T))];
        }
        readonly Dictionary<(long,Type),IntPtr> buffers=new();
        public IntPtr RawPointer(EntityRef entity,Type type)
        {
            ComponentReads.Add("NativeGet:"+type.Name);
            NativeGets++;
            if(NullRawPointers.Contains((entity.Raw,type)))return IntPtr.Zero;
            if(!Components.TryGetValue((entity.Raw,type),out var component))return IntPtr.Zero;
            if(NullDynamicGet&&type==typeof(DynamicSpawnerComponent))return IntPtr.Zero;
            if(ThrowNpcGet&&type==typeof(DynamicNpcSpawnerComponent))throw new InvalidOperationException("injected diagnostic read failure");
            int size=type==typeof(IdentityComponent)?24:type==typeof(IntegrationGroupComponent)?8:type==typeof(DynamicSpawnerComponent)?32:80;
            if(!buffers.TryGetValue((entity.Raw,type),out var ptr)) {ptr=Marshal.AllocHGlobal(size);buffers.Add((entity.Raw,type),ptr);}
            Marshal.Copy(new byte[size],0,ptr,size);
            if(type==typeof(IdentityComponent))Marshal.StructureToPtr((IdentityComponent)component,ptr,false);
            else if(type==typeof(IntegrationGroupComponent))Marshal.StructureToPtr((IntegrationGroupComponent)component,ptr,false);
            else if(type==typeof(DynamicSpawnerComponent))Marshal.StructureToPtr(((DynamicSpawnerComponent)component).DynamicSpawnerData,ptr,false);
            else Marshal.StructureToPtr(((DynamicNpcSpawnerComponent)component).NpcData,IntPtr.Add(ptr,16),false);
            return ptr;
        }
        public static void FreeAllBuffers()
        {foreach(var frame in AllocatedFrames)foreach(var ptr in frame.buffers.Values)Marshal.FreeHGlobal(ptr);AllocatedFrames.Clear();}
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
