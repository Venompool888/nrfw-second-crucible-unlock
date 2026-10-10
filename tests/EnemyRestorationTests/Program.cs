using CrucibleUnlock;

int checks = 0;
void Check(bool ok, string detail) { checks++; if (!ok) throw new Exception(detail); }
var catalog = EnemyRestorationCatalog.Load();
Check(catalog.Profiles.Length == 10, "embedded catalog must supply all approved enemies");
Check(EnemyRestorationCatalog.DynamicGuid(0) == -4611686018427387904L, "explicit dynamic GUID tag");
Check(EnemyRestorationCatalog.DynamicGuid(4000000000000000000L) == -611686018427387904L, "large local ID conversion");
Check(EnemyRestorationRegistration.KeepAction(6, new long[] {7}, new long[] {11}), "good combo retained");
Check(!EnemyRestorationRegistration.KeepAction(11, Array.Empty<long>(), new long[] {11}), "bad primary removed");
Check(!EnemyRestorationRegistration.KeepAction(6, new long[] {11}, new long[] {11}), "bad required combo removes entry");
var backend = new MemoryBackend();
var definitions = EnemyRestorationRegistration.Register(catalog, backend);
Check(definitions.Count == 43, "all roles really registered, including three private weapons");
foreach (var p in catalog.Profiles) {
    var npc = backend.Assets[p.NpcGuid];
    Check(npc.Refs["host"] == p.HostGuid && npc.Refs["controller"] == p.ControllerGuid && npc.Refs["view"] == p.ViewGuid, "NPC connections " + p.Key);
    Check(backend.Assets[p.HostGuid].Refs["view"] == p.ViewGuid, "host view " + p.Key);
    Check(backend.Assets[p.ControllerGuid].Refs["weapon"] == (p.BlockedActions.Length > 0 ? p.PrivateWeaponGuid : p.WeaponGuid), "original/private weapon " + p.Key);
    Check(backend.Assets[p.ControllerGuid].Refs["offhand"] == p.OffhandGuid, "shield connection " + p.Key);
}
int originalCount = backend.RegisterCalls;
EnemyRestorationRegistration.Register(catalog, backend);
Check(backend.RegisterCalls == originalCount, "initialization repeated without replacing assets");
var collision = new MemoryBackend();
collision.Assets[catalog.Profiles[0].HostGuid] = new Token { Path = "another-mod" };
try { EnemyRestorationRegistration.Register(catalog, collision); throw new Exception("collision accepted"); }
catch (InvalidOperationException) { Check(collision.RegisterCalls == 0, "collision prevented all writes"); }
var fault = new MemoryBackend { FailAfterInsert = 7 };
try { EnemyRestorationRegistration.Register(catalog, fault); throw new Exception("failure swallowed"); }
catch (InvalidOperationException) { Check(fault.Assets.Count == 0, "rollback includes partially registered throwing asset"); }
var incomplete = new MemoryBackend();
EnemyRestorationRegistration.Register(catalog, incomplete);
incomplete.Assets.Remove(catalog.Profiles[0].NpcGuid);
int priorWrites = incomplete.RegisterCalls;
try { EnemyRestorationRegistration.Register(catalog, incomplete); throw new Exception("partial graph accepted"); }
catch (InvalidOperationException) { Check(incomplete.RegisterCalls == priorWrites && incomplete.Assets.Count == 42, "partial existing graph is left intact, with no repair writes"); }
var prepareFailure = new MemoryBackend { FailClone = 4 };
try { EnemyRestorationRegistration.Register(catalog, prepareFailure); throw new Exception("clone failure swallowed"); }
catch (InvalidOperationException) { Check(prepareFailure.RegisterCalls == 0 && prepareFailure.Assets.Count == 0, "all graph preparation completes before the first write"); }
Check(backend.Assets[catalog.Profiles[0].NpcGuid].SourceGuid == 1084336240379028843L, "NPC clone uses the actual halberd combat donor");
Console.WriteLine($"PASS: {checks} enemy runtime registration checks; no game assemblies loaded.");

sealed class Token {
    public string Path;
    public Dictionary<string,long> Refs = new();
    public long SourceGuid;
}
sealed class MemoryBackend : IEnemyRestorationBackend {
    public Dictionary<long,Token> Assets = new();
    public int RegisterCalls, FailAfterInsert, FailClone, CloneCalls;
    public object Find(long guid) => Assets.TryGetValue(guid, out var t) ? t : null;
    public object Clone(long sourceGuid, EnemyAssetRole role) {
        if (++CloneCalls == FailClone) throw new InvalidOperationException("injected clone failure");
        return new Token { SourceGuid=sourceGuid };
    }
    public object CreateView() => new Token();
    public void Configure(object obj, EnemyAssetDefinition def) {
        var t=(Token)obj;
        t.Path=def.Path;
        var p=def.Profile;
        if(def.Role==EnemyAssetRole.Host) t.Refs["view"]=p.ViewGuid;
        if(def.Role==EnemyAssetRole.Controller) {
            t.Refs["weapon"]=p.BlockedActions.Length>0?p.PrivateWeaponGuid:p.WeaponGuid;
            t.Refs["offhand"]=p.OffhandGuid;
        }
        if(def.Role==EnemyAssetRole.Npc) {
            t.Refs["host"]=p.HostGuid;t.Refs["controller"]=p.ControllerGuid;t.Refs["view"]=p.ViewGuid;
        }
    }
    public bool Matches(object obj, EnemyAssetDefinition def) => ((Token)obj).Path == def.Path;
    public void Register(object obj, EnemyAssetDefinition def) {
        Assets.Add(def.Guid,(Token)obj);
        if(++RegisterCalls==FailAfterInsert) throw new InvalidOperationException("injected post-insert failure");
    }
    public void Remove(EnemyAssetDefinition def) { Assets.Remove(def.Guid); }
}
