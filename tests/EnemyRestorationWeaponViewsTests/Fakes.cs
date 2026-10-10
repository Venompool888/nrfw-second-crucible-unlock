using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppQuantum;
using Il2CppMoon;
using Il2CppMoon.Pooling;

public class FakeNative
{
    private static long next;
    public IntPtr Pointer { get; set; } = new IntPtr(++next);
    public T TryCast<T>() where T : class => this as T;
}
public class FakeDictionary<K,V> : FakeNative
{
    private readonly Dictionary<K,V> items = new Dictionary<K,V>();
    public int FailAfterAdd;
    public int Count => items.Count;
    public V this[K key] { get => items[key]; set => items[key] = value; }
    public bool ContainsKey(K key) => items.ContainsKey(key);
    public bool TryGetValue(K key, out V value) => items.TryGetValue(key, out value);
    public bool Remove(K key) => items.Remove(key);
    public void Add(K key,V value) { items.Add(key,value); if(FailAfterAdd>0 && --FailAfterAdd==0) throw new InvalidOperationException("Injected post-add failure"); }
}
public class FakeList<T> : FakeNative
{
    private readonly List<T> items=new List<T>();
    public int FailAfterAdd;
    public int Count=>items.Count;
    public T this[int index] {get=>items[index];set=>items[index]=value;}
    public void RemoveAt(int index)=>items.RemoveAt(index);
    public void Add(T value) {items.Add(value);if(FailAfterAdd>0 && --FailAfterAdd==0) throw new InvalidOperationException("Injected post-add failure");}
}
namespace UnityEngine
{
    public enum HideFlags {DontSave}
    public class Object:FakeNative {public string name;public HideFlags hideFlags;public static int Destroyed; public static void Destroy(Object value) {++Destroyed;}}
    public class Component:Object {}
    public class GameObject:Object
    {
        public Component Component,Child;
        public Component GetComponent(Type type)=>type.IsInstanceOfType(Component)?Component:null;
    }
    public class ScriptableObject:Object
    {
        public static int Created;
        public static T CreateInstance<T>() where T:ScriptableObject,new() {++Created;return new T();}
    }
}
namespace Il2CppInterop.Runtime {public static class Il2CppType {public static Type Of<T>()=>typeof(T);}}
namespace Il2CppQuantum
{
    public struct AssetGuid {public long Value;}
    public class AssetObject:FakeNative
    {
        private AssetGuid guid; private string path;public int Writes;
        public AssetGuid Guid {get=>guid;set{guid=value;++Writes;}}
        public string Path {get=>path;set{path=value;++Writes;}}
    }
    public class EnemyWeaponData:AssetObject {public static int Created;public EnemyWeaponData(){++Created;}}
    public interface IAssetResolutionContext {AssetObject FindAsset(AssetGuid guid);}
}
namespace Il2CppMoon {public struct SerializableGuid {public ulong m_data1,m_data2;}}
namespace Il2CppMoon.Pooling
{
    public struct PooledPrefabRef {public SerializableGuid Guid;}
    public class PoolingSystem:FakeNative
    {
        public static PoolingSystem Active;
        public class PrefabData {public UnityEngine.GameObject RootInstance,Asset;public string LoadKey; public PooledPrefabRef Prefab;}
        public class Op {public PrefabData Data;public UnityEngine.GameObject Result=>Data.Asset;}
        public PrefabData[] m_prefabs;
        public bool Done=true;
        public int Loads;
        public bool TryResolvePrefab(PooledPrefabRef prefab,out int index)
        {
            index=Array.FindIndex(m_prefabs,p=>p.Prefab.Guid.m_data1==prefab.Guid.m_data1 && p.Prefab.Guid.m_data2==prefab.Guid.m_data2);
            return index>=0;
        }
        public Op LoadOpCreate(string key) {++Loads;return new Op{Data=Array.Find(m_prefabs,p=>p.LoadKey==key)};}
        public bool LoadOpDone(Op op)=>Done;
    }
}
namespace Il2Cpp
{
    public class EnemyWeaponView:UnityEngine.Component {}
    public class AssetBase:UnityEngine.ScriptableObject
    {
        public AssetGuid AssetGuid;
        public static Func<AssetGuid,AssetObject> Resolve;
        public AssetObject AssetObject=>Resolve(AssetGuid);
    }
    public class EnemyWeaponDataAsset:AssetBase
    {
        public PooledPrefabRef PrefabRef;
        public EnemyWeaponData Get(IAssetResolutionContext ctx)=>(EnemyWeaponData)ctx.FindAsset(AssetGuid);
    }
    public class RuntimeAssetResolutionContext:FakeNative,IAssetResolutionContext
    {
        public FakeDictionary<AssetGuid,AssetObject> m_assetByGuid=new FakeDictionary<AssetGuid,AssetObject>();
        public FakeDictionary<AssetGuid,AssetBase> m_loadedWrappers=new FakeDictionary<AssetGuid,AssetBase>();
        public FakeDictionary<AssetGuid,object> m_wrappersBeingLoaded=new FakeDictionary<AssetGuid,object>();
        public FakeList<AssetObject> m_allAssets=new FakeList<AssetObject>();
        public Dictionary<long,AssetBase> Sources=new Dictionary<long,AssetBase>();
        public bool TryFindWrapperImpl(AssetGuid guid,out AssetBase source,bool load)=>Sources.TryGetValue(guid.Value,out source);
        public T Cast<T>()=>(T)(object)this;
        public AssetObject FindAsset(AssetGuid guid)=>m_assetByGuid.TryGetValue(guid,out var asset)?asset:null;
    }
}
