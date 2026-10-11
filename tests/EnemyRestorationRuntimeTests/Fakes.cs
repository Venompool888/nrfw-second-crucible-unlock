using System.Reflection;
using CrucibleUnlock;
using Il2CppQuantum;

// These types are compiled into this test executable. No referenced or loaded
// IL2CPP, Unity, Harmony or game assembly is required.
namespace FakeRuntime
{
    static class Reads
    {
        public static int Unity;
        public static bool Forbidden;
        public static void Read() { ++Unity; if (Forbidden) throw new InvalidOperationException("Unity read while disabled"); }
    }
    class NativeObject
    {
        static long next;
        readonly IntPtr pointer = new(Interlocked.Increment(ref next));
        public IntPtr Pointer { get { Reads.Read(); return pointer; } }
        public T TryCast<T>() where T : class { Reads.Read(); return this as T; }
    }
}
namespace HarmonyLib
{
    class Harmony
    {
        public string Id;
        public Harmony(string id) { Id = id; }
        public void UnpatchSelf() => TrialRepairLog.Hooks.RemoveAll(h => h.Owner == Id);
    }
}
namespace Il2Cpp
{
    class IAssetWrapperResolver : FakeRuntime.NativeObject { }
    class RuntimeAssetResolutionContext : IAssetWrapperResolver
    {
        public readonly Dictionary<AssetGuid, AssetObject> m_assetByGuid = new();
        public readonly Dictionary<AssetGuid, FakeWrapper> m_loadedWrappers = new();
        public void Update() { } // Legacy R3 registration can resolve, but host never dispatches it.
    }
    sealed class FakeWrapper
    {
        public AssetGuid Guid;
        public AssetObject AssetObject { get { FakeRuntime.Reads.Read(); return Global.Resolve?.Invoke(Guid); } }
    }
    static class Global { public static Func<AssetGuid, AssetObject> Resolve; }
}
namespace Il2CppMoon.Forsaken
{
    static class Core
    {
        static Il2CppQuantum.FakeResourceManager manager;
        public static Queue<Il2CppQuantum.FakeResourceManager> ScriptedManagers;
        public static Il2CppQuantum.FakeResourceManager ResourceManager
        {
            get { FakeRuntime.Reads.Read(); if (ScriptedManagers?.Count > 0) manager = ScriptedManagers.Dequeue(); return manager; }
            set { manager = value; ScriptedManagers = null; }
        }
        static Il2Cpp.IAssetWrapperResolver current;
        public static Queue<Il2Cpp.IAssetWrapperResolver> ScriptedReads;
        public static Il2Cpp.IAssetWrapperResolver AssetCtx
        {
            get
            {
                FakeRuntime.Reads.Read();
                if (ScriptedReads?.Count > 0) current = ScriptedReads.Dequeue();
                return current;
            }
            set { current = value; ScriptedReads = null; }
        }
    }
}
namespace Il2CppMoon.TheLoop
{
    class ViewFrame { }
    static class UpdateAsyncQuantumViewAssetLoading { public static void Tick(ViewFrame vf) { } }
}
namespace Il2CppMoon.Playmode
{
    class InitializeQuantumAssetDatabase { public static void InitializeResourceManager(Il2CppMoon.TheLoop.ViewFrame vf) { } }
}
namespace Il2CppQuantum
{
    struct AssetGuid { public long Value; }
    struct AssetRefNpcData { public AssetGuid Id; }
    struct AssetRefEntityData { public AssetGuid Id { get; set; } }
    struct AssetRefControllerData { public AssetGuid Id { get; set; } }
    class AssetObject : FakeRuntime.NativeObject { public AssetGuid Guid; public string Path; }
    class NpcData : AssetObject { public AssetRefEntityData EntityDataAssetRef { get; set; } public AssetRefControllerData ControllerDataAssetRef { get; set; } }
    class EntityViewData : AssetObject { }
    class EnemyWeaponData : AssetObject { }
    class DynamicAssetDatabase
    {
        public readonly Dictionary<long,AssetObject> Assets = new();
        public AssetObject FindAsset(AssetGuid guid) => Assets.GetValueOrDefault(guid.Value);
    }
    class FakeResourceManager : FakeRuntime.NativeObject { }
    class FakeContext { public FakeResourceManager ResourceManager = new(); }
    class ViewStorage { public readonly Dictionary<long,int> m_lookup = new(); public readonly List<EntityViewData> m_storage = new(); }
    class InstanceDataStorage { public readonly ViewStorage m_storageEntityViewData = new(); }
    class Frame { public DynamicAssetDatabase DynamicAssetDB = new(); public FakeContext Context = new(); public InstanceDataStorage InstanceDataStorage = new(); }
    class InitializeContentSystem { public void OnInit(Frame frame) { } }
}
namespace CrucibleUnlock
{
    static class BuildGuard
    {
        public static int Checks;
        public static void VerifyEnemyDatabase(string path) { ++Checks; }
    }
    class EnemyRestorationNativeBackend { public Frame Frame; public EnemyRestorationNativeBackend(Frame frame) { Frame = frame; } }
    static class EnemyRestorationRegistration
    {
        public static List<AssetObject> Register(EnemyRestorationCatalog catalog, EnemyRestorationNativeBackend backend)
        {
            foreach (var p in catalog.Profiles)
            {
                if (!backend.Frame.DynamicAssetDB.Assets.ContainsKey(p.ViewGuid))
                    backend.Frame.DynamicAssetDB.Assets.Add(p.ViewGuid, new EntityViewData { Guid = new() { Value = p.ViewGuid }, Path = p.Path("view") });
                if (p.BlockedActions.Length > 0 && !backend.Frame.DynamicAssetDB.Assets.ContainsKey(p.PrivateWeaponGuid))
                    backend.Frame.DynamicAssetDB.Assets.Add(p.PrivateWeaponGuid, new EnemyWeaponData { Guid = new() { Value = p.PrivateWeaponGuid }, Path = p.Path("weapon") });
            }
            return backend.Frame.DynamicAssetDB.Assets.Values.ToList();
        }
    }
    static class EnemyRestorationActionHooks
    {
        public static int Installs;
        public static void Install(HarmonyLib.Harmony owner) { ++Installs; }
    }
    static class EnemyRestorationVoiceRepair
    {
        public static void Install(HarmonyLib.Harmony owner) { }
        public static void ResumeAfterViewPublication() { }
        public static void StopWithoutUnityReads() { }
    }
    static class EnemyRestorationKeeperWeaponViews
    {
        public static bool Ready = true;
        public static int Calls, Stops;
        public static bool Ensure(Il2Cpp.RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog)
        { FakeRuntime.Reads.Read(); ++Calls; return Ready; }
        public static void StopWithoutUnityReads() { ++Stops; }
    }
    static class EnemyRestorationReactionViews
    {
        public static bool Ready = true;
        public static int Calls, Stops;
        public static bool Ensure(Il2Cpp.RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog)
        { FakeRuntime.Reads.Read(); ++Calls; return Ready; }
        public static void StopWithoutUnityReads() { ++Stops; }
    }
    static class EnemyRestorationAudioDiagnostics
    {
        public static int Stops;
        public static void StopWithoutUnityReads() { ++Stops; }
    }
    static class EnemyRestorationWeaponViews
    {
        public static bool Ready;
        public static int Calls;
        public static EnemyWeaponData[] LastWeapons;
        public static bool Ensure(Il2Cpp.RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog, EnemyWeaponData[] weapons)
        {
            FakeRuntime.Reads.Read(); ++Calls; LastWeapons = weapons;
            if (!Ready) return false;
            for (int i = 0; i < catalog.Profiles.Length; ++i)
            {
                var p = catalog.Profiles[i];
                if (p.BlockedActions.Length == 0) continue;
                var guid = new AssetGuid { Value = p.PrivateWeaponGuid };
                if (!ctx.m_assetByGuid.ContainsKey(guid)) ctx.m_assetByGuid.Add(guid, weapons[i]);
                if (!ctx.m_loadedWrappers.ContainsKey(guid)) ctx.m_loadedWrappers.Add(guid, new Il2Cpp.FakeWrapper { Guid = guid });
            }
            return true;
        }
    }
    static class EnemyRestorationViews
    {
        public static bool LutReady;
        public static bool ThrowEnsure;
        public static int Calls;
        public static Il2Cpp.RuntimeAssetResolutionContext LastContext;
        public static EntityViewData[] LastViews;
        // Legacy overload only exists so the frozen R4 source remains runnable.
        public static bool Ensure(Il2Cpp.RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog)
            => Ensure(ctx, catalog, null);
        public static bool Ensure(Il2Cpp.RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog, EntityViewData[] views)
        {
            FakeRuntime.Reads.Read(); ++Calls; LastContext = ctx; LastViews = views;
            if (ThrowEnsure) throw new InvalidOperationException("Injected strict view validation failure");
            if (!LutReady) return false;
            for (int i = 0; i < catalog.Profiles.Length; i++)
            {
                var p = catalog.Profiles[i];
                var guid = new AssetGuid { Value = p.ViewGuid };
                if (!ctx.m_assetByGuid.ContainsKey(guid)) ctx.m_assetByGuid.Add(guid, views == null ? new EntityViewData { Guid = guid, Path = p.Path("view") } : views[i]);
                if (!ctx.m_loadedWrappers.ContainsKey(guid)) ctx.m_loadedWrappers.Add(guid, new Il2Cpp.FakeWrapper { Guid = guid });
            }
            return true;
        }
    }
    static class TrialRepairLog
    {
        public record Hook(string Owner, Type TargetType, string Method, Type[] Args, MethodInfo Prefix, MethodInfo Postfix);
        public static readonly List<Hook> Hooks = new();
        public static readonly List<string> Messages = new();
        public static void Write(string value) => Messages.Add(value);
        public static void Patch(HarmonyLib.Harmony owner, Type type, string method, Type[] args, Type patchType,
            string prefix = null, string postfix = null, Type returnType = null)
        {
            var target = type.GetMethod(method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
            if (target == null || target.ReturnType != (returnType ?? typeof(void))) throw new InvalidOperationException("Fake target ABI mismatch");
            MethodInfo Find(string name) => name == null ? null : patchType.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Fake callback absent");
            Hooks.Add(new Hook(owner.Id, type, method, args, Find(prefix), Find(postfix)));
        }
        public static void ResetResourceManager()
        {
            var hook = Hooks.Single(h => h.TargetType == typeof(Il2CppMoon.Playmode.InitializeQuantumAssetDatabase) &&
                h.Method == "InitializeResourceManager" && h.Args.SequenceEqual(new[] { typeof(Il2CppMoon.TheLoop.ViewFrame) }));
            if (hook.Prefix != null) Invoke(hook.Prefix, new Il2CppMoon.TheLoop.ViewFrame());
            Il2CppMoon.Playmode.InitializeQuantumAssetDatabase.InitializeResourceManager(new());
            if (hook.Postfix != null) Invoke(hook.Postfix, new Il2CppMoon.TheLoop.ViewFrame());
        }
        static void Invoke(MethodInfo method, object argument)
        { method.Invoke(null, method.GetParameters().Length == 0 ? Array.Empty<object>() : new[] { argument }); }
    }
}
