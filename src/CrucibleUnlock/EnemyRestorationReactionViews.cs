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
        internal const long BlockedGuid = 1496781608746759672L;
        private sealed class Ready
        {
            public RuntimeAssetResolutionContext Context;
            public HitReactionDataAsset Source;
            public TimelineActionView Template;
            public long Guid;
        }
        private sealed class Owned
        {
            public EntityView Owner;
            public RuntimeAssetResolutionContext Context;
            public GameObject Root;
            public TimelineActionView View;
            public ulong Entity;
            public bool Releasing;
            public IntPtr Weapon, Animator, Sound, Companion;
            public long Guid;
        }
        private static Dictionary<long, Ready> _ready;
        private static readonly Dictionary<(IntPtr Owner, long Guid), Owned> Copies = new Dictionary<(IntPtr, long), Owned>();
        private static readonly HashSet<string> LoggedProfiles = new HashSet<string>(StringComparer.Ordinal);
        private static int _cleanupErrors;

        public static bool Ensure(RuntimeAssetResolutionContext ctx, EnemyRestorationCatalog catalog)
        {
            if (ctx == null || catalog?.Profiles == null) throw new InvalidOperationException("Reaction view context/catalog is missing.");
            var next = new Dictionary<long, Ready>();
            foreach (var guid in new[] { ParryGuid, BlockedGuid })
            {
                if (!ctx.TryFindWrapperImpl(new Il2CppQuantum.AssetGuid { Value = guid }, out var sourceBase, true))
                { _ready = null; return false; }
                var source = sourceBase?.TryCast<HitReactionDataAsset>();
                var template = source?.View?.TryCast<TimelineActionView>();
                if (source == null || source.AssetGuid.Value != guid || template == null)
                    throw new InvalidOperationException("Reaction source wrapper/view type or GUID mismatch: " + guid);
                VerifyTemplate(template, guid);
                if (_ready != null && _ready.TryGetValue(guid, out var previous) && previous.Context.Pointer == ctx.Pointer &&
                    (previous.Source.Pointer != source.Pointer || previous.Template.Pointer != template.Pointer))
                    throw new InvalidOperationException("Reaction source identity changed in the current context: " + guid);
                next.Add(guid, new Ready { Context = ctx, Source = source, Template = template, Guid = guid });
            }
            bool first = _ready == null || _ready[ParryGuid].Context.Pointer != ctx.Pointer;
            _ready = next; // Publish only after both original sources have validated.
            if (first)
                foreach (var pair in next)
                    TrialRepairLog.Write("[enemy-animation] " + Label(pair.Key) + " SOURCE READY guid=" + pair.Key + "; tracks=13; clip=" + ClipName(pair.Key) + "; context=" + ctx.Pointer);
            return true;
        }

        // Only called by the root's small GetActionView postfix after a missing
        // result, target GUID, module readiness and current manager checks.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ActionView TryRepair(EntityView owner, Il2CppQuantum.ActionData action, EnemyRestorationCatalog catalog)
        {
            var guid = action?.Guid.Value ?? 0;
            if (_ready == null || owner == null || !_ready.TryGetValue(guid, out var ready) || Core.AssetCtx?.Pointer != ready.Context.Pointer ||
                !EnemyRestorationOwnerScope.TryGetProfile(owner, catalog, out var profile)) return null;
            var humanoid = owner.TryCast<HumanoidEnemyView>();
            var weapon = humanoid?.MainWeaponView;
            var animator = humanoid?.CharacterAnimator;
            var companion = weapon?.AudioCompanion;
            var sound = companion?.WeaponSoundHost;
            if (weapon == null || animator == null || sound == null || !NeedsReaction(profile, guid, weapon)) return null; // Equipment may still be loading.
            if (ready.Source.AssetGuid.Value != guid || ready.Source.View?.Pointer != ready.Template.Pointer ||
                !ready.Context.TryFindWrapperImpl(new Il2CppQuantum.AssetGuid { Value = guid }, out var sourceNow, false) ||
                sourceNow?.Pointer != ready.Source.Pointer)
                throw new InvalidOperationException("Parry source wrapper ownership was lost.");
            VerifyTemplate(ready.Template, guid);
            var map = owner.m_actionViewMap;
            if (map == null) throw new InvalidOperationException("Parry owner action map is unavailable.");
            map.TryGetValue(new Il2CppQuantum.AssetGuid { Value = guid }, out var mapped);
            var key = (owner.Pointer, guid);
            if (Copies.TryGetValue(key, out var existing))
            {
                if (existing.Owner.Pointer != owner.Pointer || existing.Context.Pointer != ready.Context.Pointer ||
                    existing.Entity != owner.EntityRef.Raw || existing.Root == null || existing.View == null ||
                    existing.Releasing || existing.Weapon != weapon.Pointer || existing.Animator != animator.Pointer || existing.Sound != sound.Pointer || existing.Companion != companion.Pointer ||
                    mapped?.Pointer != existing.View.Pointer || existing.View.EntityOwner?.Pointer != owner.Pointer ||
                    !existing.Root.transform.IsChildOf(owner.transform))
                    throw new InvalidOperationException("Private reaction view owner changed before cleanup.");
                VerifyWeaponAudio(existing.View, guid, companion);
                return existing.View;
            }
            if (mapped != null) throw new InvalidOperationException("Parry action map is owned by a foreign view.");
            GameObject root = null;
            TimelineActionView clone = null;
            bool registered = false;
            try
            {
                root = new GameObject(guid == ParryGuid ? "NRFW_PrivateSwordParryReaction" : "NRFW_PrivateSwordBlockedReaction");
                root.SetActive(false);
                root.transform.SetParent(owner.transform, false, false);
                var copy = UObject.Instantiate(ready.Template.gameObject, root.transform, false);
                clone = copy.GetComponent(Il2CppType.Of<TimelineActionView>())?.TryCast<TimelineActionView>();
                if (clone == null || clone.Pointer == ready.Template.Pointer || clone.QuantumAsset?.AssetGuid.Value != guid ||
                    clone.EffectiveTimeline == null || clone.EffectiveTimeline.Pointer == ready.Template.EffectiveTimeline.Pointer)
                    throw new InvalidOperationException("Parry reaction subtree/timeline was not independently cloned.");
                var records = clone.EffectiveTimeline.EntityRecords;
                if (records == null || records.Count != 13) throw new InvalidOperationException("Parry clone timeline shape changed.");
                int animations = 0, previews = 0, sounds = 0, weapons = 0;
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
                        if (clip?.Clip == null || clip.name != ClipName(guid) || clip.AnimatorDefinition == null ||
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
                    var weaponAudio = item.TryCast<WwiseWeaponAnimator>();
                    if (weaponAudio != null)
                    {
                        if (guid != BlockedGuid || !weaponAudio.UseWeaponSoundHost)
                            throw new InvalidOperationException("Reaction weapon sound track changed.");
                        var reference = weaponAudio.MoonSoundHost?.Copy();
                        if (reference == null || reference.Pointer == weaponAudio.MoonSoundHost.Pointer)
                            throw new InvalidOperationException("Reaction weapon sound reference is shared with source.");
                        reference.SetStaticValue(sound);
                        if (reference.SafeResolve(null)?.Pointer != sound.Pointer) throw new InvalidOperationException("Reaction weapon sound binding failed.");
                        weaponAudio.MoonSoundHost = reference;
                        ++weapons;
                    }
                }
                if (animations != 1 || previews != 1 || sounds != 1 || weapons != (guid == BlockedGuid ? 1 : 0))
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
                    !registeredMap.TryGetValue(new Il2CppQuantum.AssetGuid { Value = guid }, out var registeredView) ||
                    registeredView?.Pointer != clone.Pointer)
                    throw new InvalidOperationException("Parry clone did not register to its current owner.");
                // WwiseWeaponAnimator native pool callback clears its caches;
                // the actual weapon is a sibling of this reaction subtree.
                // Bind only the cloned track after that callback, through the
                // generated reference-field setters (IL2CPP GC write barrier).
                if (guid == BlockedGuid)
                    for (int i = 0; i < records.Count; ++i)
                    {
                        var audio = records[i]?.TimelineItem?.TryCast<WwiseWeaponAnimator>();
                        if (audio == null) continue;
                        audio.m_cachedWeaponCompanion = companion;
                    }
                VerifyWeaponAudio(clone, guid, companion);
                root.SetActive(true);
                Copies.Add(key, new Owned { Owner = owner, Context = ready.Context, Root = root,
                    View = clone, Guid = guid, Companion = companion.Pointer, Entity = owner.EntityRef.Raw, Weapon = weapon.Pointer, Animator = animator.Pointer, Sound = sound.Pointer });
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                if (!Cleanup(owner, registered ? clone : null, root, guid, errors))
                    Copies.Add(key, new Owned { Owner = owner, Context = ready.Context, Root = root,
                        View = registered ? clone : null, Guid = guid, Companion = companion.Pointer, Entity = owner.EntityRef.Raw, Releasing = true,
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
                if (LoggedProfiles.Add(profile.Key + ":" + guid))
                    TrialRepairLog.Write("[enemy-animation] " + Label(guid) + " READY profile=" + profile.Key + "; entity=" + owner.EntityRef.Raw +
                        "; view=" + clone.Pointer + "; animator=" + animator.Pointer + "; weapon=" + weapon.Pointer + "; sound=" + sound.Pointer + "; tracks=13");
            }
            catch { }
            return clone;
        }

        private static string ClipName(long guid) => guid == ParryGuid ? "parriedHitReact" : "blockedHitReact";
        private static string Label(long guid) => guid == ParryGuid ? "SWORD PARRY" : "SWORD BLOCKED";
        private static bool NeedsReaction(EnemyRestorationProfile p, long guid, EnemyWeaponView weapon)
        {
            if (p == null) return false;
            bool shield = p.Key == "keeper_sword_shield_left" || p.Key == "keeper_sword_shield_right";
            bool curved = p.Key == "dead_tide_great_curved_sword";
            if (guid == ParryGuid) return shield || curved;
            // Direct Weapon.RecoilReaction in shipped QDB 22928553: only these
            // two original weapons reference BlockedGuid; neither is private.
            long canonical = shield ? 717510086814971040L : curved ? 1969620323811017082L : 0;
            return guid == BlockedGuid && canonical != 0 && p.WeaponGuid == canonical && weapon.WeaponData.Id.Value == canonical;
        }
        private static void VerifyTemplate(TimelineActionView template, long guid)
        {
            if (template.QuantumAsset?.AssetGuid.Value != guid || template.gameObject?.name != (guid == ParryGuid ? "parriedAttackHitReact" : "blockedAttackHitReaction") ||
                template.EntityOwner != null || template.OwningEntityView != null || template.CharacterView != null ||
                template.EffectiveTimeline == null || template.EffectiveTimeline.EntityRecords == null || template.EffectiveTimeline.EntityRecords.Count != 13)
                throw new InvalidOperationException("Parry source timeline identity/shape changed.");
            int animations = 0, previews = 0, sounds = 0, weapons = 0, scales = 0, voices = 0, steps = 0, foley = 0;
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
                    if (clip?.Clip == null || clip.name != ClipName(guid) || clip.AnimatorDefinition?.name != "playerMoonAnimatorDefinition" || animation.Animator == null)
                        throw new InvalidOperationException("Parry source animation is missing or mismatched.");
                    ++animations;
                }
                if (item.TryCast<WeaponPreviewAnimator>() != null) ++previews;
                var audio = item.TryCast<WwiseSoundAnimator>();
                if (audio != null) { if (audio.MoonSoundHost == null) throw new InvalidOperationException("Parry source sound reference is missing."); ++sounds; }
                var weaponAudio = item.TryCast<WwiseWeaponAnimator>();
                if (weaponAudio != null)
                { if (weaponAudio.MoonSoundHost == null || !weaponAudio.UseWeaponSoundHost) throw new InvalidOperationException("Blocked source weapon sound reference changed."); ++weapons; }
                if (item.TryCast<QuantumTimeScaleAnimator>() != null) ++scales;
                if (item.TryCast<WwiseVoiceAnimator>() != null) ++voices;
                if (item.TryCast<WwiseFootstepAnimator>() != null) ++steps;
                if (item.TryCast<WwiseFoleyAnimator>() != null) ++foley;
            }
            if (animations != 1 || previews != 1 || sounds != 1 || steps != 4 || foley != 2 ||
                weapons != (guid == BlockedGuid ? 1 : 0) || scales != (guid == BlockedGuid ? 1 : 3) || voices != (guid == BlockedGuid ? 2 : 1))
                throw new InvalidOperationException("Parry source external reference shape changed.");
        }

        private static void VerifyWeaponAudio(TimelineActionView view, long guid, WwiseWeaponCompanion companion)
        {
            if (guid != BlockedGuid) return;
            int count = 0;
            var records = view.EffectiveTimeline.EntityRecords;
            for (int i = 0; i < records.Count; ++i)
            {
                var audio = records[i]?.TimelineItem?.TryCast<WwiseWeaponAnimator>();
                if (audio == null) continue;
                if (!audio.UseWeaponSoundHost || audio.m_cachedWeaponCompanion?.Pointer != companion.Pointer ||
                    audio.EffectiveWeaponCompanion?.Pointer != companion.Pointer ||
                    audio.EffectiveSoundHost?.Pointer != companion.WeaponSoundHost?.Pointer)
                    throw new InvalidOperationException("Blocked reaction weapon companion/host ownership was lost.");
                ++count;
            }
            if (count != 1) throw new InvalidOperationException("Blocked reaction weapon audio track is absent.");
        }

        // Owner lifecycle cleanup also retries records retained across context reset.
        public static void Release(EntityView owner)
        {
            if (owner == null) return;
            var errors = new List<Exception>();
            var detached = new List<(IntPtr Owner, long Guid)>();
            foreach (var pair in Copies)
            {
                if (pair.Key.Owner != owner.Pointer) continue;
                var copy = pair.Value;
                if (copy.Owner.Pointer != owner.Pointer) throw new InvalidOperationException("Reaction cleanup owner mismatch.");
                copy.Releasing = true;
                if (Cleanup(owner, copy.View, copy.Root, copy.Guid, errors)) detached.Add(pair.Key);
            }
            foreach (var key in detached) Copies.Remove(key);
            if (errors.Count != 0) LogCleanupError("release", errors);
            if (errors.Count != 0) throw new AggregateException("Reaction release failed.", errors);
        }
        private static void LogCleanupError(string stage, List<Exception> errors)
        {
            if (++_cleanupErrors <= 3)
                TrialRepairLog.Write("[enemy-animation] SWORD PARRY ERROR stage=" + stage + "; errors=" + errors.Count + "; first=" + errors[0]);
        }
        private static bool Cleanup(EntityView owner, TimelineActionView view, GameObject root, long reactionGuid, List<Exception> errors)
        {
            bool detached = view == null;
            if (view != null)
            {
                try
                {
                    var map = owner.m_actionViewMap;
                    if (map == null) throw new InvalidOperationException("Parry action map removal cannot be verified.");
                    if (map.TryGetValue(new Il2CppQuantum.AssetGuid { Value = reactionGuid }, out var before) && before?.Pointer == view.Pointer && view.QuantumAsset?.AssetGuid.Value == reactionGuid)
                        owner.UnregisterActionView(view);
                }
                catch (Exception error) { errors.Add(error); }
                try
                {
                    var map = owner.m_actionViewMap;
                    if (map == null) throw new InvalidOperationException("Parry action map removal cannot be verified.");
                    var guid = new Il2CppQuantum.AssetGuid { Value = reactionGuid };
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
