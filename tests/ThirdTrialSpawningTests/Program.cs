using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CrucibleUnlock;
using Il2CppQuantum;
using Il2CppQuantum.StreamingGraph;

static class Program
{
    static int checks;
    static readonly List<string> Failures=new();
    static readonly List<string> Logs=TrialRepairLog.Messages;
    static void Check(bool valid,string why) {++checks;if(!valid)Failures.Add(why);}
    sealed class Fixture
    {
        public readonly Frame Frame=new();
        public readonly EntityRef Entity=new(){Raw=501};
        public readonly EntityRef Origin=new(){Raw=401};
        public readonly DynamicNpcSpawnerData Instance=new(){Guid=new(){Value=9000001}};
        public readonly ThirdTrialStage Stage;
        public IntegrationGroupComponent Group {get=>(IntegrationGroupComponent)Frame.Components[(Entity.Raw,typeof(IntegrationGroupComponent))];set=>Frame.Components[(Entity.Raw,typeof(IntegrationGroupComponent))]=value;}
        public IntegrationGroupComponent SourceGroup {get=>(IntegrationGroupComponent)Frame.Components[(Origin.Raw,typeof(IntegrationGroupComponent))];set=>Frame.Components[(Origin.Raw,typeof(IntegrationGroupComponent))]=value;}
        public bool Ready {get;set;}=true;
        public Fixture(int stageIndex=0,bool withGeneric=false)
        {
            Stage=ThirdTrialCatalog.LoadEmbedded().Stages[stageIndex];
            var chunk=new RuntimeStreamingNode{Kind=StreamingNodeKind.Chunk,Id=new(){Value=Stage.ChunkGuid},Entity=new(){Raw=201}};
            var bucket=new RuntimeStreamingNode{Kind=StreamingNodeKind.Bucket,Id=new(){Value=Stage.BucketGuid},Entity=new(){Raw=202},Parent=chunk};
            var nugget=new RuntimeStreamingNode{Kind=StreamingNodeKind.Nugget,Id=new(){Value=Stage.NuggetGuid},Entity=new(){Raw=203},Parent=bucket};
            Group=new(){IntegrationId=7,StreamingNodeDataIndex=new(){Node=nugget}};
            SourceGroup=Group;
            Frame.Components[(Entity.Raw,typeof(IntegrationGroupComponent))]=Group;
            Frame.Components[(Entity.Raw,typeof(IdentityComponent))]=new IdentityComponent{Scope=Origin};
            Frame.Components[(Origin.Raw,typeof(IntegrationGroupComponent))]=SourceGroup;
            Frame.Components[(Origin.Raw,typeof(DynamicSpawnerComponent))]=new DynamicSpawnerComponent{DynamicSpawnerData=new(){Data=new(){Guid=Stage.SpawnerGuids[0]}}};
            // Native IntegrateNpcSpawner fills SpawnerData only AFTER Spawn returns.
            Frame.Components[(Entity.Raw,typeof(DynamicNpcSpawnerComponent))]=new DynamicNpcSpawnerComponent();
            if(withGeneric)Frame.Components[(Entity.Raw,typeof(DynamicSpawnerComponent))]=new DynamicSpawnerComponent{DynamicSpawnerData=new(){Data=new(){Guid=Stage.SpawnerGuids[0]}}};
            Frame.Integration.Nodes.Add(7,nugget);
            Frame.Playlist.Add(new(){Chunk=new(){Id=new(){Value=Stage.ChunkGuid}},Nuggets=new(){Values=Stage.EntryNuggetGuids.Select(g=>new AssetRefNuggetData{Id=new(){Value=g}}).ToList()}});
        }
        public (bool Allow,long Npc,long Result,string[] Reasons) Call()
        {
            ThirdTrialRuntime.Failures.Clear();ThirdTrialRuntime.Lookups.Clear();ThirdTrialRuntime.Ready=Ready;
            var npc=new AssetRefNpcData{Id=new(){Value=123456}};var result=new EntityRef{Raw=987};
            var parameters=TrialRepairLog.Callback.GetParameters();
            var values=parameters.Select(p=>p.Name switch {"__instance"=>(object)Instance,"__0"=>Frame,"__1"=>Entity,"__2"=>npc,"__result"=>result,_=>throw new Exception("unknown callback parameter "+p.Name)}).ToArray();
            bool allow=(bool)TrialRepairLog.Callback.Invoke(null,values);
            npc=(AssetRefNpcData)values[Array.FindIndex(parameters,p=>p.Name=="__2")];
            result=(EntityRef)values[Array.FindIndex(parameters,p=>p.Name=="__result")];
            return(allow,npc.Id.Value,result.Raw,ThirdTrialRuntime.Failures.ToArray());
        }
        public long CompleteNative(long npc,long result,long? storedNpc=null)
        {
            var component=(DynamicNpcSpawnerComponent)Frame.Components[(Entity.Raw,typeof(DynamicNpcSpawnerComponent))];
            component.NpcData=new(){Id=new(){Value=storedNpc??npc}};
            if(TrialRepairLog.PostCallback==null)return result;
            var args=new object[]{Frame,Entity,new AssetRefNpcData{Id=new(){Value=npc}},new EntityRef{Raw=result}};
            TrialRepairLog.PostCallback.Invoke(null,args);
            return ((EntityRef)args[3]).Raw;
        }
    }
    static int Main(string[] args)
    {
        ThirdTrialSpawning.Install(Logs.Add,Logs.Add);
        Check(TrialRepairLog.Callback!=null,"Actual Install resolves exact eight native Spawn parameter types and EntityRef return");
        Check(TrialRepairLog.PostCallback!=null,"Actual Install registers read-only result postfix on same Spawn hook");
        NativeLayout.GroupSize=7;var wrongLayout=new Fixture();var layoutCall=wrongLayout.Call();NativeLayout.GroupSize=8;
        Check(!layoutCall.Allow&&layoutCall.Reasons.Any(r=>r.Contains("phase=component-layout")&&r.Contains("layout mismatch"))&&Frame.NativeGets==0,
            "Native layout mismatch is rejected before any raw component pointer read");
        for(int i=0;i<10;i++)
        {
            var fixture=new Fixture(i);var call=fixture.Call();
            Check(call.Allow&&call.Npc==ThirdTrialRuntime.Replacement(fixture.Stage.ProfileKey)&&call.Reasons.Length==0,
                "Native-shaped new NPC spawner without generic DynamicSpawnerComponent must replace profile "+i);
            long result=call.Allow?fixture.CompleteNative(call.Npc,600+i):0;
            Check(result==600+i&&Logs.Any(m=>m.Contains("[third-spawn] RESULT expectedNpc="+call.Npc+"; storedNpc="+call.Npc+"; entity="+(600+i))),
                "Postfix reads native stored replacement without altering result profile "+i);
        }
        var generic=new Fixture(withGeneric:true);var genericCall=generic.Call();
        Check(genericCall.Allow&&genericCall.Npc==ThirdTrialRuntime.Replacement(generic.Stage.ProfileKey),"Legacy synthetic all-components fixture can pass");
        var other=new Fixture();other.Frame.Source=ThirdTrialPolicy.TemplatePlaylistGuid;var passed=other.Call();
        Check(passed.Allow&&passed.Npc==123456&&other.Frame.ComponentReads.Count==0,"Original second source passes without component reads or NPC modification");
        var missing=new Fixture();missing.Frame.Components.Remove((missing.Entity.Raw,typeof(IntegrationGroupComponent)));var blocked=missing.Call();
        Check(!blocked.Allow&&blocked.Result==0&&blocked.Npc==123456&&blocked.Reasons.Length==1,"Missing integration ownership stays fail closed");
        var preload=new Fixture(7);preload.Frame.IsVerified=false;preload.Frame.PlayerCurrentChunk=ThirdTrialCatalog.LoadEmbedded().Stages[0].ChunkGuid;
        var predicted=preload.Call(); Check(predicted.Allow&&predicted.Npc==ThirdTrialRuntime.Replacement(preload.Stage.ProfileKey),"Predicted/preloaded stage uses owning chunk profile despite player's different current room");
        foreach(string failure in new[]{"npc","identity","scope0","selfScope","originSpawner","originGroup","groupId","ownerId","ownerEntity","mapping","unknownGuid","sourceData","entry","ready"})
        {
            var f=new Fixture(withGeneric:true);
            switch(failure)
            {
                case "npc":f.Frame.Components.Remove((f.Entity.Raw,typeof(DynamicNpcSpawnerComponent)));break;
                case "identity":f.Frame.Components.Remove((f.Entity.Raw,typeof(IdentityComponent)));break;
                case "scope0":f.Frame.Components[(f.Entity.Raw,typeof(IdentityComponent))]=new IdentityComponent();break;
                case "selfScope":f.Frame.Components[(f.Entity.Raw,typeof(IdentityComponent))]=new IdentityComponent{Scope=f.Entity};break;
                case "originSpawner":f.Frame.Components.Remove((f.Origin.Raw,typeof(DynamicSpawnerComponent)));break;
                case "originGroup":f.Frame.Components.Remove((f.Origin.Raw,typeof(IntegrationGroupComponent)));break;
                case "groupId":{var g=f.SourceGroup;g.IntegrationId=8;f.SourceGroup=g;break;}
                case "ownerId":{var g=f.SourceGroup;g.StreamingNodeDataIndex.Node=new(){Id=new(){Value=123},Entity=f.Group.StreamingNodeDataIndex.Node.Entity};f.SourceGroup=g;break;}
                case "ownerEntity":{var g=f.SourceGroup;g.StreamingNodeDataIndex.Node=new(){Id=f.Group.StreamingNodeDataIndex.Node.Id,Entity=new(){Raw=123}};f.SourceGroup=g;break;}
                case "mapping":f.Frame.Integration.Nodes.Clear();break;
                case "unknownGuid":((DynamicSpawnerComponent)f.Frame.Components[(f.Origin.Raw,typeof(DynamicSpawnerComponent))]).DynamicSpawnerData.Data.Guid=123;break;
                case "sourceData":((DynamicSpawnerComponent)f.Frame.Components[(f.Origin.Raw,typeof(DynamicSpawnerComponent))]).DynamicSpawnerData.Data=null;break;
                case "entry":f.Frame.Playlist[0].Nuggets.Values.RemoveAt(0);break;
                case "ready":f.Ready=false;break;
            }
            var rejected=f.Call();
            Check(!rejected.Allow&&rejected.Result==0&&rejected.Npc==123456&&rejected.Reasons.Length==1,"Strict scope/readiness remains closed: "+failure);
        }
        var unknown=new Fixture();unknown.Frame.ThrowSource=true;var unknownCall=unknown.Call();
        Check(unknownCall.Allow&&unknownCall.Npc==123456&&unknown.Frame.ComponentReads.Count==0,"Unknown source identity passes native guard without scope reads");
        var repeated=new Fixture();int logCount=Logs.Count(m=>m.Contains("[third-spawn] REPLACED"));repeated.Call();repeated.Call();
        Check(Logs.Count(m=>m.Contains("[third-spawn] REPLACED"))==logCount,"Replacement diagnostics bounded to once per profile");
        var phase=new Fixture();phase.Frame.IsVerified=false;var phaseCall=phase.Call();if(phaseCall.Allow)phase.CompleteNative(phaseCall.Npc,701);
        Check(Logs.Any(m=>m.Contains("REPLACED")&&m.Contains("profile="+phase.Stage.ProfileKey)&&m.Contains("verified=False"))&&
            Logs.Any(m=>m.Contains("RESULT expectedNpc="+phaseCall.Npc)&&m.Contains("verified=False")),"Predicted phase has separate replacement/result records from verified phase");
        int results=Logs.Count(m=>m.Contains("[third-spawn] RESULT"));if(phaseCall.Allow)phase.CompleteNative(phaseCall.Npc,702);
        Check(Logs.Count(m=>m.Contains("[third-spawn] RESULT"))==results,"Repeated result diagnostics bounded per profile and phase");
        var logError=new Fixture(8);logError.Frame.IsVerified=false;var errorCall=logError.Call();logError.Frame.ThrowNpcGet=true;
        Check((errorCall.Allow?logError.CompleteNative(errorCall.Npc,703):0)==703&&ThirdTrialRuntime.Failures.Count==0&&Logs.Count(m=>m.Contains("[third-spawn] LOGERROR"))==1,
            "Diagnostic read exception logs once without changing native result or failing runtime");
        if(errorCall.Allow)logError.CompleteNative(errorCall.Npc,704);Check(Logs.Count(m=>m.Contains("[third-spawn] LOGERROR"))==1,"Diagnostic errors remain bounded");
        logError.Frame.ThrowNpcGet=false;
        Check((errorCall.Allow?logError.CompleteNative(errorCall.Npc,708):0)==708&&Logs.Any(m=>m.Contains("RESULT expectedNpc="+errorCall.Npc)&&m.Contains("entity=708")),
            "Initial diagnostic read failure does not consume success bit; later native result can be recorded");
        var mismatch=new Fixture(9);mismatch.Frame.IsVerified=false;var mismatchCall=mismatch.Call();
        Check((mismatchCall.Allow?mismatch.CompleteNative(mismatchCall.Npc,705,42):0)==705&&ThirdTrialRuntime.Failures.Count==0&&
            Logs.Any(m=>m.Contains("RESULT expectedNpc="+mismatchCall.Npc+"; storedNpc=42; entity=705")),
            "Result diagnostics actually read stored field, exposing mismatch without changing native result or runtime");
        var nullComponent=new Fixture();nullComponent.Frame.NullDynamicGet=true;var nullCall=nullComponent.Call();
        Check(!nullCall.Allow&&nullCall.Reasons.Any(r=>r.Contains("phase=source-component")&&r.Contains("Has=true/Get=null")),
            "Has=true/Get=null class projection fails closed with exact component phase diagnostic");
        var nullEntry=new Fixture();nullEntry.Frame.Playlist[0]=null;var nullEntryCall=nullEntry.Call();
        Check(!nullEntryCall.Allow&&nullEntryCall.Reasons.Any(r=>r.Contains("phase=generated-entry")&&r.Contains("BeforeSpawn")),
            "Null native runtime-entry projection preserves phase and full stack rather than generic NRE message");
        foreach(string pointer in new[]{"identity","newGroup","sourceGroup","sourceSpawner"})
        {
            var f=new Fixture();
            var type=pointer=="identity"?typeof(IdentityComponent):pointer=="sourceSpawner"?typeof(DynamicSpawnerComponent):typeof(IntegrationGroupComponent);
            var entity=pointer.StartsWith("source",StringComparison.Ordinal)?f.Origin:f.Entity;
            f.Frame.NullRawPointers.Add((entity.Raw,type));var rejected=f.Call();
            Check(!rejected.Allow&&rejected.Result==0&&rejected.Reasons.Any(r=>r.Contains("Has=true/Get=null")&&r.Contains("BeforeSpawn")),
                "Present component with null official pointer stays closed and preserves stack: "+pointer);
        }
        var nullNpc=new Fixture(5);nullNpc.Frame.IsVerified=false;var nullNpcCall=nullNpc.Call();
        nullNpc.Frame.NullRawPointers.Add((nullNpc.Entity.Raw,typeof(DynamicNpcSpawnerComponent)));
        Check((nullNpcCall.Allow?nullNpc.CompleteNative(nullNpcCall.Npc,709):0)==709&&ThirdTrialRuntime.Failures.Count==0,
            "Null NPC diagnostic pointer does not change native result or trial control");
        nullNpc.Frame.NullRawPointers.Clear();
        Check((nullNpcCall.Allow?nullNpc.CompleteNative(nullNpcCall.Npc,710):0)==710&&Logs.Any(m=>m.Contains("RESULT expectedNpc="+nullNpcCall.Npc)&&m.Contains("entity=710")),
            "Null NPC pointer diagnostic retries to a later valid stored result");
        ThirdTrialSpawning.StopWithoutNativeReads();var stopped=new Fixture();var stopCall=stopped.Call();
        Check(stopCall.Allow&&stopCall.Npc==123456&&stopped.Frame.ComponentReads.Count==0,"Disabled hook performs no component reads or NPC modification");
        Check(stopped.CompleteNative(ThirdTrialRuntime.Replacement(stopped.Stage.ProfileKey),706)==706&&stopped.Frame.ComponentReads.Count==0,
            "Disabled postfix performs no component reads or native result mutation");
        Check(Frame.GenericGets==0,"Production never calls Frame.Get<T> for projected or unsupported AOT components");
        Check(Frame.NativeGets>0,"Production exercises actual raw component buffers through official extension interface");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Il2Cpp")||a.GetName().Name.StartsWith("Unity")),"No game or Unity assemblies loaded");
        ThirdTrialSpawning.Dispose();
        Frame.FreeAllBuffers();
        string source=args.Length>0?args[0]:"src/CrucibleUnlock/ThirdTrialSpawning.cs";
        var report=new{source=Path.GetFullPath(source),source_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),actual_source_compiled=true,
            managed_fakes_only=true,checks,passed=Failures.Count==0,failures=Failures,
            limitation="Real blittable buffers model native component sizes/offsets; unsupported Frame.Get specializations throw. Does not execute official native pointer methods or prove live R7 spawn/argument writeback; no game DLLs."};
        if(args.Length>1)File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var failure in Failures)Console.WriteLine("FAIL: "+failure);
        Console.WriteLine($"Actual ThirdTrialSpawning tests: {checks} checks; {Failures.Count} failures; no game DLLs.");
        return Failures.Count==0?0:1;
    }
}
