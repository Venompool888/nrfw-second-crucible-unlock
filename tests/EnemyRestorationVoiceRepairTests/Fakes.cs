using System.Reflection;
using System.Runtime.InteropServices;

namespace HarmonyLib { internal sealed class Harmony { } }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    internal sealed class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        internal Il2CppReferenceArray(params T[] items) { _items = items; }
        public int Length => _items.Length;
        public T this[int index] => _items[index];
    }
}
namespace Il2CppMoon.Wwise.MoonWwiseTypes
{
    internal sealed class SwitchReference
    {
        public IntPtr Pointer { get; set; }
        public string Guid { get; set; }
        public bool Valid { get; set; } = true;
        public bool IsValid() => Valid;
        public SwitchReference(long pointer, string guid) { Pointer = new IntPtr(pointer); Guid = guid; }
    }
}
namespace Il2CppQuantum
{
    internal enum CharacterType { }
    internal struct EntityRef { public ulong Raw; }
    internal sealed class NpcAttachmentComponent { public const int SIZE = 144; }
    internal sealed class ResourceManager { public IntPtr Pointer; }
    internal sealed class FrameContext { public ResourceManager ResourceManager; }
    internal sealed class Frame
    {
        public FrameContext Context = new() { ResourceManager = new() { Pointer = new IntPtr(1) } };
        public int AttachedType;
        public bool HasComponent = true;
        public bool Has<T>(EntityRef entity) => HasComponent;
    }
    internal static class EntityRefExtensions
    {
        public static unsafe void* Get<T>(EntityRef entity, Frame frame)
        {
            var pointer = (int*)Marshal.AllocHGlobal(sizeof(int));
            *pointer = frame.AttachedType;
            return pointer;
        }
    }
}
namespace Il2CppMoon.Forsaken
{
    internal class EntityView
    {
        public IntPtr Pointer { get; set; }
        public Il2CppQuantum.EntityRef EntityRef { get; set; }
        public Il2CppQuantum.Frame PredictedFrame { get; set; }
        public SoundHost PrimarySoundHost { get; set; }
        public void OnDestroy() { }
        public T TryCast<T>() where T : class => this as T;
    }
    internal sealed class SoundHost { public IntPtr Pointer { get; set; } = new IntPtr(9); }
    internal class CharacterView : EntityView
    {
        private Il2CppMoon.Wwise.MoonWwiseTypes.SwitchReference _voice;
        public static int VoiceReads;
        public Il2CppMoon.Wwise.MoonWwiseTypes.SwitchReference CustomVoiceSwitch
        { get { ++VoiceReads; return _voice; } set { _voice = value; } }
        public bool Proof = true;
        public CrucibleUnlock.EnemyRestorationProfile Profile;
        private void FulfillVoiceAssignment() { }
        protected void OnUnboundFromEntity() { }
        public void OnReturnedToPool() { }
    }
}
namespace Il2Cpp
{
    internal sealed class WwiseVoiceSelectionPool
    {
        public IntPtr Pointer = new IntPtr(5);
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppMoon.Wwise.MoonWwiseTypes.SwitchReference> VoiceSwitchSelects;
        public int Calls;
        public Il2CppMoon.Wwise.MoonWwiseTypes.SwitchReference GetRandomVoiceFromPool()
        { return VoiceSwitchSelects[Calls++ % VoiceSwitchSelects.Length]; }
    }
    internal sealed class WwiseDefinitionsVoices
    {
        public Dictionary<int,WwiseVoiceSelectionPool> Pools = new();
        public WwiseVoiceSelectionPool GetCharacterVoicePool(Il2CppQuantum.CharacterType type) => Pools.GetValueOrDefault((int)type);
    }
    internal sealed class WwiseGlobalDefinitions
    {
        public static WwiseGlobalDefinitions Instance = new();
        public WwiseDefinitionsVoices Voices = new();
    }
}
namespace CrucibleUnlock
{
    internal sealed class EnemyRestorationProfile { public int Index, CharacterType; public string Key; public long NpcTemplateGuid; }
    internal sealed class EnemyRestorationCatalog { public EnemyRestorationProfile[] Profiles; }
    internal static class EnemyRestorationRuntime { public static bool CurrentViewsReady = true; public static EnemyRestorationCatalog Catalog; }
    internal static class EnemyRestorationOwnerScope
    {
        public static bool TryGetProfile(Il2CppMoon.Forsaken.EntityView view, EnemyRestorationCatalog catalog, out EnemyRestorationProfile profile)
        { var character = (Il2CppMoon.Forsaken.CharacterView)view; profile = character.Proof ? character.Profile : null; return profile != null; }
    }
    internal static class TrialRepairLog
    {
        public static List<string> Logs = new();
        public static List<string> Hooks = new();
        public static void Write(string value) => Logs.Add(value);
        public static void Patch(HarmonyLib.Harmony owner, Type type, string name, Type[] args, Type callback, string prefix=null, string postfix=null, Type returnType=null)
        {
            var method = type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null);
            if(method == null || method.ReturnType != (returnType??typeof(void))) throw new Exception("Invalid hook target " + name);
            if(prefix != null && callback.GetMethod(prefix,BindingFlags.NonPublic|BindingFlags.Static)==null) throw new Exception("Missing prefix");
            if(postfix != null && callback.GetMethod(postfix,BindingFlags.NonPublic|BindingFlags.Static)==null) throw new Exception("Missing postfix");
            Hooks.Add(type.Name + "." + name);
        }
    }
}
