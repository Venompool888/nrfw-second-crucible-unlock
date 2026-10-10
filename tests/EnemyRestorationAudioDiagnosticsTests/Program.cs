using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Wwise;
using Il2CppQuantum;
using UnityEngine;

var errors = new List<string>();
int checks = 0;
void Check(bool ok, string name) { ++checks; if (!ok) errors.Add(name); }

var catalog = new EnemyRestorationCatalog { Profiles = new EnemyRestorationProfile[10] };
var owners = new HumanoidEnemyView[10];
var clones = new GameObject[10];
var items = new EnemyItemDataBaseAsset[10];
var companions = new WwiseWeaponCompanion[10];
for (int i = 0; i < 10; ++i)
{
    var profile = new EnemyRestorationProfile
    {
        Index = i, Key = "profile-" + i, WeaponGuid = 1000 + i,
        PrivateWeaponGuid = -2000 - i, BlockedActions = i < 3 ? new long[] { 11 } : Array.Empty<long>()
    };
    catalog.Profiles[i] = profile;
    var owner = new HumanoidEnemyView { PrimarySoundHost = new SoundHost() };
    var companion = new WwiseWeaponCompanion
    {
        Owner = owner, WeaponSoundHost = new SoundHost(), AttackEvent = new DialogueEventReference()
    };
    var weapon = new EnemyWeaponView
    {
        WeaponData = new AssetRefEnemyWeaponData { Id = new AssetGuid { Value = profile.WeaponGuid } },
        AudioCompanion = companion
    };
    var clone = new GameObject { Weapon = weapon };
    owner.MainWeaponView = weapon;
    var item = new EnemyItemDataBaseAsset
    {
        AssetGuid = new AssetGuid { Value = i < 3 ? profile.PrivateWeaponGuid : profile.WeaponGuid }
    };
    EnemyRestorationOwnerScope.Owners[owner.Pointer] = profile;
    owners[i] = owner;
    clones[i] = clone;
    items[i] = item;
    companions[i] = companion;
}

void Observe(int i, bool mainHand = true) => EnemyRestorationAudioDiagnostics.ObserveEquipment(
    owners[i], clones[i], items[i], mainHand, catalog);
EnemyRestorationAudioDiagnostics.StopWithoutUnityReads();
TrialRepairLog.Messages.Clear();
EnemyRestorationOwnerScope.Reads = 0;
EnemyRestorationRuntime.CurrentViewsReady = false;
Observe(0);
Check(EnemyRestorationOwnerScope.Reads == 0 && clones[0].ComponentReads == 0 && TrialRepairLog.Messages.Count == 0,
    "not-ready callback reads no owner or Unity component");
EnemyRestorationRuntime.CurrentViewsReady = true;
Observe(0, false);
Check(EnemyRestorationOwnerScope.Reads == 0 && TrialRepairLog.Messages.Count == 0,
    "off-hand callback is ignored before owner lookup");
var foreign = new HumanoidEnemyView();
EnemyRestorationAudioDiagnostics.ObserveEquipment(foreign, clones[0], items[0], true, catalog);
Check(TrialRepairLog.Messages.Count == 0 && clones[0].ComponentReads == 0,
    "foreign owner never reaches equipment read or log");

for (int i = 0; i < 10; ++i) { Observe(i); Observe(i); }
Check(TrialRepairLog.Messages.Count == 10, "one readiness report for each of ten exact private NPC owners");
for (int i = 0; i < 10; ++i)
    Check(TrialRepairLog.Messages.Count(x => x.Contains("equipment profile=profile-" + i + ";")) == 1,
        "profile " + i + " has one report");
Check(TrialRepairLog.Messages[0].Contains("ownerMatch=True") &&
    TrialRepairLog.Messages[0].Contains("presence-only=true") &&
    TrialRepairLog.Messages[0].Contains("attackEvent=0x"), "report labels ownership and presence-only limit");
Check(items[0].AssetGuid.Value == catalog.Profiles[0].PrivateWeaponGuid &&
    companions[0].Owner.Pointer == owners[0].Pointer &&
    owners[0].MainWeaponView.Pointer == clones[0].Weapon.Pointer,
    "diagnostics leave source references unchanged");

EnemyRestorationAudioDiagnostics.StopWithoutUnityReads();
TrialRepairLog.Messages.Clear();
companions[4].Owner = owners[5];
Observe(4);
Check(TrialRepairLog.Messages.Count == 1 && TrialRepairLog.Messages[0].Contains("ownerMatch=False") &&
    TrialRepairLog.Messages[0].Contains("profile-4"), "audio companion owner mismatch is diagnosed, not repaired");
Check(companions[4].Owner.Pointer == owners[5].Pointer, "mismatched owner is unchanged");

EnemyRestorationAudioDiagnostics.StopWithoutUnityReads();
TrialRepairLog.Messages.Clear();
var originalMain = owners[6].MainWeaponView;
owners[6].MainWeaponView = owners[7].MainWeaponView;
Observe(6);
Check(TrialRepairLog.Messages.Count == 0, "postfix waits for native retained clone identity");
owners[6].MainWeaponView = originalMain;
Observe(6);
Check(TrialRepairLog.Messages.Count == 1, "first complete callback reports after an incomplete one");

EnemyRestorationAudioDiagnostics.StopWithoutUnityReads();
TrialRepairLog.Messages.Clear();
clones[2].Weapon.WeaponData.Id = new AssetGuid { Value = 777777 };
Observe(2);
Check(TrialRepairLog.Messages.Count == 0, "foreign weapon prefab binding is rejected even for exact private owner");
clones[2].Weapon.WeaponData.Id = new AssetGuid { Value = catalog.Profiles[2].WeaponGuid };
Observe(2);
Check(TrialRepairLog.Messages.Count == 1, "restored source weapon binding can report");

EnemyRestorationAudioDiagnostics.StopWithoutUnityReads();
TrialRepairLog.Messages.Clear();
EnemyRestorationOwnerScope.Throw = true;
Observe(8);
Observe(8);
Check(TrialRepairLog.Messages.Count == 1 && TrialRepairLog.Messages[0].Contains("observation failed"),
    "owner read error is bounded and cannot escape postfix");
EnemyRestorationOwnerScope.Throw = false;

Console.WriteLine($"Audio diagnostics actual-source fake: {checks} checks, {errors.Count} failures");
foreach (var error in errors) Console.WriteLine("FAIL " + error);
return errors.Count == 0 ? 0 : 1;
