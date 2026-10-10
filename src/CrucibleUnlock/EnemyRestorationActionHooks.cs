using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;
using EntityView = Il2CppMoon.Forsaken.EntityView;

namespace CrucibleUnlock
{
    // Native detours stay small and contain no interop collection traversal.
    // Owner proof and item/timeline lifecycle work live in isolated helpers.
    internal static class EnemyRestorationActionHooks
    {
        private static int _equipFailure, _reactionFailure, _cleanupFailure, _keeperCleanupFailure;

        internal static void Install(PatchOwner owner)
        {
            _equipFailure = _reactionFailure = _cleanupFailure = _keeperCleanupFailure = 0;
            Prepare(typeof(EnemyRestorationKeeperWeaponViews), nameof(EnemyRestorationKeeperWeaponViews.TryEquip));
            Prepare(typeof(EnemyRestorationKeeperWeaponViews), nameof(EnemyRestorationKeeperWeaponViews.Release));
            Prepare(typeof(EnemyRestorationReactionViews), nameof(EnemyRestorationReactionViews.TryRepair));
            Prepare(typeof(EnemyRestorationReactionViews), nameof(EnemyRestorationReactionViews.Release));
            Prepare(typeof(EnemyRestorationActionHooks), nameof(Equip));
            Prepare(typeof(EnemyRestorationActionHooks), nameof(Repair));
            Prepare(typeof(EnemyRestorationActionHooks), nameof(Release));
            TrialRepairLog.Patch(owner, typeof(HumanoidEnemyView), "OnItemAssetResolved",
                new[] { typeof(EnemyItemDataBaseAsset), typeof(bool) },
                typeof(EnemyRestorationActionHooks), prefix: nameof(BeforeItemAssetResolved));
            TrialRepairLog.Patch(owner, typeof(EntityView), "GetActionView",
                new[] { typeof(ActionData), typeof(bool).MakeByRefType() },
                typeof(EnemyRestorationActionHooks), postfix: nameof(AfterGetActionView), returnType: typeof(ActionView));
            TrialRepairLog.Patch(owner, typeof(HumanoidEnemyView), "OnReturnedToPool", Type.EmptyTypes,
                typeof(EnemyRestorationActionHooks), prefix: nameof(BeforeHumanoidReturned));
            TrialRepairLog.Patch(owner, typeof(EntityView), "OnDestroy", Type.EmptyTypes,
                typeof(EnemyRestorationActionHooks), prefix: nameof(BeforeEntityDestroyed));
            TrialRepairLog.Write("[enemy-animation] installed scoped keeper equipment and sword parry hooks; isolated helpers prepared");
        }

        private static void Prepare(Type type, string name)
        {
            var helper = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (helper == null) throw new InvalidOperationException("Animation helper missing: " + type.Name + "." + name);
            RuntimeHelpers.PrepareMethod(helper.MethodHandle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool BeforeItemAssetResolved(HumanoidEnemyView __instance, EnemyItemDataBaseAsset __0, bool __1)
        {
            if (!EnemyRestorationRuntime.CurrentViewsReady || !__1 || __instance == null || __0 == null ||
                __0.AssetGuid.Value != EnemyRestorationKeeperWeaponViews.WeaponGuid) return true;
            return Equip(__instance, __0, __1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Equip(HumanoidEnemyView view, EnemyItemDataBaseAsset item, bool mainHand)
        {
            try { return !EnemyRestorationKeeperWeaponViews.TryEquip(view, item, mainHand, EnemyRestorationRuntime.Catalog); }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref _equipFailure, 1) == 0)
                    TrialRepairLog.Write("[enemy-animation] keeper scope/equipment error: " + error);
                return true; // An unproven owner keeps its original callback.
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AfterGetActionView(EntityView __instance, ActionData __0, ref ActionView __result)
        {
            if (!EnemyRestorationRuntime.CurrentViewsReady || __instance == null || __result != null ||
                __0 == null || __0.Guid.Value != EnemyRestorationReactionViews.ParryGuid) return;
            Repair(__instance, __0, ref __result);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Repair(EntityView view, ActionData action, ref ActionView result)
        {
            try { result = EnemyRestorationReactionViews.TryRepair(view, action, EnemyRestorationRuntime.Catalog); }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref _reactionFailure, 1) == 0)
                    TrialRepairLog.Write("[enemy-animation] sword parry error: " + error);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeHumanoidReturned(HumanoidEnemyView __instance) { Release(__instance); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeEntityDestroyed(EntityView __instance) { Release(__instance); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Release(EntityView owner)
        {
            // Cleanup remains enabled after module stop and before equipment
            // unbinds. The helper only touches objects it actually registered.
            try { EnemyRestorationReactionViews.Release(owner); }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref _cleanupFailure, 1) == 0)
                    TrialRepairLog.Write("[enemy-animation] sword parry cleanup error: " + error);
            }
            try { EnemyRestorationKeeperWeaponViews.Release(owner); }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref _keeperCleanupFailure, 1) == 0)
                    TrialRepairLog.Write("[enemy-animation] keeper cleanup error: " + error);
            }
        }
    }
}
