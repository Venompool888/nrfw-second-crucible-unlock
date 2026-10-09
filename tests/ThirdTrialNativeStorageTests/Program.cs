using CrucibleUnlock;
using Il2CppQuantum;
static class Program
{
    static int checks;
    static void Check(bool value,string why) { ++checks;if(!value)throw new Exception(why); }
    static void Throws(Action action,string why)
    { bool threw=false;try {action();}catch(InvalidOperationException){threw=true;}Check(threw,why); }
    sealed class Fixture
    {
        public EnemyRestorationCatalog Catalog=EnemyRestorationCatalog.Load();
        public Frame Frame=new();
        public bool EntranceReady;
        public Exception Failure;
        public ThirdTrialStorageBackend Backend;
        public Fixture(bool optional=false)
        {
            Add(new TeamData(),Catalog.TeamGuid);
            foreach(var p in Catalog.Profiles)
            {
                Add(new EntityData(),p.HostGuid);Add(new EntityViewData(),p.ViewGuid);
                var ai=Add(new HumanoidEnemyAiControllerData(),p.ControllerGuid);
                long weapon=p.BlockedActions.Length==0?p.WeaponGuid:p.PrivateWeaponGuid;
                ai.Weapon.Id=new AssetGuid{Value=weapon};ai.OffHandItem.Id=new AssetGuid{Value=p.OffhandGuid};
                if(!Frame.Assets.ContainsKey(weapon))Add(new EnemyWeaponData(),weapon);
                if(p.OffhandGuid!=0&&!Frame.Assets.ContainsKey(p.OffhandGuid))Add(new EnemyWeaponData(),p.OffhandGuid);
                var npc=Add(new NpcData(),p.NpcGuid);
                npc.CharacterType=p.CharacterType;npc.CharacterVersion=100+p.Index;
                npc.Export.Id=123; // Production must replace the template/block Id.
                npc.Export.NpcData.Id=new AssetGuid {Value=456};
                npc.Export.EntityData.Id=new AssetGuid {Value=p.HostGuid};
                npc.Export.Controller.Id=new AssetGuid {Value=p.ControllerGuid};
                npc.Export.EntityView.Id=new AssetGuid {Value=p.ViewGuid};
                npc.Export.TeamData.Id=new AssetGuid {Value=p.TeamGuid};
                if(optional)
                {
                    long interaction=900000+p.Index*2,spawner=interaction+1;
                    Add(new InteractionControllerData(),interaction);Add(new NpcSpawnerData(),spawner);
                    npc.Export.InteractionController.Id=new AssetGuid{Value=interaction};
                    npc.Export.Spawner.Id=new AssetGuid{Value=spawner};
                }
            }
        }
        T Add<T>(T asset,long guid) where T:AssetObject
        {asset.Guid=new AssetGuid{Value=guid};Frame.Assets.Add(guid,asset);return asset;}
        public void Ensure()
        {
            EntranceReady=false;Failure=null;Backend=new ThirdTrialStorageBackend(Frame);
            try {ThirdTrialStoragePolicy.Ensure(Catalog,Backend);EntranceReady=true;}
            catch(Exception error){Failure=error;}
        }
        public void CheckFailed(string why)
        {Ensure();Check(Failure!=null&&!EntranceReady,why+": entry unavailable");}
    }
    static RuntimeNpcAttachments Block(Fixture f,EnemyRestorationProfile p)
    {
        var s=f.Frame.InstanceDataStorage.m_storageRuntimeNpcAttachments;
        return s.m_storage[s.m_lookup[p.NpcGuid]-1];
    }
    static void Main()
    {
        var f=new Fixture();f.Ensure();Check(f.Failure==null&&f.EntranceReady,"complete real backend graph ready: "+f.Failure);
        var storage=f.Frame.InstanceDataStorage;
        Check(storage.m_storageNpcData.m_storage.Count==10&&storage.m_storageEntityData.m_storage.Count==10&&
            storage.m_storageControllerData.m_storage.Count==10&&storage.m_storageEntityViewData.m_storage.Count==10,"forty private dependencies");
        Check(storage.m_storageTeamData.m_storage.Count==1,"shared team deduplicated");
        Check(storage.m_storageTree.m_storage.Count==10&&storage.m_storageRuntimeNpcAttachments.m_storage.Count==10,"ten trees and ten blocks");
        Check(storage.m_storageTree.m_storage.Select(x=>x.Pointer).Distinct().Count()==10,"ten independent behavior trees");
        Check(storage.m_storageRuntimeNpcAttachments.m_storage.Select(x=>x.Pointer).Distinct().Count()==10,"ten independent attachment blocks");
        foreach(var p in f.Catalog.Profiles)
        {
            var b=Block(f,p);
            Check(b.Id==p.NpcGuid&&b.NpcData.Get(f.Frame).Guid.Value==p.NpcGuid,"exported NPC ID remapped");
            Check(b.EntityData.Get(f.Frame).Guid.Value==p.HostGuid&&b.EntityView.Get(f.Frame).Guid.Value==p.ViewGuid&&
                b.Controller.Get(f.Frame).Guid.Value==p.ControllerGuid&&b.TeamData.Get(f.Frame).Guid.Value==p.TeamGuid,"raw nonzero reference closure");
            var tree=b.Controller.Get(f.Frame).GetTreeRef(f.Frame).Get(f.Frame);
            Check(tree.OwnerGuid.Value==p.ControllerGuid&&tree.InstanceMemorySize>0,"tree owner and memory");
            Check(b.InstanceData.IsDefault&&b.PatrolRoute.IsDefault,"no inherited placement refs");
            Check(b.InteractionController.IsDefault&&b.Spawner.IsDefault,"raw zero refs remain default");
            Check(b.CharacterType==p.CharacterType&&b.CharacterVersion==100+p.Index,"character type/version closure");
            Check(f.Backend.IsReady(p),"production IsReady readback");
        }
        // Native lookups are 1-based while storage is zero-based. The first and
        // last entries must both resolve correctly. Replacing [i-1] with [i] in
        // production fails this real Ensure/readback path (last is out of range).
        Check(storage.m_storageNpcData.m_lookup[f.Catalog.Profiles[0].NpcGuid]==1&&
            storage.m_storageNpcData.m_lookup[f.Catalog.Profiles[9].NpcGuid]==10,"lookup exposes 1-based boundary");
        int writes=storage.Writes,trees=f.Frame.TreeBuilds,blocks=f.Frame.BlockBuilds;
        f.Ensure();Check(f.EntranceReady&&f.Failure==null&&storage.Writes==writes,"repeat real backend no append");
        Check(f.Frame.TreeBuilds==trees&&f.Frame.BlockBuilds==blocks,"repeat no tree/block recreation");
        Throws(()=>storage.Add((NpcData)f.Frame.Assets[f.Catalog.Profiles[0].NpcGuid]),"fake native Add rejects duplicates");

        var optional=new Fixture(true);optional.Ensure();Check(optional.EntranceReady,"all optional raw dependencies ready");
        Check(optional.Frame.InstanceDataStorage.m_storageInteractionControllerData.m_storage.Count==10&&
            optional.Frame.InstanceDataStorage.m_storageNpcSpawnerData.m_storage.Count==10,"twenty optional deps cached");
        foreach(var p in optional.Catalog.Profiles)
        {
            var b=Block(optional,p);
            Check(b.InteractionController.Index!=0&&b.Spawner.Index!=0&&
                b.InteractionController.Get(optional.Frame).Guid.Value==900000+p.Index*2&&
                b.Spawner.Get(optional.Frame).Guid.Value==900001+p.Index*2,"optional nonzero ref closure");
        }
        // A late dependency failure must prevent the first cache mutation.
        foreach(string kind in new[]{"npc","host","controller","view","team","weapon","offhand","interaction","spawner"})
        {
            var missing=new Fixture(true);var p=missing.Catalog.Profiles[9];
            var ai=(HumanoidEnemyAiControllerData)missing.Frame.Assets[p.ControllerGuid];
            long id=kind switch {"npc"=>p.NpcGuid,"host"=>p.HostGuid,"controller"=>p.ControllerGuid,"view"=>p.ViewGuid,
                "team"=>p.TeamGuid,"weapon"=>ai.Weapon.Id.Value,"offhand"=>987654321,"interaction"=>900000+p.Index*2,_=>900001+p.Index*2};
            if(kind=="offhand")ai.OffHandItem.Id=new AssetGuid{Value=id};
            missing.Frame.Assets.Remove(id);missing.CheckFailed("missing "+kind);
            Check(missing.Frame.InstanceDataStorage.Writes==0,"missing "+kind+" preflight zero writes");
        }
        foreach(string kind in new[]{"npc","host","controller","view","team","interaction","spawner","tree","block"})
        {
            var collision=new Fixture(true);var p=collision.Catalog.Profiles[9];var s=collision.Frame.InstanceDataStorage;
            AssetObject Foreign(AssetObject a,long id){a.Guid=new AssetGuid{Value=id};return a;}
            switch(kind)
            {
                case "npc":s.Add(Foreign(new NpcData(),p.NpcGuid));break;
                case "host":s.Add(Foreign(new EntityData(),p.HostGuid));break;
                case "controller":s.Add(Foreign(new ControllerData(),p.ControllerGuid));break;
                case "view":s.Add(Foreign(new EntityViewData(),p.ViewGuid));break;
                case "team":s.Add(Foreign(new TeamData(),p.TeamGuid));break;
                case "interaction":s.Add(Foreign(new InteractionControllerData(),900000+p.Index*2));break;
                case "spawner":s.Add(Foreign(new NpcSpawnerData(),900001+p.Index*2));break;
                case "tree":s.Add(new Tree {OwnerGuid=new AssetGuid{Value=p.ControllerGuid},InstanceMemorySize=64});break;
                case "block":s.Add(new RuntimeNpcAttachments {Id=p.NpcGuid});break;
            }
            int before=s.Writes;collision.CheckFailed("foreign "+kind);
            Check(s.Writes==before,"foreign "+kind+" zero plugin writes");
        }
        var malformed=new Fixture();var last=malformed.Catalog.Profiles[9];
        ((NpcData)malformed.Frame.Assets[last.NpcGuid]).Export.EntityView.Id=new AssetGuid{Value=last.HostGuid};
        malformed.CheckFailed("raw graph mismatch");Check(malformed.Frame.InstanceDataStorage.Writes==0,"raw graph preflight zero writes");
        var badWeapon=new Fixture();var weaponProfile=badWeapon.Catalog.Profiles[9];
        badWeapon.Frame.BadWeaponGet=((HumanoidEnemyAiControllerData)badWeapon.Frame.Assets[weaponProfile.ControllerGuid]).Weapon.Id.Value;
        badWeapon.CheckFailed("weapon context Get differs from FindAsset");Check(badWeapon.Frame.InstanceDataStorage.Writes==0,"weapon dual lookup preflight zero writes");
        foreach(string fail in new[]{"npcExport","treeBuild","emptyTree","nullTreeRoot","treeAdd","blockExport","blockMismatch","characterType","characterVersion","zeroInteraction","zeroSpawner","blockAdd","blockAddAfterInsertion"})
        {
            var broken=new Fixture();var p=broken.Catalog.Profiles[9];var s=broken.Frame.InstanceDataStorage;
            switch(fail)
            {
                case "npcExport":broken.Frame.FailExport=p.NpcGuid;break;
                case "treeBuild":broken.Frame.FailTree=p.ControllerGuid;break;
                case "emptyTree":broken.Frame.EmptyTree=p.ControllerGuid;break;
                case "nullTreeRoot":broken.Frame.NoRootTree=p.ControllerGuid;break;
                case "treeAdd":s.FailTreeAdd=p.ControllerGuid;break;
                case "blockExport":broken.Frame.FailBlockExport=p.NpcGuid;break;
                case "blockMismatch":broken.Frame.MismatchBlock=p.NpcGuid;break;
                case "characterType":broken.Frame.BadCharacterType=p.NpcGuid;break;
                case "characterVersion":broken.Frame.BadCharacterVersion=p.NpcGuid;break;
                case "zeroInteraction":broken.Frame.NondefaultZeroInteraction=p.NpcGuid;break;
                case "zeroSpawner":broken.Frame.NondefaultZeroSpawner=p.NpcGuid;break;
                case "blockAdd":s.FailBlockAdd=p.NpcGuid;break;
                case "blockAddAfterInsertion":s.FailBlockAdd=p.NpcGuid;s.AddBeforeThrow=true;break;
            }
            broken.CheckFailed(fail);
            if(fail is "blockExport" or "blockMismatch")Check(s.m_storageRuntimeNpcAttachments.m_storage.Count==0,"all blocks prepared before expose "+fail);
            if(fail=="npcExport")Check(s.Writes==0,"export failure before any writes");
            if(fail=="blockAddAfterInsertion")Check(s.m_storageRuntimeNpcAttachments.m_storage.Count==10&&!broken.EntranceReady,
                "Add may throw after insertion: populated caches do not grant entry readiness");
        }
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Il2Cpp",StringComparison.OrdinalIgnoreCase)||
            a.GetName().Name.StartsWith("Unity",StringComparison.OrdinalIgnoreCase)),"no game/Unity/IL2CPP assemblies loaded");
        Console.WriteLine("ThirdTrialStorageBackend real-source fake tests passed: "+checks+"; no game/Unity/IL2CPP DLL references or loads.");
    }
}
