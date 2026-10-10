using System;
using System.Collections.Generic;

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
        public IntPtr Pointer { get; } = new IntPtr(System.Threading.Interlocked.Increment(ref _next));
        public static readonly List<GameObject> Destroyed = new();
        public static void Destroy(GameObject obj) => Destroyed.Add(obj);
        public T TryCast<T>() where T : class => this as T;
    }
    public class Component : Object
    {
        public Transform transform;
    }
    public class Transform : Component
    {
        public Transform root;
        public Transform parent;
        public bool ThrowDuringSetParent;
        public GameObject gameObject;
        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (ThrowDuringSetParent) throw new InvalidOperationException("native SetParent failed");
            parent = value;
        }
    }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public string name;
        public Transform transform;
        public Il2CppMoon.Forsaken.EnemyWeaponView Weapon;
        public Component[] Children = Array.Empty<Component>();
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            transform.root = transform;
        }
        public Component GetComponent(Type type) => type == typeof(Il2CppMoon.Forsaken.EnemyWeaponView) ? Weapon : null;
        public Component[] GetComponentsInChildren(Type type, bool includeInactive) => Children;
        public void SetActive(bool value) => activeSelf = value;
    }
}
namespace Il2CppMoon.Playmode
{
    public class RuntimeAssetResolutionContext : UnityEngine.Object
    {
        public readonly Dictionary<long, Il2Cpp.AssetBase> Wrappers = new();
        public bool TryFindWrapperImpl(Il2CppQuantum.AssetGuid id, out Il2Cpp.AssetBase value, bool includeLoaded)
            => Wrappers.TryGetValue(id.Value, out value);
    }
}
namespace Il2CppMoon.Pooling
{
    public class PoolingSystem : UnityEngine.Object
    {
        public static PoolingSystem Active;
        public UnityEngine.GameObject NextClone;
        public UnityEngine.GameObject LastSource;
        public UnityEngine.Transform LastParent;
        public UnityEngine.GameObject CloneSync(UnityEngine.GameObject root, UnityEngine.Transform parent)
        {
            LastSource = root;
            LastParent = parent;
            return NextClone; // The separate-scene native branch ignores parent.
        }
    }
}
namespace Il2Cpp
{
    public class AssetBase : UnityEngine.Object
    {
        public Il2CppQuantum.AssetGuid AssetGuid;
        public new T TryCast<T>() where T : class => this as T;
    }
    public struct PrefabGuid { public ulong m_data1, m_data2; }
    public struct PrefabRef { public PrefabGuid Guid; }
    public class EnemyItemDataBaseAsset : AssetBase { }
    public class EnemyWeaponDataAsset : EnemyItemDataBaseAsset { public PrefabRef PrefabRef; }
    public class MeleeAttackDataAsset : AssetBase { public Il2CppMoon.Forsaken.ActionView View; }
}
namespace Il2CppMoon.Forsaken
{
    public class Core { public static Il2CppMoon.Playmode.RuntimeAssetResolutionContext AssetCtx; }
    public class ActionView : UnityEngine.Component { public Il2Cpp.AssetBase QuantumAsset; }
    public class EntityView : UnityEngine.Component
    {
        public Dictionary<Il2CppQuantum.AssetGuid, ActionView> m_actionViewMap = new();
        public void UnregisterActionView(ActionView view) => m_actionViewMap.Remove(view.QuantumAsset.AssetGuid);
        public void UnregisterActionView(Il2CppQuantum.AssetGuid guid) => m_actionViewMap.Remove(guid);
    }
    public class HumanoidEnemyView : EntityView
    {
        public HumanoidEnemyView() { transform = new UnityEngine.GameObject("keeper-owner").transform; }
        public EnemyWeaponView MainWeaponView;
        public bool ThrowDuringEquip;
        public bool ThrowAfterHandAssignment;
        public bool KeepFirstRegistration;
        public long SkipRegistrationGuid;
        public void OnItemViewInstantiated(UnityEngine.GameObject clone, Il2Cpp.EnemyItemDataBaseAsset item, bool mainHand)
        {
            var weapon = clone.Weapon;
            weapon.m_enemyView = this;
            weapon.m_meleeEffects[0].Init(clone.transform.parent?.Pointer == transform.Pointer ? this : null);
            for (int i = 0; i < weapon.m_actionViews.Count; ++i)
                if (weapon.m_actionViews[i].QuantumAsset.AssetGuid.Value != SkipRegistrationGuid)
                {
                    var guid = weapon.m_actionViews[i].QuantumAsset.AssetGuid;
                    if (!KeepFirstRegistration || !m_actionViewMap.ContainsKey(guid))
                        m_actionViewMap[guid] = weapon.m_actionViews[i];
                }
            weapon.RegisteredActions = 7;
            if (ThrowDuringEquip) throw new InvalidOperationException("partial native Equip failure");
            MainWeaponView = weapon;
            if (ThrowAfterHandAssignment) throw new InvalidOperationException("late native callback failure");
        }
    }
    public class WeaponDataRef { public Il2CppQuantum.AssetGuid Id; }
    public class EnemyWeaponView : UnityEngine.Component
    {
        public EnemyWeaponView() { m_meleeEffects.Add(new Effects.MeleeEffects()); }
        public WeaponDataRef WeaponData = new();
        public EntityView m_enemyView;
        public List<ActionView> m_actionViews = new();
        public List<Effects.MeleeEffects> m_meleeEffects = new();
        public Il2Cpp.WwiseWeaponCompanion AudioCompanion = new();
        public int RegisteredActions;
        public int UnequipCalls;
        public bool ThrowFirstUnequip;
        public void OnInstantiatedFromPool() { }
        public void Unequip()
        {
            ++UnequipCalls;
            if (ThrowFirstUnequip && UnequipCalls == 1)
            {
                for (int i = 0; i < 3; ++i)
                    m_enemyView.UnregisterActionView(m_actionViews[i]);
                RegisteredActions = 4; // Native loop removed three, then threw.
                throw new InvalidOperationException("partial native Unequip failure");
            }
            for (int i = 0; i < m_actionViews.Count; ++i)
                m_enemyView.UnregisterActionView(m_actionViews[i]);
            RegisteredActions = 0;
            m_enemyView = null;
        }
    }
}
namespace Il2CppMoon.Forsaken.Effects
{
    public class MeleeEffects : UnityEngine.Component
    {
        public Il2CppMoon.Forsaken.EntityView m_characterView;
        public bool Initialized;
        public void Init(Il2CppMoon.Forsaken.EntityView owner)
        {
            if (Initialized) return;
            m_characterView = owner;
            Initialized = true;
        }
    }
}
namespace Il2Cpp
{
    public class WwiseWeaponCompanion : UnityEngine.Object
    {
        public UnityEngine.Object WeaponSoundHost = new();
    }
}
namespace CrucibleUnlock
{
    public class EnemyRestorationProfile
    {
        public int Index;
        public string Key;
        public long WeaponGuid, ViewGuid, NpcGuid;
    }
    public class EnemyRestorationCatalog { public EnemyRestorationProfile[] Profiles; }
    public static class EnemyRestorationRuntime { public static bool CurrentViewsReady = true; }
    public static class EnemyRestorationOwnerScope
    {
        public static bool TryGetProfile(Il2CppMoon.Forsaken.EntityView view, EnemyRestorationCatalog catalog,
            out EnemyRestorationProfile profile) { profile = catalog.Profiles[3]; return true; }
    }
    public static class TrialRepairLog { public static readonly List<string> Messages = new(); public static void Write(string text) => Messages.Add(text); }
}
