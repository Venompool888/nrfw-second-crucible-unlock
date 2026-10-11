using System;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppQuantum;
using Il2CppMoon;
using Il2CppMoon.Pooling;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);++checks;}
    private static void Throws(Action act,string name)
    {
        try {act();}catch(InvalidOperationException){++checks;return;}catch(AggregateException){++checks;return;}
        throw new Exception("Expected failure: "+name);
    }
    private sealed class Fixture
    {
        public RuntimeAssetResolutionContext Ctx=new RuntimeAssetResolutionContext();
        public EnemyRestorationCatalog Catalog=new EnemyRestorationCatalog {Profiles=new EnemyRestorationProfile[10]};
        public EnemyWeaponData[] Weapons=new EnemyWeaponData[10];
        public Fixture()
        {
            var pool=new PoolingSystem {m_prefabs=new PoolingSystem.PrefabData[3]};
            PoolingSystem.Active=pool;
            AssetBase.Resolve=g=>Ctx.FindAsset(g);
            int slot=0;
            for(int i=0;i<10;++i)
            {
                var p=new EnemyRestorationProfile {Index=i,Key="profile"+i,WeaponGuid=1000+i,BlockedActions=i==1||i==8||i==9?new long[]{200}:Array.Empty<long>()};
                Catalog.Profiles[i]=p;
                if(p.BlockedActions.Length==0)continue;
                Weapons[i]=new EnemyWeaponData{Guid=new AssetGuid{Value=p.PrivateWeaponGuid},Path=p.Path("weapon")};
                var prefab=new PooledPrefabRef{Guid=new SerializableGuid{m_data1=(ulong)(10+slot),m_data2=(ulong)(20+slot)}};
                Ctx.Sources.Add(p.WeaponGuid,new EnemyWeaponDataAsset{AssetGuid=new AssetGuid{Value=p.WeaponGuid},PrefabRef=prefab});
                pool.m_prefabs[slot]=new PoolingSystem.PrefabData{Prefab=prefab,LoadKey="weapon"+slot,Asset=new GameObject{Component=new EnemyWeaponView()}};
                ++slot;
            }
        }
        public bool Ensure()=>EnemyRestorationWeaponViews.Ensure(Ctx,Catalog,Weapons);
        public void Empty(string name)=>Check(Ctx.m_assetByGuid.Count==0 && Ctx.m_loadedWrappers.Count==0 && Ctx.m_allAssets.Count==0,name);
    }
    public static void Main()
    {
        var f=new Fixture();int created=EnemyWeaponData.Created;
        Check(f.Ensure(),"all three canonical weapons ready");
        Check(f.Ctx.m_assetByGuid.Count==3 && f.Ctx.m_loadedWrappers.Count==3 && f.Ctx.m_allAssets.Count==3,"exact private3 registrations");
        foreach(int i in new[]{1,8,9})
        {
            var p=f.Catalog.Profiles[i];var data=f.Weapons[i];var source=(EnemyWeaponDataAsset)f.Ctx.Sources[p.WeaponGuid];
            var wrapper=(EnemyWeaponDataAsset)f.Ctx.m_loadedWrappers[data.Guid];
            Check(wrapper.Pointer!=source.Pointer && wrapper.PrefabRef.Guid.m_data1==source.PrefabRef.Guid.m_data1 && wrapper.PrefabRef.Guid.m_data2==source.PrefabRef.Guid.m_data2,"copy exact official PrefabRef");
            Check(wrapper.Get(f.Ctx).Pointer==data.Pointer && wrapper.AssetObject.Pointer==data.Pointer,"both resolver paths return canonical pointer");
            Check(data.Writes==2 && source.AssetGuid.Value==p.WeaponGuid,"no canonical or source writes");
        }
        Check(EnemyWeaponData.Created==created,"bridge creates zero simulation weapons");
        Check(f.Ensure() && PoolingSystem.Active.Loads==3,"idempotent registration and resource handles");
        f.Weapons=(EnemyWeaponData[])f.Weapons.Clone();Check(f.Ensure(),"array clone same pointers accepted");
        foreach(string failure in new[]{"null","length","missing","extra","guid","path","pointer","duplicatePointer","index","sourceType","sourceGuid","zeroPrefab","unpooled","root","warmRoot","childOnly","loadKey","assetFailure","assetCollision","wrapperCollision","pendingCollision","listGuid","listPath","dictionaryPostAdd","wrapperPostAdd","listPostAdd","globalResolve"})
        {
            f=new Fixture();var p=f.Catalog.Profiles[1];var guid=f.Weapons[1].Guid;var pool=PoolingSystem.Active;
            switch(failure)
            {
                case "null":f.Weapons=null;break;
                case "length":f.Weapons=new EnemyWeaponData[3];break;
                case "missing":f.Weapons[1]=null;break;
                case "extra":f.Weapons[0]=f.Weapons[1];break;
                case "guid":f.Weapons[1].Guid=new AssetGuid{Value=9};break;
                case "path":f.Weapons[1].Path="foreign";break;
                case "pointer":f.Weapons[1].Pointer=IntPtr.Zero;break;
                case "duplicatePointer":f.Weapons[8].Pointer=f.Weapons[1].Pointer;break;
                case "index":p.Index=8;break;
                case "sourceType":f.Ctx.Sources[p.WeaponGuid]=new AssetBase{AssetGuid=new AssetGuid{Value=p.WeaponGuid}};break;
                case "sourceGuid":f.Ctx.Sources[p.WeaponGuid].AssetGuid=new AssetGuid{Value=99};break;
                case "zeroPrefab":((EnemyWeaponDataAsset)f.Ctx.Sources[p.WeaponGuid]).PrefabRef=default;break;
                case "unpooled":pool.m_prefabs=Array.Empty<PoolingSystem.PrefabData>();break;
                case "root":pool.m_prefabs[0].Asset.Component=null;break;
                case "warmRoot":pool.m_prefabs[0].RootInstance=new GameObject();break;
                case "childOnly":pool.m_prefabs[0].Asset.Component=null;pool.m_prefabs[0].Asset.Child=new EnemyWeaponView();break;
                case "loadKey":pool.m_prefabs[0].LoadKey="";break;
                case "assetFailure":pool.m_prefabs[0].Asset=null;break;
                case "assetCollision":f.Ctx.m_assetByGuid.Add(guid,new EnemyWeaponData());break;
                case "wrapperCollision":f.Ctx.m_loadedWrappers.Add(guid,new EnemyWeaponDataAsset());break;
                case "pendingCollision":f.Ctx.m_wrappersBeingLoaded.Add(guid,new object());break;
                case "listGuid":f.Ctx.m_allAssets.Add(new EnemyWeaponData{Guid=guid,Path="foreign"});break;
                case "listPath":f.Ctx.m_allAssets.Add(new EnemyWeaponData{Guid=new AssetGuid{Value=8},Path=p.Path("weapon")});break;
                case "dictionaryPostAdd":f.Ctx.m_assetByGuid.FailAfterAdd=2;break;
                case "wrapperPostAdd":f.Ctx.m_loadedWrappers.FailAfterAdd=2;break;
                case "listPostAdd":f.Ctx.m_allAssets.FailAfterAdd=2;break;
                case "globalResolve":AssetBase.Resolve=g=>null;break;
            }
            Throws(()=>f.Ensure(),failure);
            if(failure=="assetCollision"||failure=="wrapperCollision"||failure=="listGuid"||failure=="listPath")
                Check(f.Ctx.m_assetByGuid.Count+f.Ctx.m_loadedWrappers.Count+f.Ctx.m_allAssets.Count==1,"foreign item preserved: "+failure);
            else f.Empty("no partial registrations: "+failure);
        }
        f=new Fixture();f.Ctx.Sources.Remove(f.Catalog.Profiles[1].WeaponGuid);
        Check(!f.Ensure(),"source pending returns waiting");f.Empty("waiting source has no partial cache");
        f=new Fixture();PoolingSystem.Active.Done=false;
        Check(!f.Ensure(),"resource pending returns waiting");f.Empty("waiting root has no partial cache");
        PoolingSystem.Active.Done=true;Check(f.Ensure(),"resource completion registers safely");
        foreach(string mutation in new[]{"canonical","source","sourcePrefab","wrapperPrefab","wrapperGuid","root","loadKey","globalResolve","pool","assets","wrappers","list","catalog","unpruned"})
        {
            f=new Fixture();Check(f.Ensure(),"mutation fixture ready: "+mutation);
            var p=f.Catalog.Profiles[1];var source=(EnemyWeaponDataAsset)f.Ctx.Sources[p.WeaponGuid];
            var wrapper=(EnemyWeaponDataAsset)f.Ctx.m_loadedWrappers[f.Weapons[1].Guid];
            switch(mutation)
            {
                case "canonical":f.Weapons[1]=new EnemyWeaponData{Guid=f.Weapons[1].Guid,Path=f.Weapons[1].Path};break;
                case "source":f.Ctx.Sources[p.WeaponGuid]=new EnemyWeaponDataAsset{AssetGuid=source.AssetGuid,PrefabRef=source.PrefabRef};break;
                case "sourcePrefab":source.PrefabRef=default;break;
                case "wrapperPrefab":wrapper.PrefabRef=default;break;
                case "wrapperGuid":wrapper.AssetGuid=new AssetGuid{Value=7};break;
                case "root":PoolingSystem.Active.m_prefabs[0].Asset.Component=null;break;
                case "loadKey":PoolingSystem.Active.m_prefabs[0].LoadKey="mutated";break;
                case "globalResolve":AssetBase.Resolve=g=>null;break;
                case "pool":PoolingSystem.Active=new PoolingSystem();break;
                case "assets":f.Ctx.m_assetByGuid.Remove(f.Weapons[1].Guid);break;
                case "wrappers":f.Ctx.m_loadedWrappers.Remove(f.Weapons[1].Guid);break;
                case "list":f.Ctx.m_allAssets[0]=new EnemyWeaponData();break;
                case "catalog":p.WeaponGuid=77;break;
                case "unpruned":p.BlockedActions=Array.Empty<long>();break;
            }
            Throws(()=>f.Ensure(),"owned mutation refuses READY: "+mutation);
        }
        Console.WriteLine("EnemyRestorationWeaponViews pure orchestration tests: "+checks+" passed.");
    }
}
