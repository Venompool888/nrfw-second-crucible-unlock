using System.Collections;
using System.Reflection;
using System.Text.Json;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Playmode;
using Il2CppMoon.Pooling;
using Il2CppQuantum;
using UnityEngine;

var failures = new List<string>();
int checks = 0;
void Check(bool ok, string label) { ++checks; if (!ok) failures.Add(label); }
int PendingCount()
{
    var field = typeof(EnemyRestorationKeeperWeaponViews).GetField("FailedClones", BindingFlags.NonPublic | BindingFlags.Static);
    return ((ICollection)field.GetValue(null)).Count;
}
const long weaponGuid = EnemyRestorationKeeperWeaponViews.WeaponGuid;
long[] attacks =
{
    853623266400606714L, 1749484287522819268L, 3638489295562831256L,
    710057211704157313L, 2058300539494848210L, 3427211662195995683L,
    2873488513977282736L
};
var catalog = new EnemyRestorationCatalog { Profiles = new EnemyRestorationProfile[4] };
catalog.Profiles[3] = new EnemyRestorationProfile
{
    Index = 3, Key = "keeper_greatsword", WeaponGuid = weaponGuid,
    ViewGuid = -611686018427387872L, NpcGuid = -611686018427387871L
};
(EnemyWeaponDataAsset item, PoolingSystem pool, GameObject clone) Prepare(bool throwFirstUnequip)
{
    EnemyRestorationKeeperWeaponViews.StopWithoutUnityReads();
    var ctx = new RuntimeAssetResolutionContext();
    Core.AssetCtx = ctx;
    var root = new GameObject("enemyKeeperGreatSwordViewVariant");
    root.Weapon = new EnemyWeaponView { WeaponData = new WeaponDataRef { Id = new AssetGuid { Value = weaponGuid } }, transform = root.transform };
    var item = new EnemyWeaponDataAsset
    {
        AssetGuid = new AssetGuid { Value = weaponGuid },
        PrefabRef = new PrefabRef { Guid = new PrefabGuid { m_data1 = 1694525422741062212UL, m_data2 = 18207951083920916411UL } }
    };
    ctx.Wrappers.Add(weaponGuid, item);
    var children = new List<Component>();
    for (int i = 0; i < attacks.Length; ++i)
    {
        var action = new MeleeAttackDataAsset { AssetGuid = new AssetGuid { Value = attacks[i] } };
        action.View = new ActionView { QuantumAsset = action, transform = root.transform };
        children.Add(action.View);
        ctx.Wrappers.Add(attacks[i], action);
    }
    root.Children = children.ToArray();
    var clone = new GameObject("keeper-clone");
    clone.Weapon = new EnemyWeaponView
    {
        WeaponData = new WeaponDataRef { Id = new AssetGuid { Value = weaponGuid } },
        transform = clone.transform, ThrowFirstUnequip = throwFirstUnequip
    };
    for (int i = 0; i < attacks.Length; ++i)
        clone.Weapon.m_actionViews.Add(new ActionView
        {
            QuantumAsset = ctx.Wrappers[attacks[i]], transform = clone.transform
        });
    var pool = new PoolingSystem { NextClone = clone };
    PoolingSystem.Active = pool;
    Check(EnemyRestorationKeeperWeaponViews.Ensure(ctx, catalog), "Exact shipped source prepares");
    return (item, pool, clone);
}

var (weapon, _, firstClone) = Prepare(throwFirstUnequip: true);
var firstOwner = new HumanoidEnemyView { ThrowDuringEquip = true };
Check(EnemyRestorationKeeperWeaponViews.TryEquip(firstOwner, weapon, true, catalog), "Proved private owner handles native Equip failure");
Check(firstClone.Weapon.UnequipCalls == 1 && firstOwner.m_actionViewMap.Count == 0,
    "Rollback attempted native Unequip then removed only remaining clone-owned map entries");
Check(!firstClone.activeSelf && !UnityEngine.Object.Destroyed.Contains(firstClone) && PendingCount() == 1,
    "Failed native Unequip leaves an inactive, rooted clone for retry");
Check(TrialRepairLog.Messages.Any(x => x.Contains("keeper clone rollback incomplete")),
    "Rollback error is separately reported despite the Equip failure");
EnemyRestorationKeeperWeaponViews.StopWithoutUnityReads();
Check(PendingCount() == 1, "Resolver reset preserves failed clone ownership for native return callback");
var foreign = new ActionView { QuantumAsset = new MeleeAttackDataAsset { AssetGuid = new AssetGuid { Value = attacks[0] } } };
firstOwner.m_actionViewMap[new AssetGuid { Value = attacks[0] }] = foreign;
EnemyRestorationKeeperWeaponViews.Release(firstOwner);
Check(firstOwner.m_actionViewMap[new AssetGuid { Value = attacks[0] }].Pointer == foreign.Pointer &&
    firstClone.Weapon.UnequipCalls == 1 && PendingCount() == 1 && !UnityEngine.Object.Destroyed.Contains(firstClone),
    "Foreign replacement forbids native GUID-based Unequip and preserves inactive clone");
firstOwner.m_actionViewMap.Remove(new AssetGuid { Value = attacks[0] });
EnemyRestorationKeeperWeaponViews.Release(firstOwner);
Check(firstClone.Weapon.UnequipCalls == 2 && firstClone.Weapon.RegisteredActions == 0 &&
    UnityEngine.Object.Destroyed.Contains(firstClone) && PendingCount() == 0,
    "Owner release retries native Unequip, unregisters remaining views and destroys clone");

var (secondItem, _, secondClone) = Prepare(throwFirstUnequip: false);
var secondOwner = new HumanoidEnemyView { ThrowAfterHandAssignment = true };
Check(EnemyRestorationKeeperWeaponViews.TryEquip(secondOwner, secondItem, true, catalog),
    "Late callback failure remains intercepted");
Check(secondOwner.MainWeaponView?.Pointer == secondClone.Weapon.Pointer &&
    !UnityEngine.Object.Destroyed.Contains(secondClone) && PendingCount() == 0,
    "Main-hand pointer transfer keeps clone native-owned after late callback failure");

var (thirdItem, _, thirdClone) = Prepare(throwFirstUnequip: false);
thirdClone.Weapon.WeaponData.Id = new AssetGuid { Value = 0 };
var thirdOwner = new HumanoidEnemyView();
Check(EnemyRestorationKeeperWeaponViews.TryEquip(thirdOwner, thirdItem, true, catalog),
    "Invalid clone binding fails closed for proved owner");
Check(UnityEngine.Object.Destroyed.Contains(thirdClone) && PendingCount() == 0,
    "Pre-callback clone failure destroys clone without Unequip");
Check(thirdClone.Weapon.UnequipCalls == 0, "No native Unequip before native callback");

var (fourthItem, fourthPool, fourthClone) = Prepare(throwFirstUnequip: false);
var fourthOwner = new HumanoidEnemyView();
int successBefore = TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped"));
Check(EnemyRestorationKeeperWeaponViews.TryEquip(fourthOwner, fourthItem, true, catalog) &&
    fourthOwner.m_actionViewMap.Count == attacks.Length &&
    TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped")) == successBefore + 1,
    "Successful callback validates every cloned ActionView map entry before logging success");
Check(fourthClone.transform.parent?.Pointer == fourthOwner.transform.Pointer &&
    fourthClone.Weapon.m_meleeEffects[0].m_characterView?.Pointer == fourthOwner.Pointer,
    "Keeper clone has official owner parent before native first MeleeEffects.Init");
Check(fourthClone.Weapon.AudioCompanion.WeaponSoundHost != null,
    "Native weapon companion/host remains available for diagnostics");
Check(fourthPool.LastParent.Pointer == fourthOwner.transform.Pointer &&
    fourthPool.LastSource.transform.parent == null,
    "Clone source remains unmodified and owner parent is also supplied to non-separate-scene cloning");

var (fifthItem, _, fifthClone) = Prepare(throwFirstUnequip: false);
var fifthOwner = new HumanoidEnemyView { SkipRegistrationGuid = attacks[0] };
successBefore = TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped"));
Check(EnemyRestorationKeeperWeaponViews.TryEquip(fifthOwner, fifthItem, true, catalog) &&
    fifthOwner.MainWeaponView?.Pointer == fifthClone.Weapon.Pointer &&
    TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped")) == successBefore,
    "Missing native ActionView mapping is detected without destroying transferred ownership");

// The shipped keeper root contains two different Thrust ActionViews with one
// GUID. Native registration has one dictionary winner; both belong to this clone.
foreach (bool firstWins in new[] { false, true })
{
    var (duplicateItem, _, duplicateClone) = Prepare(throwFirstUnequip: false);
    var duplicate = new ActionView
    {
        QuantumAsset = Core.AssetCtx.Wrappers[attacks[3]], transform = duplicateClone.transform
    };
    duplicateClone.Weapon.m_actionViews.Add(duplicate);
    var duplicateOwner = new HumanoidEnemyView { KeepFirstRegistration = firstWins };
    successBefore = TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped"));
    Check(EnemyRestorationKeeperWeaponViews.TryEquip(duplicateOwner, duplicateItem, true, catalog) &&
        TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped")) == successBefore + 1,
        "Shipped duplicate Thrust accepts either native clone-owned winner: first=" + firstWins);
    var winner = duplicateOwner.m_actionViewMap[new AssetGuid { Value = attacks[3] }];
    Check(winner.Pointer == (firstWins ? duplicateClone.Weapon.m_actionViews[3].Pointer : duplicate.Pointer) &&
        duplicateOwner.MainWeaponView.Pointer == duplicateClone.Weapon.Pointer && duplicateClone.activeSelf,
        "Verification preserves native registration choice and equipped clone: first=" + firstWins);

    var verify = typeof(EnemyRestorationKeeperWeaponViews).GetMethod("VerifyEquippedActionViews",
        BindingFlags.NonPublic | BindingFlags.Static);
    duplicateOwner.m_actionViewMap[new AssetGuid { Value = attacks[3] }] = new ActionView
    {
        QuantumAsset = Core.AssetCtx.Wrappers[attacks[3]]
    };
    bool foreignRejected = false;
    try { verify.Invoke(null, new object[] { duplicateOwner, duplicateClone.Weapon }); }
    catch (TargetInvocationException error) { foreignRejected = error.InnerException is InvalidOperationException; }
    Check(foreignRejected, "Same GUID on a foreign ActionView still fails ownership validation");
}

var (duplicatePartialItem, _, duplicatePartialClone) = Prepare(throwFirstUnequip: false);
duplicatePartialClone.Weapon.m_actionViews.Add(new ActionView
{
    QuantumAsset = Core.AssetCtx.Wrappers[attacks[3]], transform = duplicatePartialClone.transform
});
var duplicatePartialOwner = new HumanoidEnemyView { ThrowDuringEquip = true };
Check(EnemyRestorationKeeperWeaponViews.TryEquip(duplicatePartialOwner, duplicatePartialItem, true, catalog),
    "Partial native Equip with shipped duplicate remains scoped and intercepted");
Check(duplicatePartialClone.Weapon.UnequipCalls == 1 && duplicatePartialOwner.m_actionViewMap.Count == 0 &&
    UnityEngine.Object.Destroyed.Contains(duplicatePartialClone) && PendingCount() == 0,
    "Clone-owned duplicate winner permits native rollback without false foreign-pointer failure");

var (parentFailureItem, _, parentFailureClone) = Prepare(throwFirstUnequip: false);
parentFailureClone.transform.ThrowDuringSetParent = true;
var parentFailureOwner = new HumanoidEnemyView();
Check(EnemyRestorationKeeperWeaponViews.TryEquip(parentFailureOwner, parentFailureItem, true, catalog) &&
    parentFailureOwner.MainWeaponView == null && parentFailureClone.Weapon.m_enemyView == null &&
    UnityEngine.Object.Destroyed.Contains(parentFailureClone) && PendingCount() == 0,
    "Parenting failure closes and destroys only the untransferred clone before native Equip");

var (staleEffectItem, _, staleEffectClone) = Prepare(throwFirstUnequip: false);
staleEffectClone.Weapon.m_meleeEffects[0].Init(null); // Native Init is one-shot.
var staleEffectOwner = new HumanoidEnemyView();
successBefore = TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped"));
EnemyRestorationKeeperWeaponViews.TryEquip(staleEffectOwner, staleEffectItem, true, catalog);
Check(TrialRepairLog.Messages.Count(x => x.Contains("keeper weapon view equipped")) == successBefore &&
    staleEffectOwner.MainWeaponView?.Pointer == staleEffectClone.Weapon.Pointer &&
    !UnityEngine.Object.Destroyed.Contains(staleEffectClone),
    "Already-initialized null effects are detected without resetting native state or destroying native ownership");

Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Il2Cpp") || a.GetName().Name.StartsWith("Unity")),
    "No game DLL loaded in managed fake harness");
if (args.Length > 0)
    File.WriteAllText(args[0], JsonSerializer.Serialize(new
    {
        checks, failures, passed = failures.Count == 0,
        actual_source_compiled = true, managed_fakes_only = true
    }, new JsonSerializerOptions { WriteIndented = true }));
foreach (var failure in failures) Console.WriteLine("FAIL: " + failure);
Console.WriteLine($"Keeper bridge actual-source fake tests: {checks} checks; {failures.Count} failures; no game DLLs.");
return failures.Count == 0 ? 0 : 1;
