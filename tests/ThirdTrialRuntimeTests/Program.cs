using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CrucibleUnlock;
using Il2CppQuantum;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
static class Program
{
    static int checks;
    static void Check(bool v,string why){++checks;if(!v)throw new Exception(why);}
    static object Invoke(string name,params object[] args)=>typeof(ThirdTrialRuntime).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static InfiniteDungeonPlaylist Template()
    {
        using var stream=typeof(Program).Assembly.GetManifestResourceStream("NRFW.Tests.SecondPlaylist.json");
        using var doc=JsonDocument.Parse(stream);var d=doc.RootElement;
        long Guid(JsonElement e)=>e.GetProperty("Value").GetInt64();
        long Ref(JsonElement e)=>Guid(e.GetProperty("Id"));
        var p=new InfiniteDungeonPlaylist{Guid=new(){Value=Guid(d.GetProperty("Guid"))},Path=d.GetProperty("Path").GetString(),
            PlaylistType=(InfiniteDungeonPlaylistType)d.GetProperty("PlaylistType").GetInt32(),SpawnPoint=new(){Id=new(){Value=Ref(d.GetProperty("SpawnPoint"))}},
            DisableLootDrops=d.GetProperty("DisableLootDrops").GetBoolean(),DisableHealingItemsUsage=d.GetProperty("DisableHealingItemsUsage").GetBoolean(),
            DisableExperienceGain=d.GetProperty("DisableExperienceGain").GetBoolean(),LootDropModifierRaw=d.GetProperty("LootDropModifier").GetProperty("RawValue").GetInt64(),
            RespiteRoomRange=new(){X=d.GetProperty("RespiteRoomRange").GetProperty("X").GetInt32(),Y=d.GetProperty("RespiteRoomRange").GetProperty("Y").GetInt32()}};
        foreach(var e in d.GetProperty("Entries").EnumerateArray())
        {
            var ns=e.GetProperty("Nuggets");var nuggets=new Il2CppStructArray<AssetRefNuggetData>(ns.GetArrayLength());int i=0;
            foreach(var n in ns.EnumerateArray())nuggets[i++]=new(){Id=new(){Value=Ref(n)}};
            p.Entries.Add(new(){RandomizedFromType=e.GetProperty("RandomizedFromType").GetBoolean(),FloorType=(InfiniteDungeonFloorType)e.GetProperty("FloorType").GetInt32(),SpecificFloor=new(){Id=new(){Value=Ref(e.GetProperty("SpecificFloor"))}},Nuggets=nuggets});
        }
        p.ExtraLootSettings.Add("template-clone-marker",73);return p;
    }
    static string Snapshot(InfiniteDungeonPlaylist p)=>JsonSerializer.Serialize(new{guid=p.Guid.Value,p.Path,type=(int)p.PlaylistType,spawn=p.SpawnPoint.Id.Value,
        p.DisableLootDrops,p.DisableHealingItemsUsage,p.DisableExperienceGain,p.LootDropModifierRaw,p.RespiteRoomRange.X,p.RespiteRoomRange.Y,
        entries=p.Entries.Select(e=>new{e.RandomizedFromType,type=(int)e.FloorType,guid=e.SpecificFloor.Id.Value,nuggets=Enumerable.Range(0,e.Nuggets.Length).Select(i=>e.Nuggets[i].Id.Value).ToArray()}).ToArray(),extra=p.ExtraLootSettings});
    sealed class Fixture
    {
        public readonly Frame Frame=new();public readonly EnemyRestorationCatalog Catalog=EnemyRestorationCatalog.Load();
        public readonly ThirdTrialCatalog Rooms=ThirdTrialCatalog.LoadEmbedded();public readonly InfiniteDungeonPlaylist Original=Template();
        public bool FailAssets,FailPublish;
        public Fixture()
        {
            void Add(AssetObject a,long id){a.Guid=new(){Value=id};Frame.Assets[id]=a;}
            Add(new TeamData(),Catalog.TeamGuid);
            foreach(var p in Catalog.Profiles)
            {
                Add(new EntityData(),p.HostGuid);Add(new EntityViewData(),p.ViewGuid);
                var ai=new HumanoidEnemyAiControllerData{Weapon=new(){Id=new(){Value=p.BlockedActions.Length==0?p.WeaponGuid:p.PrivateWeaponGuid}},OffHandItem=new(){Id=new(){Value=p.OffhandGuid}}};
                Add(ai,p.ControllerGuid);Add(new EnemyWeaponData(),ai.Weapon.Id.Value);if(p.OffhandGuid!=0)Add(new EnemyWeaponData(),p.OffhandGuid);
                var npc=new NpcData{CharacterType=p.CharacterType,CharacterVersion=p.Index+20};
                npc.Export.EntityData.Id=new(){Value=p.HostGuid};npc.Export.Controller.Id=new(){Value=p.ControllerGuid};npc.Export.EntityView.Id=new(){Value=p.ViewGuid};npc.Export.TeamData.Id=new(){Value=p.TeamGuid};Add(npc,p.NpcGuid);
            }
            foreach(var s in Rooms.Stages){Add(new InfiniteDungeonFloorData(),s.FloorGuid);foreach(long id in s.EntryNuggetGuids)Add(new NuggetData(),id);}
            Add(new WorldSpawnPointData(),ThirdTrialPolicy.SpawnPointGuid);
            Frame.Context.ResourceManager.Assets.Add(ThirdTrialPolicy.TemplatePlaylistGuid,Original);
        }
        public void Install()
        {
            TrialRepairLog.Hooks.Clear();
            ThirdTrialRuntime.StopWithoutNativeReads();EnemyRestorationRuntime.Catalog=Catalog;EnemyRestorationRuntime.CurrentViewsReady=true;
            EnemyRestorationRuntime.CurrentManager=Frame.Context.ResourceManager;
            EnemyRestorationRuntime.Published.Clear();EnemyRestorationRuntime.Publications=0;EnemyRestorationRuntime.FailPublish=FailPublish;
            EnemyRestorationRuntime.Prepare=f=>
            {
                f.InstanceDataStorage.Events.Add("assets");if(FailAssets)throw new InvalidOperationException("owned graph preparation failure");
                foreach(var p in Catalog.Profiles)foreach(long id in new[]{p.NpcGuid,p.HostGuid,p.ControllerGuid,p.ViewGuid})
                    f.DynamicAssetDB.Assets[id]=f.Assets[id];
                foreach(var p in Catalog.Profiles.Where(p=>p.BlockedActions.Length>0))f.DynamicAssetDB.Assets[p.PrivateWeaponGuid]=f.Assets[p.PrivateWeaponGuid];
                f.PrivateAssetsPrepared=Catalog.Profiles.Length*4+Catalog.Profiles.Count(p=>p.BlockedActions.Length>0);
            };
            ThirdTrialRuntime.Install();
        }
        public void Init()
        {
            if(Frame.OfficialContentExports==0)
                TrialRepairLog.Dispatch(typeof(InitializeContentSystem),"OnInit",Frame,()=>new InitializeContentSystem().OnInit(Frame));
            else Invoke("AfterInit",Frame); // Repeated mod callback, not repeated native export.
        }
        public InfiniteDungeonPlaylist Private=>Frame.DynamicAssetDB.FindAsset(new(){Value=ThirdTrialPolicy.PlaylistGuid})?.TryCast<InfiniteDungeonPlaylist>();
        public void CheckFailed(string reason,bool canRemain=false)
        {
            Install();Init();Check(!ThirdTrialRuntime.Enabled&&!ThirdTrialRuntime.IsFrameReady(Frame)&&ThirdTrialRuntime.ResolveReadyPlaylist(Frame)==null,reason+" sealed");
            Check(EnemyRestorationRuntime.Publications==0,reason+" cannot publish simulation views on partial bootstrap");
            if(!canRemain)Check(Frame.PlaylistAdds==0,reason+" no private playlist registration");
        }
    }
    static int Main(string[] args)
    {
        Exception failure=null;
        try { Run(); } catch(Exception error) { failure=error; Console.WriteLine("FAIL: "+error.Message); }
        string source=args.Length>0?args[0]:"src/CrucibleUnlock/ThirdTrialRuntime.cs";
        var report=new {source=Path.GetFullPath(source),source_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),
            actual_source_compiled=true,managed_fakes_only=true,checks,passed=failure==null,failure=failure?.ToString(),
            limitation="Actual ThirdRuntime/storage source with managed publisher fake verifies complete-bootstrap ordering, not native runtime publication or rendering."};
        if(args.Length>1)File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        return failure==null?0:1;
    }
    static void Run()
    {
        CheckOfficialInitializationOrder();
        var f=new Fixture();string source=Snapshot(f.Original);f.Install();f.Init();
        Check(ThirdTrialRuntime.Enabled&&ThirdTrialRuntime.IsFrameReady(f.Frame),"actual OnInit ready");var p=f.Private;
        Check(EnemyRestorationRuntime.Publications==1&&f.Frame.InstanceDataStorage.Events.IndexOf("publish")>f.Frame.InstanceDataStorage.Events.IndexOf("playlist"),
            "Actual Third AfterInit publishes once only after full storage and playlist bootstrap");
        Check(p!=null&&p.Pointer!=f.Original.Pointer&&Snapshot(f.Original)==source,"real second fixture independently cloned and unchanged");
        Check(f.Frame.Context.AssetSerializer.Original.Pointer==f.Original.Pointer&&f.Original.Guid.Value==ThirdTrialPolicy.TemplatePlaylistGuid,"cloned exact mainline second source");
        Check(p.Entries.Count==11&&(int)p.PlaylistType==1&&p.SpawnPoint.Id.Value==ThirdTrialPolicy.SpawnPointGuid,"ten Arena plus reward and third spawn/type");
        Check(p.RespiteRoomRange.X==5&&p.RespiteRoomRange.Y==5&&p.DisableLootDrops&&!p.DisableHealingItemsUsage&&!p.DisableExperienceGain&&p.LootDropModifierRaw==65536,"second respite and loot/experience flags cloned");
        Check(p.ExtraLootSettings["template-clone-marker"]==73&&!ReferenceEquals(p.ExtraLootSettings,f.Original.ExtraLootSettings),"unmodified serialized settings deep copied");
        Check(!ReferenceEquals(p.Entries,f.Original.Entries)&&!ReferenceEquals(p.Entries[10],f.Original.Entries[7]),"entries and reward independent");
        for(int i=0;i<10;i++)
        {
            var e=p.Entries[i];var s=f.Rooms.Stages[i];
            Check(!e.RandomizedFromType&&(int)e.FloorType==2&&e.SpecificFloor.Id.Value==s.FloorGuid&&Enumerable.Range(0,e.Nuggets.Length).Select(j=>e.Nuggets[j].Id.Value).SequenceEqual(s.EntryNuggetGuids),"fixed full nugget stage "+i);
        }
        Check(p.Entries[10].RandomizedFromType&&(int)p.Entries[10].FloorType==4,"native second reward retained");
        var events=f.Frame.InstanceDataStorage.Events;
        Check(events.IndexOf("official-content-complete")<events.IndexOf("assets")&&events.Count(x=>x=="tree")==10&&events.Count(x=>x=="block")==10,"native content then asset graph before private storage");
        Check(events.IndexOf("block")>events.LastIndexOf("tree")&&events.IndexOf("playlist")>events.LastIndexOf("block"),"Trees then all blocks then playlist order");
        Check(events.Skip(events.IndexOf("official-content-complete")+1).TakeWhile(x=>x!="block").Count(x=>x=="dependency")==40,"all forty private dependencies before blocks; official Team reused");
        int writes=f.Frame.InstanceDataStorage.Writes,adds=f.Frame.PlaylistAdds,trees=f.Frame.TreeBuilds,blocks=f.Frame.BlockBuilds;f.Init();
        Check(f.Frame.InstanceDataStorage.Writes==writes&&f.Frame.PlaylistAdds==adds&&f.Frame.TreeBuilds==trees&&f.Frame.BlockBuilds==blocks&&ThirdTrialRuntime.IsFrameReady(f.Frame),"repeat OnInit no duplicate writes");
        Check(EnemyRestorationRuntime.Publications==1,"repeat complete bootstrap does not duplicate published snapshot");
        EnemyRestorationRuntime.CurrentViewsReady=false;Check(!ThirdTrialRuntime.IsFrameReady(f.Frame)&&ThirdTrialRuntime.ResolveReadyPlaylist(f.Frame)==null,"all current views gate");
        EnemyRestorationRuntime.CurrentViewsReady=true;Check(ReferenceEquals(ThirdTrialRuntime.ResolveReadyPlaylist(f.Frame),p),"exact ready private source");
        var other=new Frame();foreach(var a in f.Frame.Assets)other.Assets[a.Key]=a.Value;foreach(var a in f.Frame.DynamicAssetDB.Assets)other.DynamicAssetDB.Assets[a.Key]=a.Value;
        Check(!ThirdTrialRuntime.IsFrameReady(other),"another storage identity not initialized");other.InstanceDataStorage=f.Frame.InstanceDataStorage;
        Check(!ThirdTrialRuntime.IsFrameReady(other),"retired/different manager cannot borrow initialized storage and current ready flag");
        other.Context.ResourceManager=f.Frame.Context.ResourceManager;
        Check(ThirdTrialRuntime.IsFrameReady(other),"same initialized storage valid for another Frame");
        foreach(var profile in f.Catalog.Profiles)foreach(long id in new[]{profile.NpcGuid,profile.ControllerGuid})
        {
            var a=f.Frame.DynamicAssetDB.Assets[id];f.Frame.DynamicAssetDB.Assets.Remove(id);
            Check(!ThirdTrialRuntime.IsFrameReady(f.Frame),"every profile dynamic NPC/controller gate");f.Frame.DynamicAssetDB.Assets[id]=a;
        }
        Check(ThirdTrialRuntime.TryGetNpc(f.Frame,f.Catalog.Profiles[0].Key,out var npc)&&npc.Id.Value==f.Catalog.Profiles[0].NpcGuid,"actual TryGetNpc ready graph");
        var lastReady=f.Catalog.Profiles[9];var cached=f.Frame.InstanceDataStorage.m_storageNpcData;
        int cachedIndex=cached.m_lookup[lastReady.NpcGuid]-1;var cachedNpc=cached.m_storage[cachedIndex];
        cached.m_storage[cachedIndex]=new NpcData{Guid=new(){Value=lastReady.NpcGuid}};
        Check(!ThirdTrialRuntime.IsFrameReady(f.Frame),"same GUID foreign cached NPC pointer cannot grant readiness");cached.m_storage[cachedIndex]=cachedNpc;
        var blockSlot=f.Frame.InstanceDataStorage.m_storageRuntimeNpcAttachments;int blockIndex=blockSlot.m_lookup[lastReady.NpcGuid];
        blockSlot.m_lookup.Remove(lastReady.NpcGuid);Check(!ThirdTrialRuntime.IsFrameReady(f.Frame),"missing tenth runtime block prevents readiness");blockSlot.m_lookup[lastReady.NpcGuid]=blockIndex;
        var treeSlot=f.Frame.InstanceDataStorage.m_storageTree;var tree=treeSlot.m_storage[treeSlot.m_lookup[lastReady.ControllerGuid]-1];
        var owner=tree.OwnerGuid;tree.OwnerGuid=default;Check(!ThirdTrialRuntime.IsFrameReady(f.Frame),"tenth tree requires exact owner");tree.OwnerGuid=owner;
        var badOrder=new Fixture();(badOrder.Catalog.Profiles[0],badOrder.Catalog.Profiles[1])=(badOrder.Catalog.Profiles[1],badOrder.Catalog.Profiles[0]);
        bool orderRejected=false;try{badOrder.Install();}catch(InvalidOperationException){orderRejected=true;}Check(orderRejected&&!ThirdTrialRuntime.Enabled&&badOrder.Frame.InstanceDataStorage.Writes==0,"profile order collision before initialization");
        foreach(string failure in new[]{"assets","npc","weapon","weaponGet","npcExport","tree","emptyTree","rootTree","treeAdd","blocks","blockMismatch","typeMismatch","versionMismatch","zeroInteraction","zeroSpawner","blockAdd","blockAddAfterInsert","template","clone","sameClone","cloneCount","reward","floor","nugget","spawn","foreign","sameShapeForeign","playlistAdd","afterAdd","readback","pointerReadback","lootFlag","healingFlag","experienceFlag","respite","publish"})
        {
            var b=new Fixture();var last=b.Catalog.Profiles[9];
            switch(failure)
            {
                case "assets":b.FailAssets=true;break;
                case "publish":b.FailPublish=true;break;
                case "npc":b.Frame.Assets.Remove(last.NpcGuid);break;
                case "weapon":b.Frame.Assets.Remove(((HumanoidEnemyAiControllerData)b.Frame.Assets[last.ControllerGuid]).Weapon.Id.Value);break;
                case "weaponGet":b.Frame.BadWeaponGet=((HumanoidEnemyAiControllerData)b.Frame.Assets[last.ControllerGuid]).Weapon.Id.Value;break;
                case "npcExport":b.Frame.FailExport=last.NpcGuid;break;
                case "tree":b.Frame.FailTree=last.ControllerGuid;break;
                case "emptyTree":b.Frame.EmptyTree=last.ControllerGuid;break;
                case "rootTree":b.Frame.NoRootTree=last.ControllerGuid;break;
                case "treeAdd":b.Frame.InstanceDataStorage.FailTreeAdd=last.ControllerGuid;break;
                case "blocks":b.Frame.FailBlockExport=last.NpcGuid;break;
                case "blockMismatch":b.Frame.MismatchBlock=last.NpcGuid;break;
                case "typeMismatch":b.Frame.BadCharacterType=last.NpcGuid;break;
                case "versionMismatch":b.Frame.BadCharacterVersion=last.NpcGuid;break;
                case "zeroInteraction":b.Frame.NondefaultZeroInteraction=last.NpcGuid;break;
                case "zeroSpawner":b.Frame.NondefaultZeroSpawner=last.NpcGuid;break;
                case "blockAdd":b.Frame.InstanceDataStorage.FailBlockAdd=last.NpcGuid;break;
                case "blockAddAfterInsert":b.Frame.InstanceDataStorage.FailBlockAdd=last.NpcGuid;b.Frame.InstanceDataStorage.AddBeforeThrow=true;break;
                case "template":b.Frame.Context.ResourceManager.Assets.Clear();break;
                case "clone":b.Frame.Context.AssetSerializer.Fail=true;break;
                case "sameClone":b.Frame.Context.AssetSerializer.ReturnOriginal=true;break;
                case "cloneCount":b.Frame.Context.AssetSerializer.WrongCount=true;break;
                case "reward":b.Frame.Context.AssetSerializer.WrongReward=true;break;
                case "floor":b.Frame.Assets.Remove(b.Rooms.Stages[9].FloorGuid);break;
                case "nugget":b.Frame.Assets.Remove(b.Rooms.Stages[9].EntryNuggetGuids[^1]);break;
                case "spawn":b.Frame.Assets.Remove(ThirdTrialPolicy.SpawnPointGuid);break;
                case "foreign":b.Frame.DynamicAssetDB.Assets.Add(ThirdTrialPolicy.PlaylistGuid,new InfiniteDungeonPlaylist{Guid=new(){Value=ThirdTrialPolicy.PlaylistGuid},Path="foreign"});break;
                case "sameShapeForeign":
                    var restored=p.Copy();Check(Snapshot(restored)==Snapshot(p)&&restored.Pointer!=p.Pointer,"deserialized foreign blob matches complete own shape but has new identity");
                    b.Frame.DynamicAssetDB.Assets.Add(ThirdTrialPolicy.PlaylistGuid,restored);break;
                case "playlistAdd":b.Frame.FailPlaylistAdd=true;break;
                case "afterAdd":b.Frame.FailPlaylistAfterAdd=true;break;
                case "readback":b.Frame.WrongPlaylistReadback=true;break;
                case "pointerReadback":b.Frame.DifferentPointerReadback=true;break;
                case "lootFlag":b.Original.DisableLootDrops=false;break;
                case "healingFlag":b.Original.DisableHealingItemsUsage=true;break;
                case "experienceFlag":b.Original.DisableExperienceGain=true;break;
                case "respite":b.Original.RespiteRoomRange.X=4;break;
            }
            string before=Snapshot(b.Original);
            b.CheckFailed(failure,failure is "afterAdd" or "readback" or "pointerReadback" or "publish");
            Check(Snapshot(b.Original)==before,"initialization failure leaves mainline second source untouched "+failure);
            foreach(long sourceGuid in new[]{ThirdTrialPolicy.PlaylistGuid,ThirdTrialPolicy.TemplatePlaylistGuid,ThirdTrialPolicy.OriginalPlaylistGuid,1485587607739743779L,0L})
            {
                b.Frame.SourcePlaylist=sourceGuid;Check((bool)Invoke("BeforeRuleUpdate",b.Frame)==(sourceGuid!=ThirdTrialPolicy.PlaylistGuid),"native clear/progress blocked only own source on "+failure);
            }
            Check(!ThirdTrialRuntime.TryGetNpc(b.Frame,last.Key,out _),"failed entry cannot expose NPC "+failure);
        }
        var failedRepeat=new Fixture();failedRepeat.Install();failedRepeat.Init();failedRepeat.FailAssets=true;failedRepeat.Init();
        Check(!ThirdTrialRuntime.Enabled&&!ThirdTrialRuntime.IsFrameReady(failedRepeat.Frame)&&EnemyRestorationRuntime.Publications==1,
            "Failed repeated bootstrap does not republish or grant readiness from a prior snapshot");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Il2Cpp")||a.GetName().Name.StartsWith("Unity")),"no native/game assemblies loaded");
        Console.WriteLine("Actual ThirdTrialRuntime source managed tests passed: "+checks+"; no game DLL references or execution.");
    }
    static void CheckOfficialInitializationOrder()
    {
        var f=new Fixture();f.Install();
        var installed=TrialRepairLog.Hooks.Single(h=>h.Postfix=="AfterInit");
        Console.WriteLine("INIT ORDER actual production hook: "+installed.TargetType.Name+"."+installed.Method+" postfix "+installed.Postfix);
        TrialRepairLog.Dispatch(typeof(StartOfFrameSystem),"OnInit",f.Frame,()=>new StartOfFrameSystem().OnInit(f.Frame));
        bool noEarlyAssets=f.Frame.DynamicAssetDB.Assets.Count==0&&f.Frame.PrivateAssetsPrepared==0&&f.Frame.Exports==0&&f.Frame.PlaylistAdds==0;
        bool exported=false;Exception error=null;
        try
        {
            TrialRepairLog.Dispatch(typeof(InitializeContentSystem),"OnInit",f.Frame,()=>new InitializeContentSystem().OnInit(f.Frame));
            exported=true;
        }
        catch(Exception caught){error=caught;}
        Console.WriteLine("INIT ORDER official content exported="+exported+"; pooled="+f.Frame.OfficialPooledExports+"; error="+error?.Message);
        Check(exported,"official ExportRandomAssets must finish before bootstrap; observed: "+error);
        Check(noEarlyAssets,"StartOfFrame cannot create restored assets, export blocks or register playlist before content");
        Check(installed.TargetType==typeof(InitializeContentSystem),"bootstrap registered at verified content completion");
        var events=f.Frame.InstanceDataStorage.Events;
        Check(events.IndexOf("official-content-complete")<events.IndexOf("assets"),"all private asset creation after native content export");
        Check(f.Frame.PrivateAssetsPrepared==43,"all forty-three graph assets prepared only after native export");
        Check(f.Frame.OfficialContentExports==1&&f.Frame.OfficialPooledExports==1,"unrelated official pooled exports continue");
        Check(f.Frame.InstanceDataStorage.m_storageTeamData.m_storage.Count==1,"official static Team reused without duplicate Add");
        var team=f.Frame.InstanceDataStorage.m_storageTeamData.m_storage[0];
        Check(team.Pointer==f.Frame.Assets[f.Catalog.TeamGuid].Pointer,"reuse exact official static Team pointer");
        Check(ThirdTrialRuntime.IsFrameReady(f.Frame)&&f.Frame.PlaylistAdds==1,"bootstrap ready after content");
        int writes=f.Frame.InstanceDataStorage.Writes;
        TrialRepairLog.Dispatch(typeof(StartOfFrameSystem),"OnInit",f.Frame,()=>new StartOfFrameSystem().OnInit(f.Frame));
        Check(f.Frame.InstanceDataStorage.Writes==writes,"start-of-frame no bootstrap side effects");
    }
}
