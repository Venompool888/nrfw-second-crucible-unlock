using System;
using System.Collections.Generic;
using System.Threading;

namespace Il2CppInterop.Runtime
{
    public static class Il2CppType { public static Type Of<T>() => typeof(T); }
}
namespace Il2CppQuantum
{
    public struct AssetGuid { public long Value; }
}
namespace UnityEngine
{
    public class Object
    {
        private static long _next;
        public IntPtr Pointer { get; } = new IntPtr(Interlocked.Increment(ref _next));
        public T TryCast<T>() where T : class => this as T;
    }
    public class Component : Object { }
    public class GameObject : Object
    {
        public Il2Cpp.EnemyWeaponView Weapon;
        public int ComponentReads;
        public Component GetComponent(Type type)
        {
            ++ComponentReads;
            return type == typeof(Il2Cpp.EnemyWeaponView) ? Weapon : null;
        }
    }
}
namespace Il2CppMoon.Wwise
{
    public class SoundHost : UnityEngine.Object { }
    public class DialogueEventReference : UnityEngine.Object { }
}
namespace Il2Cpp
{
    public class EnemyItemDataBaseAsset : UnityEngine.Object
    {
        public Il2CppQuantum.AssetGuid AssetGuid;
    }
    public class WwiseWeaponCompanion : UnityEngine.Object
    {
        public Il2CppMoon.Wwise.SoundHost WeaponSoundHost;
        public Il2CppMoon.Forsaken.EntityView Owner;
        public Il2CppMoon.Wwise.DialogueEventReference AttackEvent;
    }
    public class AssetRefEnemyWeaponData { public Il2CppQuantum.AssetGuid Id; }
    public class EnemyWeaponView : UnityEngine.Component
    {
        public AssetRefEnemyWeaponData WeaponData = new();
        public WwiseWeaponCompanion AudioCompanion;
    }
}
namespace Il2CppMoon.Forsaken
{
    public class EntityView : UnityEngine.Component
    {
        public Il2CppMoon.Wwise.SoundHost PrimarySoundHost;
    }
    public class HumanoidEnemyView : EntityView
    {
        public Il2Cpp.EnemyWeaponView MainWeaponView;
    }
}
namespace CrucibleUnlock
{
    public sealed class EnemyRestorationProfile
    {
        public int Index;
        public string Key;
        public long WeaponGuid, PrivateWeaponGuid;
        public long[] BlockedActions;
    }
    public sealed class EnemyRestorationCatalog { public EnemyRestorationProfile[] Profiles; }
    public static class EnemyRestorationRuntime { public static bool CurrentViewsReady = true; }
    public static class EnemyRestorationOwnerScope
    {
        public static readonly Dictionary<IntPtr, EnemyRestorationProfile> Owners = new();
        public static int Reads;
        public static bool Throw;
        public static bool TryGetProfile(Il2CppMoon.Forsaken.EntityView owner, EnemyRestorationCatalog catalog,
            out EnemyRestorationProfile profile)
        {
            ++Reads;
            if (Throw) throw new InvalidOperationException("fake scope read failed");
            return Owners.TryGetValue(owner.Pointer, out profile);
        }
    }
    public static class TrialRepairLog
    {
        public static readonly List<string> Messages = new();
        public static void Write(string text) => Messages.Add(text);
    }
}
