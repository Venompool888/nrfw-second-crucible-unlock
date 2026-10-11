using CrucibleUnlock;
int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
void Throws(Action action, string name) { bool threw = false; try { action(); } catch (InvalidOperationException) { threw = true; } Check(threw,name); }
var catalog = EnemyRestorationCatalog.Load();
var rooms = ThirdTrialCatalog.LoadEmbedded();
Check(ThirdTrialFailurePolicy.AllowRuleUpdate(ThirdTrialPolicy.TemplatePlaylistGuid,false),"third failure leaves second trial progression active");
Check(ThirdTrialFailurePolicy.AllowRuleUpdate(1485587607739743779L,false),"third failure leaves first trial progression active");
Check(ThirdTrialFailurePolicy.AllowRuleUpdate(ThirdTrialPolicy.PlaylistGuid,true),"available third trial uses official clear");
Check(!ThirdTrialFailurePolicy.AllowRuleUpdate(ThirdTrialPolicy.PlaylistGuid,false),"partial spawn failure cannot earn a clear or reward");
Check(rooms.Stages.Length == 10 && rooms.Stages.Sum(x=>x.SpawnerGuids.Length)==121,"ten safe arena content sets");
Check(rooms.Stages.Select(x=>x.ProfileKey).SequenceEqual(catalog.Profiles.Select(x=>x.Key)),"one restored profile per ordered room");
var backend = new Storage(catalog.Profiles.Length);
try { ThirdTrialStoragePolicy.Ensure(catalog,backend); } catch (NotImplementedException) { }
Check(backend.Ready.Count==10,"all ten default runtime attachment blocks are wired");
int writes = backend.Writes;
ThirdTrialStoragePolicy.Ensure(catalog,backend);
Check(backend.Writes==writes,"repeated ensure does not append duplicates");
var collision=new Storage(10) { Collision = catalog.Profiles[9].Key };
Throws(()=>ThirdTrialStoragePolicy.Ensure(catalog,collision),"late collision refuses whole batch");
Check(collision.Writes==0,"preflight collision does not mutate any cache");
var fail=new Storage(10) { FailBuild = catalog.Profiles[9].Key };
Throws(()=>ThirdTrialStoragePolicy.Ensure(catalog,fail),"attachment export failure is visible");
Check(fail.Ready.Count==0,"all attachment blocks prepared before any is exposed");
var mismatch=new Storage(10) { BadReadback = true };
Throws(()=>ThirdTrialStoragePolicy.Ensure(catalog,mismatch),"cache readback failure never claims ready");
var duplicate=EnemyRestorationCatalog.Load(); duplicate.Profiles[9]=duplicate.Profiles[0];
Throws(()=>ThirdTrialStoragePolicy.Ensure(duplicate,new Storage(10)),"duplicate NPC refused before mutation");
Console.WriteLine($"Third trial storage tests: {count} passed");
sealed class Storage : IThirdTrialStorageBackend
{
    readonly int expected;
    public HashSet<string> Ready=new();
    readonly HashSet<string> preflight=new(),dependencies=new();
    public string Collision, FailBuild; public bool BadReadback; public int Writes;
    public Storage(int expected) { this.expected=expected; }
    public void Preflight(EnemyRestorationProfile p) { if(p.Key==Collision)throw new InvalidOperationException("foreign collision"); preflight.Add(p.Key); }
    public void EnsureDependencies(EnemyRestorationProfile p) { if(preflight.Count!=expected)throw new Exception("dependencies were inserted before full preflight");if(dependencies.Add(p.Key))Writes++; }
    public object BuildAttachments(EnemyRestorationProfile p) { if(dependencies.Count!=expected)throw new Exception("partial graph exposed"); if(p.Key==FailBuild)throw new InvalidOperationException("export failure"); return p.Key; }
    public void EnsureAttachments(EnemyRestorationProfile p, object data) { if(Ready.Add((string)data))Writes++; }
    public bool IsReady(EnemyRestorationProfile p) => !BadReadback && Ready.Contains(p.Key);
}
