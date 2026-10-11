using System.Reflection;
using System.Text.Json;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Wwise.MoonWwiseTypes;
using Il2CppQuantum;

var failures = new List<string>(); int checks = 0;
void Check(bool condition, string label) { ++checks; if (!condition) failures.Add(label); }
SwitchReference Voice(long ptr, string guid) => new(ptr, guid);
var donorBruiser = Voice(11, "donor-bruiser");
var donorAxe = Voice(12, "donor-axe-bruiser");
var family119a = Voice(1191, "inquisitor-bruiser-a");
var family119b = Voice(1192, "inquisitor-bruiser-b");
var family121 = Voice(1211, "dead-tide-bruiser");
var inquisitor = Voice(311, "inquisitor");
var keeper = Voice(211, "keeper");
var deadTide = Voice(1201, "dead-tide");
var pools = WwiseGlobalDefinitions.Instance.Voices.Pools;
WwiseVoiceSelectionPool Pool(params SwitchReference[] voices) => new() { VoiceSwitchSelects = new(voices) };
pools[119] = Pool(family119a, family119b);
pools[121] = Pool(family121);
pools[31] = Pool(inquisitor);
pools[21] = Pool(keeper);
pools[120] = Pool(deadTide);
var catalog = new EnemyRestorationCatalog { Profiles = new EnemyRestorationProfile[10] };
for (int i = 0; i < 10; ++i) catalog.Profiles[i] = new() { Index = i, Key = "profile" + i, CharacterType = 31 };
catalog.Profiles[2].CharacterType = 119;
catalog.Profiles[9].CharacterType = 121;
catalog.Profiles[3].CharacterType = 21;
catalog.Profiles[7].CharacterType = 120;
EnemyRestorationRuntime.Catalog = catalog;
long nextView = 100;
CharacterView View(int index, ulong entity, SwitchReference before)
{
    var view = new CharacterView { Pointer = new IntPtr(++nextView), Profile = catalog.Profiles[index],
        EntityRef = new EntityRef { Raw = entity }, PredictedFrame = new Frame { AttachedType = catalog.Profiles[index].CharacterType },
        PrimarySoundHost = new SoundHost() };
    view.CustomVoiceSwitch = before;
    return view;
}
var harmony = new HarmonyLib.Harmony();
EnemyRestorationVoiceRepair.Install(harmony);
Check(TrialRepairLog.Hooks.SequenceEqual(new[] { "CharacterView.FulfillVoiceAssignment", "CharacterView.OnUnboundFromEntity",
    "CharacterView.OnReturnedToPool", "EntityView.OnDestroy" }), "Exact native method ABI and cleanup hooks");
EnemyRestorationVoiceRepair.ResumeAfterViewPublication();

var bruiser = View(2, 201, donorBruiser);
EnemyRestorationVoiceRepair.Apply(bruiser, catalog);
Check(ReferenceEquals(bruiser.CustomVoiceSwitch, family119a), "Inquisitor bruiser donor switch replaced by official family");
int calls = pools[119].Calls;
EnemyRestorationVoiceRepair.Apply(bruiser, catalog);
Check(ReferenceEquals(bruiser.CustomVoiceSwitch, family119a) && pools[119].Calls == calls,
    "Repeated callback on same bind keeps selected variant");
EnemyRestorationVoiceRepair.Release(bruiser);
Check(ReferenceEquals(bruiser.CustomVoiceSwitch, donorBruiser), "Owned bruiser switch restored on release");

var axe = View(9, 901, donorAxe);
EnemyRestorationVoiceRepair.Apply(axe, catalog);
Check(ReferenceEquals(axe.CustomVoiceSwitch, family121), "Dead-tide bruiser donor switch replaced by official family");
EnemyRestorationVoiceRepair.Release(axe);
Check(ReferenceEquals(axe.CustomVoiceSwitch, donorAxe), "Dead-tide donor value restored");

var shared = View(0, 1001, null);
EnemyRestorationVoiceRepair.Apply(shared, catalog);
Check(ReferenceEquals(shared.CustomVoiceSwitch, inquisitor), "Shared humanoid prefab first family selected");
shared.Profile = catalog.Profiles[3]; shared.EntityRef = new EntityRef { Raw = 1002 }; shared.PredictedFrame.AttachedType = 21;
EnemyRestorationVoiceRepair.Apply(shared, catalog);
Check(ReferenceEquals(shared.CustomVoiceSwitch, keeper), "Pooled shared prefab changes to next private family's voice");
EnemyRestorationVoiceRepair.Release(shared);
Check(shared.CustomVoiceSwitch == null, "Shared prefab release restores original empty switch");

var foreign = View(7, 2001, deadTide); foreign.Proof = false;
EnemyRestorationVoiceRepair.Apply(foreign, catalog);
Check(ReferenceEquals(foreign.CustomVoiceSwitch, deadTide), "Unproven foreign actor untouched");
foreign.Proof = true; foreign.PredictedFrame.AttachedType = 21;
EnemyRestorationVoiceRepair.Apply(foreign, catalog);
Check(ReferenceEquals(foreign.CustomVoiceSwitch, deadTide), "Attachment/profile type mismatch untouched");
foreign.PredictedFrame.AttachedType = 120; foreign.PredictedFrame.HasComponent = false;
EnemyRestorationVoiceRepair.Apply(foreign, catalog);
Check(ReferenceEquals(foreign.CustomVoiceSwitch, deadTide), "Missing live attachment untouched");

var noPool = View(1, 3001, donorBruiser);
EnemyRestorationVoiceRepair.Apply(noPool, catalog);
Check(ReferenceEquals(noPool.CustomVoiceSwitch, inquisitor), "Official valid pool used for ordinary type");
EnemyRestorationVoiceRepair.Release(noPool);
noPool.Profile.CharacterType = 118; noPool.PredictedFrame.AttachedType = 118;
EnemyRestorationVoiceRepair.Apply(noPool, catalog);
Check(ReferenceEquals(noPool.CustomVoiceSwitch, donorBruiser), "Absent pool fails without forced assignment");

var changed = View(2, 4001, donorBruiser);
EnemyRestorationVoiceRepair.Apply(changed, catalog);
var outsider = Voice(999, "other-system"); changed.CustomVoiceSwitch = outsider;
EnemyRestorationVoiceRepair.Apply(changed, catalog);
EnemyRestorationVoiceRepair.Apply(changed, catalog);
EnemyRestorationVoiceRepair.Apply(changed, catalog);
Check(ReferenceEquals(changed.CustomVoiceSwitch, outsider), "Same-bind foreign switch change preserved on repeated callback");
EnemyRestorationVoiceRepair.Release(changed);
Check(ReferenceEquals(changed.CustomVoiceSwitch, outsider), "Foreign switch replacement preserved on cleanup");

var after = View(2, 5001, donorBruiser);
EnemyRestorationVoiceRepair.Apply(after, catalog);
var callbacks = typeof(EnemyRestorationVoiceRepair).GetMethod("AfterFulfill", BindingFlags.Static|BindingFlags.NonPublic);
callbacks?.Invoke(null, new object[] { after });
Check(TrialRepairLog.Logs.Any(x => x.Contains("profile=profile2") && x.Contains("before=") && x.Contains("after=") && x.Contains("host=0x9")),
    "Bounded post-native presence diagnostic records profile, switch, and host");
int reads = CharacterView.VoiceReads;
EnemyRestorationVoiceRepair.StopWithoutUnityReads();
Check(CharacterView.VoiceReads == reads, "Stop itself makes no Unity switch reads");
EnemyRestorationVoiceRepair.Release(after);
EnemyRestorationVoiceRepair.Apply(after, catalog);
Check(ReferenceEquals(after.CustomVoiceSwitch, donorBruiser), "Stopped lifecycle cleanup restores owned donor value");
var sharedStopped = View(0, 6001, null);
EnemyRestorationVoiceRepair.ResumeAfterViewPublication();
EnemyRestorationVoiceRepair.Apply(sharedStopped, catalog);
EnemyRestorationVoiceRepair.StopWithoutUnityReads();
var returned = typeof(EnemyRestorationVoiceRepair).GetMethod("BeforeReturned", BindingFlags.Static|BindingFlags.NonPublic);
returned?.Invoke(null, new object[] { sharedStopped });
Check(sharedStopped.CustomVoiceSwitch == null, "Stopped return clears owned private voice before ordinary pool reuse");
sharedStopped.Proof = false; sharedStopped.EntityRef = new EntityRef { Raw = 6002 };
var ordinary = Voice(51, "ordinary-humanoid");
if (sharedStopped.CustomVoiceSwitch == null) sharedStopped.CustomVoiceSwitch = ordinary; // native valid-switch precedence
Check(ReferenceEquals(sharedStopped.CustomVoiceSwitch, ordinary), "Ordinary next bind can select its own native voice");
EnemyRestorationVoiceRepair.ResumeAfterViewPublication();
after.EntityRef = new EntityRef { Raw = 5002 };
after.Profile = catalog.Profiles[3]; after.PredictedFrame.AttachedType = 21;
EnemyRestorationVoiceRepair.Apply(after, catalog);
Check(ReferenceEquals(after.CustomVoiceSwitch, keeper), "Retained lease restored on first bind after resume");
EnemyRestorationVoiceRepair.Release(after);
Check(ReferenceEquals(after.CustomVoiceSwitch, donorBruiser), "Resumed binding returns original donor switch");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Il2Cpp") || a.GetName().Name.StartsWith("Unity")),
    "No game assemblies loaded by managed test");

var output = new { checks, failures, passed = failures.Count == 0,
    actual_source_compiled = Environment.GetEnvironmentVariable("NRFW_BASELINE") != "1", managed_fakes_only = true };
if (args.Length != 0) File.WriteAllText(args[0], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
foreach (var failure in failures) Console.WriteLine("FAIL: " + failure);
Console.WriteLine($"Voice repair actual-source managed tests: {checks} checks, {failures.Count} failures");
return failures.Count == 0 ? 0 : 1;
