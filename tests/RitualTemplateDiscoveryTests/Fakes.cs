using System;
using System.Collections.Generic;

namespace Il2CppInterop.Runtime
{
    public static class Il2CppType { public static Type Of<T>() => typeof(T); }
}

namespace UnityEngine
{
    public class Component
    {
        public Transform transform;
        public T TryCast<T>() where T : class => this as T;
    }

    public class Transform : Component
    {
        private readonly List<Transform> children = new();
        public GameObject gameObject;
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
        public Transform AddChild(GameObject child)
        {
            children.Add(child.transform);
            return child.transform;
        }
    }

    public class GameObject
    {
        private readonly List<Component> components = new();
        public Transform transform;
        public bool activeSelf = true;
        public Exception ComponentFailure;
        public GameObject()
        {
            transform = new Transform { gameObject = this };
            transform.transform = transform;
        }
        public T Add<T>(T component) where T : Component
        {
            component.transform = transform;
            components.Add(component);
            return component;
        }
        public Component GetComponent(Type type)
        {
            if (ComponentFailure != null) throw ComponentFailure;
            foreach (var component in components) if (type.IsInstanceOfType(component)) return component;
            return null;
        }
    }
}

namespace Il2CppMoon.Forsaken
{
    public sealed class Data { public string Name; }
    public sealed class GuidValue { public long Value; }
    public sealed class DataAsset { public GuidValue Id = new(); }
    public sealed class QuantumAsset { public GuidValue AssetGuid = new(); }
    public class GeneralInteractableView : UnityEngine.Component
    {
        public Data Data = new();
        public DataAsset DataAsset = new();
        public bool IsBoundToEntity;
    }
    public class InteractionView : UnityEngine.Component { public QuantumAsset QuantumAsset; }
}

namespace CrucibleUnlock
{
    internal static partial class RitualViewRepair
    {
        internal static (int Count, Il2CppMoon.Forsaken.GeneralInteractableView First,
            Il2CppMoon.Forsaken.InteractionView Template) Discover(UnityEngine.Transform root)
        {
            int count = FindOfferingTemplate(root, out var first, out var template);
            return (count, first, template);
        }
    }
}
