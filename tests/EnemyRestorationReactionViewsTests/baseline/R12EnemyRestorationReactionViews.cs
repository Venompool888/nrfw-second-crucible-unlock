using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppMoon;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Timeline;
using UnityEngine;
using EntityView = Il2CppMoon.Forsaken.EntityView;
using UObject = UnityEngine.Object;

namespace CrucibleUnlock
{
    internal static class EnemyRestorationReactionViews
    {
        public const long ParryGuid = 2122162585542657617L;
        private sealed class Ready
        {
            public RuntimeAssetResolutionContext Context;
            public HitReactionDataAsset Source;
            public TimelineActionView Template;
        }
        private sealed class Owned
        {
            public EntityView Owner;
            public RuntimeAssetResolutionContext Context;
            public GameObject Root;
            public TimelineActionView View;
            public ulong Entity;
            public bool Releasing;
            public IntPtr Weapon, Animator, Sound;
        }
        private static Ready _ready;
        private static readonly Dictionary<IntPtr, Owned> Copies = new Dictionary<IntPtr, Owned>();
        private static readonly HashSet<string> LoggedProfiles = new HashSet<string>(StringComparer.Ordinal);
        private static int _cleanupErrors;

        public static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog)
        {
            if (ctx == null || catalog?.Profiles == null) throw new InvalidOperationException("Reaction view context/catalog is missing.");
            if (!ctx.TryFindWrapperImpl(new Il2CppQuantum.AssetGuid { Value = ParryGuid }, out var sourceBase, true)) return false;
            var source = sourceBase?.TryCast<HitReactionDataAsset>();
            var template = source?.View?.TryCast<TimelineActionView>();
            if (source == null || source.AssetGuid.Value != ParryGuid || template == null)
                throw new InvalidOperationException("Parry reaction source wrapper/view type or GUID mismatch.");
            VerifyTemplate(template);
            if (_ready != null && _ready.Context.Pointer == ctx.Pointer &&
                (_ready.Source.Pointer != source.Pointer || _ready.Template.Pointer != template.Pointer))
                throw new InvalidOperationException("Parry source identity changed in the current context.");
            bool first = _ready == null || _ready.Context.Pointer != ctx.Pointer;
            _ready = new Ready { Context = ctx, Source = source, Template = template };
            if (first) TrialRepairLog.Write("[enemy-animation] SWORD PARRY SOURCE READY guid=" + ParryGuid + "; tracks=13; clip=parriedHitReact; context=" + ctx.Pointer);
            return true;
        }

        // Only called by the root's small GetActionView postfix after a missing
        // result, target GUID, module readiness and current manager checks.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ActionView TryRepair(EntityView owner, Il2CppQuantum.ActionData action, EnemyRestorationCatalog catalog)
        {
            var ready = _ready;
            if (ready == null || owner == null || action?.Guid.Value != ParryGuid || Core.AssetCtx?.Pointer != ready.Context.Pointer ||
                !EnemyRestorationOwnerScope.TryGetProfile(owner, catalog, out var profile) || !NeedsParry(profile)) return null;
            var humanoid = owner.TryCast<HumanoidEnemyView>();
            var weapon = humanoid?.MainWeaponView;
            var animator = humanoid?.CharacterAnimator;
            var sound = weapon?.AudioCompanion?.WeaponSoundHost;
            if (weapon == null || animator == null || sound == null) return null; // Equipment may still be loading.
            if (ready.Source.AssetGuid.Value != ParryGuid || ready.Source.View?.Pointer != ready.Template.Pointer ||
                !ready.Context.TryFindWrapperImpl(new Il2CppQuantum.AssetGuid { Value = ParryGuid }, out var sourceNow, false) ||
                sourceNow?.Pointer != ready.Source.Pointer)
                throw new InvalidOperationException("Parry source wrapper ownership was lost.");
            VerifyTemplate(ready.Template);
            var map = owner.m_actionViewMap;
            if (map == null) throw new InvalidOperationException("Parry owner action map is unavailable.");
            map.TryGetValue(new Il2CppQuantum.AssetGuid { Value = ParryGuid }, out var mapped);
            if (Copies.TryGetValue(owner.Pointer, out var existing))
            {
                if (existing.Owner.Pointer != owner.Pointer || existing.Context.Pointer != ready.Context.Pointer ||
                    existing.Entity != owner.EntityRef.Raw || existing.Root == null || existing.View == null ||
                    existing.Releasing || existing.Weapon != weapon.Pointer || existing.Animator != animator.Pointer || existing.Sound != sound.Pointer ||
                    mapped?.Pointer != existing.View.Pointer || existing.View.EntityOwner?.Pointer != owner.Pointer ||
                    !existing.Root.transform.IsChildOf(owner.transform))
                    throw new InvalidOperationException("Private reaction view owner changed before cleanup.");
                return existing.View;
            }
            if (mapped != null) throw new InvalidOperationException("Parry action map is owned by a foreign view.");
            GameObject root = null;
            TimelineActionView clone = null;
            bool registered = false;
            try
            {
                root = new GameObject("NRFW_PrivateSwordParryReaction");
                root.SetActive(false);
                root.transform.SetParent(owner.transform, false, false);
                var copy = UObject.Instantiate(ready.Template.gameObject, root.transform, false);
                clone = copy.GetComponent(Il2CppType.Of<TimelineActionView>())?.TryCast<TimelineActionView>();
                if (clone == null || clone.Pointer == ready.Template.Pointer || clone.QuantumAsset?.AssetGuid.Value != ParryGuid ||
                    clone.EffectiveTimeline == null || clone.EffectiveTimeline.Pointer == ready.Template.EffectiveTimeline.Pointer)
                    throw new InvalidOperationException("Parry reaction subtree/timeline was not independently cloned.");
                var records = clone.EffectiveTimeline.EntityRecords;
                if (records == null || records.Count != 13) throw new InvalidOperationException("Parry clone timeline shape changed.");
                int animations = 0, previews = 0, sounds = 0;
                for (int i = 0; i < records.Count; ++i)
                {
                    var item = records[i]?.TimelineItem;
                    var component = item?.TryCast<Component>();
                    if (component == null || !component.transform.IsChildOf(copy.transform))
                        throw new InvalidOperationException("Parry timeline item still belongs to the source hierarchy.");
                    var animation = item.TryCast<ForsakenAnimationPlayer>();
                    if (animation != null)
                    {
                        var clip = animation.EffectiveAnimation?.TryCast<ClipAnimation>();
                        if (clip?.Clip == null || clip.name != "parriedHitReact" || clip.AnimatorDefinition == null ||
                            clip.AnimatorDefinition.Pointer != animator.Definition?.Pointer)
                            throw new InvalidOperationException("Parry clip is incompatible with the private actor rig.");
                        var reference = animation.Animator?.Copy();
                        if (reference == null || reference.Pointer == animation.Animator.Pointer)
                            throw new InvalidOperationException("Parry animator reference is shared with source.");
                        reference.SetStaticValue(animator);
                        if (reference.SafeResolve(null)?.Pointer != animator.Pointer) throw new InvalidOperationException("Parry animator binding failed.");
                        animation.Animator = reference;
                        ++animations;
                    }
                    var preview = item.TryCast<WeaponPreviewAnimator>();
                    if (preview != null) { preview.WeaponRig = weapon.transform; ++previews; }
                    var audio = item.TryCast<WwiseSoundAnimator>();
                    if (audio != null)
                    {
                        var reference = audio.MoonSoundHost?.Copy();
                        if (reference == null || reference.Pointer == audio.MoonSoundHost.Pointer)
                            throw new InvalidOperationException("Parry sound reference is shared with source.");
                        reference.SetStaticValue(sound);
                        if (reference.SafeResolve(null)?.Pointer != sound.Pointer) throw new InvalidOperationException("Parry sound binding failed.");
                        audio.MoonSoundHost = reference;
                        ++sounds;
                    }
                }
                if (animations != 1 || previews != 1 || sounds != 1)
                    throw new InvalidOperationException("Parry external reference track count changed.");
                // Preserve the serialized typed reference while replacing only
                // this copy's target; never mutate an original resolver.
                var main = clone.MainAnimatorRef?.Copy();
                if (main == null || main.Pointer == clone.MainAnimatorRef.Pointer)
                    throw new InvalidOperationException("Parry main animator reference was not independently copied.");
                main.SetStaticValue(animator);
                if (main.SafeResolve(null)?.Pointer != animator.Pointer)
                    throw new InvalidOperationException("Parry main animator binding failed.");
                clone.MainAnimatorRef = main;
                // The official callback finds the parent EntityView, registers
                // its map entry, initializes pool requests and caches animations.
                registered = true;
                clone.OnInstantiatedFromPool();
                var registeredMap = owner.m_actionViewMap;
                if (clone.EntityOwner?.Pointer != owner.Pointer || registeredMap == null ||
                    !registeredMap.TryGetValue(new Il2CppQuantum.AssetGuid { Value = ParryGuid }, out var registeredView) ||
                    registeredView?.Pointer != clone.Pointer)
                    throw new InvalidOperationException("Parry clone did not register to its current owner.");
                root.SetActive(true);
                Copies.Add(owner.Pointer, new Owned { Owner = owner, Context = ready.Context, Root = root,
                    View = clone, Entity = owner.EntityRef.Raw, Weapon = weapon.Pointer, Animator = animator.Pointer, Sound = sound.Pointer });
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                if (!Cleanup(owner, registered ? clone : null, root, errors))
                    Copies.Add(owner.Pointer, new Owned { Owner = owner, Context = ready.Context, Root = root,
                        View = registered ? clone : null, Entity = owner.EntityRef.Raw, Releasing = true,
                        Weapon = weapon.Pointer, Animator = animator.Pointer, Sound = sound.Pointer });
                LogCleanupError("create", errors);
                if (errors.Count > 1) throw new AggregateException("Parry reaction creation/cleanup failed.", errors);
                throw;
            }
            // Publication ends the clone transaction. Diagnostic failure must
            // not roll back a successfully registered view or duplicate its
            // ownership record.
            try
            {
                if (LoggedProfiles.Add(profile.Key))
                    TrialRepairLog.Write("[enemy-animation] SWORD PARRY READY profile=" + profile.Key + "; entity=" + owner.EntityRef.Raw +
                        "; view=" + clone.Pointer + "; animator=" + animator.Pointer + "; weapon=" + weapon.Pointer + "; sound=" + sound.Pointer + "; tracks=13");
            }
            catch { }
            return clone;
        }

        private static bool NeedsParry(EnemyRestorationProfile p) => p != null &&
            (p.Key == "keeper_sword_shield_left" || p.Key == "keeper_sword_shield_right" || p.Key == "dead_tide_great_curved_sword");
        private static void VerifyTemplate(TimelineActionView template)
        {
            if (template.QuantumAsset?.AssetGuid.Value != ParryGuid || template.gameObject?.name != "parriedAttackHitReact" ||
                template.EntityOwner != null || template.OwningEntityView != null || template.CharacterView != null ||
                template.EffectiveTimeline == null || template.EffectiveTimeline.EntityRecords == null || template.EffectiveTimeline.EntityRecords.Count != 13)
                throw new InvalidOperationException("Parry source timeline identity/shape changed.");
            int animations = 0, previews = 0, sounds = 0;
            var records = template.EffectiveTimeline.EntityRecords;
            for (int i = 0; i < records.Count; ++i)
            {
                var item = records[i]?.TimelineItem;
                var component = item?.TryCast<Component>();
                if (component == null || !component.transform.IsChildOf(template.transform))
                    throw new InvalidOperationException("Parry source track hierarchy is inconsistent.");
                var animation = item.TryCast<ForsakenAnimationPlayer>();
                if (animation != null)
                {
                    var clip = animation.EffectiveAnimation?.TryCast<ClipAnimation>();
                    if (clip?.Clip == null || clip.name != "parriedHitReact" || clip.AnimatorDefinition?.name != "playerMoonAnimatorDefinition" || animation.Animator == null)
                        throw new InvalidOperationException("Parry source animation is missing or mismatched.");
                    ++animations;
                }
                if (item.TryCast<WeaponPreviewAnimator>() != null) ++previews;
                var audio = item.TryCast<WwiseSoundAnimator>();
                if (audio != null) { if (audio.MoonSoundHost == null) throw new InvalidOperationException("Parry source sound reference is missing."); ++sounds; }
            }
            if (animations != 1 || previews != 1 || sounds != 1)
                throw new InvalidOperationException("Parry source external reference shape changed.");
        }

        // Called from a current Unity lifecycle callback, including after module
        // stop. Never infer cleanup ownership from GUID/name alone.
        public static void Release(EntityView owner)
        {
            if (owner == null || !Copies.TryGetValue(owner.Pointer, out var copy)) return;
            if (copy.Owner.Pointer != owner.Pointer) throw new InvalidOperationException("Parry cleanup owner mismatch.");
            copy.Releasing = true;
            var errors = new List<Exception>();
            if (Cleanup(owner, copy.View, copy.Root, errors)) Copies.Remove(owner.Pointer);
            if (errors.Count != 0) LogCleanupError("release", errors);
            if (errors.Count != 0) throw new AggregateException("Parry reaction release failed.", errors);
        }
        private static void LogCleanupError(string stage, List<Exception> errors)
        {
            if (++_cleanupErrors <= 3)
                TrialRepairLog.Write("[enemy-animation] SWORD PARRY ERROR stage=" + stage + "; errors=" + errors.Count + "; first=" + errors[0]);
        }
        private static bool Cleanup(EntityView owner, TimelineActionView view, GameObject root, List<Exception> errors)
        {
            bool detached = view == null;
            if (view != null)
            {
                try
                {
                    var map = owner.m_actionViewMap;
                    if (map == null) throw new InvalidOperationException("Parry action map removal cannot be verified.");
                    if (map.TryGetValue(new Il2CppQuantum.AssetGuid { Value = ParryGuid }, out var before) && before?.Pointer == view.Pointer)
                        owner.UnregisterActionView(view);
                }
                catch (Exception error) { errors.Add(error); }
                try
                {
                    var map = owner.m_actionViewMap;
                    if (map == null) throw new InvalidOperationException("Parry action map removal cannot be verified.");
                    var guid = new Il2CppQuantum.AssetGuid { Value = ParryGuid };
                    if (map.TryGetValue(guid, out var remaining) && remaining?.Pointer == view.Pointer)
                    {
                        // Native GUID overload removes only this dictionary
                        // key. Invoke it only after proving exact ownership.
                        owner.UnregisterActionView(guid);
                        map = owner.m_actionViewMap;
                        if (map == null) throw new InvalidOperationException("Parry action map removal cannot be verified.");
                    }
                    detached = !map.TryGetValue(guid, out var after) || after?.Pointer != view.Pointer;
                    if (!detached) errors.Add(new InvalidOperationException("Parry action map still references the private copy."));
                }
                catch (Exception error) { errors.Add(error); }
                try { view.OnReturnedToPool(); } catch (Exception error) { errors.Add(error); }
            }
            if (root == null) return true;
            try { root.SetActive(false); } catch (Exception error) { errors.Add(error); }
            // Keep the inactive copy in our registry when its map entry could
            // not be removed. A later Unity lifecycle callback can retry.
            if (!detached) return false;
            try { UObject.Destroy(root); return true; } catch (Exception error) { errors.Add(error); return false; }
        }
        public static void StopWithoutUnityReads() { _ready = null; }
    }
}
