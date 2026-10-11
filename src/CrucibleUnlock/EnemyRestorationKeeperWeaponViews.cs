using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Playmode;
using Il2CppMoon.Pooling;
using Il2CppQuantum;
using UnityEngine;
using EntityView = Il2CppMoon.Forsaken.EntityView;

namespace CrucibleUnlock
{
    // The shipped keeper greatsword item prefab and all seven attack views are
    // embedded in the action asset bundle, but its PrefabRef is absent from the
    // WorldLUT pool. This bridge equips a clone through the game's own item-view
    // callback for exactly our private keeper NPC. It never edits a source asset
    // or adds a partial entry to the multi-index global pool.
    internal static class EnemyRestorationKeeperWeaponViews
    {
        private const int KeeperIndex = 3;
        internal const long WeaponGuid = 4494428972466418711L;
        private const ulong KeeperPrefabData1 = 1694525422741062212UL;
        private const ulong KeeperPrefabData2 = 18207951083920916411UL;
        private const string KeeperRootName = "enemyKeeperGreatSwordViewVariant";
        private static readonly long[] AttackGuids =
        {
            853623266400606714L, 1749484287522819268L, 3638489295562831256L,
            710057211704157313L, 2058300539494848210L, 3427211662195995683L,
            2873488513977282736L
        };

        private static RuntimeAssetResolutionContext _context;
        private static PoolingSystem _pool;
        private static EnemyWeaponDataAsset _weapon;
        private static MeleeAttackDataAsset[] _actions;
        private static GameObject _root;
        private static int _reportedSuccess, _reportedFailure, _reportedRollback;
        private static readonly List<FailedClone> FailedClones = new List<FailedClone>();

        private sealed class FailedClone
        {
            internal HumanoidEnemyView Owner;
            internal GameObject Clone;
            internal EnemyWeaponView Weapon;
            internal bool CallbackStarted;
        }

        internal static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var profile = Keeper(catalog);
            if (Core.AssetCtx?.Pointer != ctx.Pointer) return false;
            var pool = PoolingSystem.Active;
            if (pool == null) return false;
            if (_context != null)
            {
                if (_context.Pointer != ctx.Pointer || _pool?.Pointer != pool.Pointer)
                    StopWithoutUnityReads(); // A new official session/pool owns a new source root.
                else
                {
                    if (_weapon?.Pointer == IntPtr.Zero || _root?.Pointer == IntPtr.Zero)
                        throw new InvalidOperationException("Keeper weapon source identity changed.");
                    return true;
                }
            }

            if (!ctx.TryFindWrapperImpl(new AssetGuid { Value = WeaponGuid }, out var weaponBase, true))
                return false;
            var weapon = weaponBase?.TryCast<EnemyWeaponDataAsset>();
            if (weapon == null || weapon.AssetGuid.Value != WeaponGuid ||
                weapon.PrefabRef.Guid.m_data1 != KeeperPrefabData1 ||
                weapon.PrefabRef.Guid.m_data2 != KeeperPrefabData2)
                throw new InvalidOperationException("Official keeper weapon wrapper/PrefabRef mismatch.");

            var actions = new MeleeAttackDataAsset[AttackGuids.Length];
            GameObject root = null;
            for (int i = 0; i < AttackGuids.Length; ++i)
            {
                var guid = AttackGuids[i];
                if (!ctx.TryFindWrapperImpl(new AssetGuid { Value = guid }, out var actionBase, true))
                    return false;
                var action = actionBase?.TryCast<MeleeAttackDataAsset>();
                var sourceView = action?.View;
                if (action == null || action.AssetGuid.Value != guid || sourceView == null ||
                    sourceView.QuantumAsset?.AssetGuid.Value != guid)
                    throw new InvalidOperationException("Official keeper action view mismatch: " + guid);
                var actionRoot = sourceView.transform?.root?.gameObject;
                if (actionRoot == null) throw new InvalidOperationException("Keeper action root is absent: " + guid);
                if (root == null) root = actionRoot;
                else if (root.Pointer != actionRoot.Pointer)
                    throw new InvalidOperationException("Keeper action views do not share one weapon root.");
                actions[i] = action;
            }
            VerifyRoot(root, profile);
            _context = ctx;
            _pool = pool;
            _weapon = weapon;
            _actions = actions;
            _root = root;
            ValidateReady(profile);
            return true;
        }

        // A true return means the caller must skip the original unresolved pool
        // request. Until exact private ownership is proven, all official enemies
        // retain the original method. Once proven, failures are closed and logged.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool TryEquip(HumanoidEnemyView view, EnemyItemDataBaseAsset itemAsset,
            bool mainHand, EnemyRestorationCatalog catalog)
        {
            if (!mainHand || view == null || itemAsset == null ||
                itemAsset.AssetGuid.Value != WeaponGuid || !EnemyRestorationRuntime.CurrentViewsReady)
                return false;
            EnemyRestorationProfile owner;
            try
            {
                if (!EnemyRestorationOwnerScope.TryGetProfile(view, catalog, out owner) || owner.Index != KeeperIndex)
                    return false;
            }
            catch (Exception error)
            {
                ReportFailure("owner scope", error);
                return false; // Do not intercept an owner that was not proved.
            }

            GameObject clone = null;
            EnemyWeaponView cloneWeapon = null;
            bool callbackStarted = false;
            try
            {
                // An earlier Equip may have registered some ActionViews before
                // throwing. Never equip a second copy while that map is uncertain.
                if (!RetryFailedClones(view))
                    throw new InvalidOperationException("Previous keeper weapon clone rollback is incomplete.");
                ValidateReady(owner);
                if (Core.AssetCtx?.Pointer != _context.Pointer || itemAsset.Pointer != _weapon.Pointer)
                    throw new InvalidOperationException("Keeper item wrapper/resolver identity changed.");
                // CloneSync uses this parent only in its non-separate-scene
                // branch. Set it explicitly as well before the first native
                // initialization, matching the official item spawn's Parent.
                clone = _pool.CloneSync(_root, view.transform);
                if (clone == null || clone.Pointer == _root.Pointer)
                    throw new InvalidOperationException("Official keeper weapon clone failed.");
                cloneWeapon = clone.GetComponent(Il2CppType.Of<EnemyWeaponView>())?.TryCast<EnemyWeaponView>();
                if (cloneWeapon == null || cloneWeapon.WeaponData.Id.Value != WeaponGuid)
                    throw new InvalidOperationException("Cloned keeper weapon root lost its native binding.");
                // The two-argument overload is a throwing unstripping stub in
                // this build. Use the shipped native overload with messages on.
                clone.transform.SetParent(view.transform, false, false);
                // The official pool callback performs this single state write.
                cloneWeapon.OnInstantiatedFromPool();
                if (!clone.activeSelf) clone.SetActive(true);
                // Native callback equips, registers all child ActionViews and
                // stores the main-hand item view; it owns the clone thereafter.
                callbackStarted = true;
                view.OnItemViewInstantiated(clone, itemAsset, true);
                if (view.MainWeaponView?.Pointer != cloneWeapon.Pointer)
                    throw new InvalidOperationException("Native item callback did not retain the cloned keeper weapon.");
                VerifyEquippedActionViews(view, cloneWeapon);
                int effectCount = VerifyMeleeEffects(view, cloneWeapon);
                if (Interlocked.Exchange(ref _reportedSuccess, 1) == 0)
                    TrialRepairLog.Write("[enemy-restoration] keeper weapon view equipped through native item callback; profile=" + owner.Key +
                        "; meleeEffects=" + effectCount + "; character=" + view.Pointer +
                        "; audio=" + (cloneWeapon.AudioCompanion?.Pointer ?? IntPtr.Zero) +
                        "; soundHost=" + (cloneWeapon.AudioCompanion?.WeaponSoundHost?.Pointer ?? IntPtr.Zero));
            }
            catch (Exception error)
            {
                if (clone != null) CleanupFailedClone(view, clone, cloneWeapon, callbackStarted);
                ReportFailure("keeper equip", error);
            }
            return true;
        }

        // Called by the root module's resolver reset. No Unity object is read or
        // destroyed here; clones already passed to native remain native-owned.
        internal static void StopWithoutUnityReads()
        {
            _context = null;
            _pool = null;
            _weapon = null;
            _actions = null;
            _root = null;
            _reportedSuccess = 0;
            _reportedFailure = 0;
        }

        // Called before the game's own return/destroy callback, while its owner
        // and registered ActionViews are still available for native Unequip.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Release(EntityView owner)
        {
            if (owner == null) return;
            RetryFailedClones(owner);
        }

        private static EnemyRestorationProfile Keeper(EnemyRestorationCatalog catalog)
        {
            if (catalog?.Profiles == null || catalog.Profiles.Length <= KeeperIndex)
                throw new InvalidOperationException("Keeper profile is missing.");
            var p = catalog.Profiles[KeeperIndex];
            if (p == null || p.Index != KeeperIndex || p.Key != "keeper_greatsword" ||
                p.WeaponGuid != WeaponGuid || p.ViewGuid != -611686018427387872L ||
                p.NpcGuid != -611686018427387871L)
                throw new InvalidOperationException("Keeper profile identity changed.");
            return p;
        }

        private static void ValidateReady(EnemyRestorationProfile profile)
        {
            if (_context == null || _pool == null || _weapon == null || _actions == null || _root == null ||
                _actions.Length != AttackGuids.Length || PoolingSystem.Active?.Pointer != _pool.Pointer ||
                _weapon.AssetGuid.Value != WeaponGuid)
                throw new InvalidOperationException("Keeper weapon view source is not ready.");
            for (int i = 0; i < AttackGuids.Length; ++i)
                if (_actions[i] == null || _actions[i].AssetGuid.Value != AttackGuids[i] ||
                    _actions[i].View == null || _actions[i].View.QuantumAsset?.AssetGuid.Value != AttackGuids[i] ||
                    _actions[i].View.transform.root.gameObject.Pointer != _root.Pointer)
                    throw new InvalidOperationException("Keeper action source changed: " + AttackGuids[i]);
            VerifyRoot(_root, profile);
        }

        private static void VerifyRoot(GameObject root, EnemyRestorationProfile profile)
        {
            if (root == null || root.name != KeeperRootName)
                throw new InvalidOperationException("Official keeper weapon root is absent or renamed.");
            var weapon = root.GetComponent(Il2CppType.Of<EnemyWeaponView>())?.TryCast<EnemyWeaponView>();
            if (weapon == null || weapon.WeaponData.Id.Value != profile.WeaponGuid)
                throw new InvalidOperationException("Official keeper weapon root binding mismatch.");
            var views = root.GetComponentsInChildren(Il2CppType.Of<ActionView>(), true);
            if (views == null) throw new InvalidOperationException("Keeper weapon ActionViews are absent.");
            var found = new HashSet<long>();
            for (int i = 0; i < views.Length; ++i)
            {
                var action = views[i]?.TryCast<ActionView>();
                var guid = action?.QuantumAsset?.AssetGuid.Value ?? 0;
                if (guid != 0) found.Add(guid);
            }
            for (int i = 0; i < AttackGuids.Length; ++i)
                if (!found.Contains(AttackGuids[i]))
                    throw new InvalidOperationException("Shipped keeper weapon root lacks action view: " + AttackGuids[i]);
        }

        private static void VerifyEquippedActionViews(HumanoidEnemyView owner, EnemyWeaponView weapon)
        {
            var map = owner.m_actionViewMap;
            if (map == null)
                throw new InvalidOperationException("Keeper native equip did not expose its action map/list.");
            var owned = CollectActionPointers(weapon);
            // The official keeper prefab has two Thrust views with one GUID.
            // Native registration chooses one map entry; validate that winner's
            // ownership without changing the game's choice or the shared prefab.
            foreach (var entry in owned)
                if (!map.TryGetValue(new AssetGuid { Value = entry.Key }, out var mapped) ||
                    mapped == null || !entry.Value.Contains(mapped.Pointer))
                    throw new InvalidOperationException("Native keeper equip did not map its own ActionView " + entry.Key);
            for (int i = 0; i < AttackGuids.Length; ++i)
                if (!owned.ContainsKey(AttackGuids[i]))
                    throw new InvalidOperationException("Native keeper equip omitted shipped attack " + AttackGuids[i]);
        }

        private static Dictionary<long, HashSet<IntPtr>> CollectActionPointers(EnemyWeaponView weapon)
        {
            var views = weapon?.m_actionViews;
            if (views == null)
                throw new InvalidOperationException("Keeper clone action view list cannot be verified.");
            var owned = new Dictionary<long, HashSet<IntPtr>>();
            for (int i = 0; i < views.Count; ++i)
            {
                var child = views[i];
                var guid = child?.QuantumAsset?.AssetGuid.Value ?? 0;
                if (guid == 0)
                    throw new InvalidOperationException("Keeper clone action view has no GUID.");
                if (!owned.TryGetValue(guid, out var pointers))
                    owned.Add(guid, pointers = new HashSet<IntPtr>());
                pointers.Add(child.Pointer);
            }
            return owned;
        }

        private static int VerifyMeleeEffects(HumanoidEnemyView owner, EnemyWeaponView weapon)
        {
            // Init is one-shot and discovers its CharacterView from the parent
            // hierarchy. Repeating Init or writing m_inited cannot safely repair
            // a null owner; read back the native Equip result instead.
            var effects = weapon.m_meleeEffects;
            if (effects == null || effects.Count == 0)
                throw new InvalidOperationException("Keeper native equip did not initialize its melee effects.");
            for (int i = 0; i < effects.Count; ++i)
                if (effects[i]?.m_characterView?.Pointer != owner.Pointer)
                    throw new InvalidOperationException("Keeper melee effect did not bind its actual CharacterView at index " + i);
            return effects.Count;
        }

        private static void CleanupFailedClone(HumanoidEnemyView owner, GameObject clone,
            EnemyWeaponView weapon, bool callbackStarted)
        {
            var pending = new FailedClone
            {
                Owner = owner, Clone = clone, Weapon = weapon, CallbackStarted = callbackStarted
            };
            if (!TryCleanup(pending)) FailedClones.Add(pending);
        }

        private static bool RetryFailedClones(EntityView owner)
        {
            bool complete = true;
            for (int i = FailedClones.Count - 1; i >= 0; --i)
            {
                var pending = FailedClones[i];
                if (pending.Owner?.Pointer != owner.Pointer) continue;
                if (TryCleanup(pending)) FailedClones.RemoveAt(i);
                else complete = false;
            }
            return complete;
        }

        private static bool TryCleanup(FailedClone pending)
        {
            Exception failure = null;
            bool nativeOwnsClone = false;
            try
            {
                nativeOwnsClone = pending.Weapon != null &&
                    pending.Owner.MainWeaponView?.Pointer == pending.Weapon.Pointer;
            }
            catch (Exception error) { failure = error; }
            if (nativeOwnsClone) return true;

            if (failure == null && pending.CallbackStarted && pending.Weapon?.m_enemyView != null)
            {
                // Native Equip registers child ActionViews before it assigns
                // MainWeaponView. Native Unequip unregisters that exact list.
                // It removes by action GUID, so first prove that no later
                // weapon has replaced one of the clone's map entries.
                try
                {
                    if (pending.Weapon.m_enemyView.Pointer != pending.Owner.Pointer)
                        throw new InvalidOperationException("Keeper clone's native owner changed.");
                    VerifyNoForeignActionMappings(pending);
                    pending.Weapon.Unequip();
                }
                catch (Exception error) { failure = error; }
            }

            if (failure != null && pending.CallbackStarted)
            {
                // A failed native loop can leave an arbitrary prefix of its
                // actions registered. Remove only map entries that still point
                // at this exact clone, then keep the clone for native retry.
                try { DetachOwnedActionViews(pending); }
                catch (Exception error) { failure = new AggregateException(failure, error); }
            }

            // Even when unregister fails, make this orphan inert. Keep it rooted
            // for a later retry; destroying it could leave native map pointers.
            try
            {
                if (pending.Clone.activeSelf) pending.Clone.SetActive(false);
            }
            catch (Exception error)
            {
                failure = failure == null ? error : new AggregateException(failure, error);
            }

            if (failure == null)
            {
                try { UnityEngine.Object.Destroy(pending.Clone); }
                catch (Exception error) { failure = error; }
            }
            if (failure == null) return true;
            ReportRollbackFailure(failure);
            return false;
        }

        private static void VerifyNoForeignActionMappings(FailedClone pending)
        {
            var map = pending.Owner.m_actionViewMap;
            if (map == null)
                throw new InvalidOperationException("Keeper clone action map/list cannot be verified.");
            foreach (var entry in CollectActionPointers(pending.Weapon))
                if (map.TryGetValue(new AssetGuid { Value = entry.Key }, out var mapped) &&
                    (mapped == null || !entry.Value.Contains(mapped.Pointer)))
                    throw new InvalidOperationException("A foreign ActionView replaced keeper clone GUID " + entry.Key);
        }

        private static void DetachOwnedActionViews(FailedClone pending)
        {
            var map = pending.Owner.m_actionViewMap;
            var views = pending.Weapon?.m_actionViews;
            if (map == null || views == null)
                throw new InvalidOperationException("Keeper clone action map/list cannot be verified for rollback.");
            var errors = new List<Exception>();
            for (int i = 0; i < views.Count; ++i)
            {
                var view = views[i];
                var guidValue = view?.QuantumAsset?.AssetGuid.Value ?? 0;
                if (guidValue == 0) { errors.Add(new InvalidOperationException("Keeper rollback action has no GUID.")); continue; }
                var guid = new AssetGuid { Value = guidValue };
                try
                {
                    if (!map.TryGetValue(guid, out var mapped) || mapped?.Pointer != view.Pointer) continue;
                    try { pending.Owner.UnregisterActionView(view); }
                    catch (Exception error) { errors.Add(error); }
                    // The GUID overload is a narrow dictionary removal. Its
                    // native body has no pointer check, so prove the pointer
                    // again immediately before using it as a fallback.
                    map = pending.Owner.m_actionViewMap;
                    if (map == null) throw new InvalidOperationException("Keeper rollback action map vanished.");
                    if (map.TryGetValue(guid, out mapped) && mapped?.Pointer == view.Pointer)
                        pending.Owner.UnregisterActionView(guid);
                    map = pending.Owner.m_actionViewMap;
                    if (map == null) throw new InvalidOperationException("Keeper rollback action map vanished.");
                    if (map.TryGetValue(guid, out mapped) && mapped?.Pointer == view.Pointer)
                        errors.Add(new InvalidOperationException("Keeper rollback map still owns action " + guidValue));
                }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException("Keeper action rollback incomplete.", errors);
        }

        private static void ReportRollbackFailure(Exception error)
        {
            if (Interlocked.Exchange(ref _reportedRollback, 1) != 0) return;
            try { TrialRepairLog.Write("[enemy-restoration] keeper clone rollback incomplete; retained inactive clone for owner release: " + error); }
            catch { }
        }

        private static void ReportFailure(string phase, Exception error)
        {
            if (Interlocked.Exchange(ref _reportedFailure, 1) != 0) return;
            try { TrialRepairLog.Write("[enemy-restoration] keeper weapon bridge failed at " + phase + ": " + error); }
            catch { }
        }
    }
}
