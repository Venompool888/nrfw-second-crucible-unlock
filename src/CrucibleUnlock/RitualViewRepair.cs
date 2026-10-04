using System;
using System.Collections.Generic;
using PatchOwner = HarmonyLib.Harmony;
using Il2CppMoon;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;
using EntityView = Il2CppMoon.Forsaken.EntityView;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace CrucibleUnlock
{
    // The installed second bowl has no offering ActionView, resolver links or blood decals.
    // Reuse the first bowl's scene-owned presentation without changing Quantum actions/assets.
    internal static class RitualViewRepair
    {
        private const int AnimatorLink = -1965569097, LeftHandLink = 897781494;
        private static PatchOwner _harmony;
        private static bool _enabled;
        private static int _errors;

        public static void Install()
        {
            var h = new PatchOwner("NRFW.CrucibleUnlock.RitualView");
            try
            {
                TrialRepairLog.Patch(h, typeof(EntityView), "GetActionView", new[] { typeof(ActionData), typeof(bool).MakeByRefType() },
                    typeof(RitualViewRepair), postfix: nameof(AfterGetActionView), returnType: typeof(ActionView));
                _harmony = h; _enabled = true;
                TrialRepairLog.Write("[ritual-view] installed missing second-bowl offering view repair; first-bowl scene template only");
            }
            catch { _enabled = false; h.UnpatchSelf(); throw; }
        }

        private static void AfterGetActionView(EntityView __instance, ActionData __0, ref ActionView __result)
        {
            if (!_enabled || __instance == null || __result != null || __0 == null || __0.Guid.Value != TrialRepairPolicy.OfferingGuid) return;
            GameObject root = null;
            var decals = new List<GameObject>();
            GeneralInteractableView bowl = null;
            DynamicDataResolver original = null, installed = null;
            InteractionView cloned = null;
            bool registering = false;
            string stage = "match-bowl";
            try
            {
                bowl = __instance.TryCast<GeneralInteractableView>();
                if (bowl == null || bowl.Data?.Name != "secondStatueBowlInteraction" ||
                    bowl.DataAsset.Id.Value != RitualViewPolicy.BowlDataGuid || !bowl.IsBoundToEntity) return;

                GeneralInteractableView first = null;
                InteractionView template = null;
                int count = 0;
                stage = "find-template";
                // Limit discovery to the sibling statues in this loaded room.
                var statues = bowl.transform.parent?.parent;
                if (statues == null) throw new InvalidOperationException("Missing statue hierarchy");
                foreach (var candidate in statues.GetComponentsInChildren<GeneralInteractableView>(true))
                {
                    if (candidate == null || candidate.Data?.Name != "firstStatueBowlInteraction" ||
                        candidate.DataAsset.Id.Value != RitualViewPolicy.BowlDataGuid || !candidate.IsBoundToEntity) continue;
                    foreach (var view in candidate.GetComponentsInChildren<InteractionView>(true))
                        if (view != null && view.QuantumAsset?.AssetGuid.Value == TrialRepairPolicy.OfferingGuid)
                        { first = candidate; template = view; ++count; }
                }
                bool sameScene = first != null && first.gameObject.scene.handle == bowl.gameObject.scene.handle;
                if (!RitualViewPolicy.ShouldCreate(__0.Guid.Value, bowl.DataAsset.Id.Value, bowl.Data.Name, true,
                    bowl.IsBoundToEntity, count, sameScene)) throw new InvalidOperationException("Offering template count=" + count);

                stage = "copy-resolver";
                original = bowl.m_dataResolver;
                if (original?.SerializedDataItems != null && original.SerializedDataItems.Count != 0)
                    throw new InvalidOperationException("Second bowl resolver is no longer empty");
                installed = CopyResolver(first, bowl);
                bowl.m_dataResolver = installed;

                // Keep the new hierarchy inactive until every external reference is retargeted.
                stage = "parent-inactive-root";
                root = new GameObject("NRFW_SecondBowlOffering");
                root.SetActive(false);
                // Moon's Unity build only exports the three-argument overload; the two-argument
                // generated method is a literal NotSupportedException (unstripping failed) stub.
                root.transform.SetParent(bowl.transform, false, false);
                stage = "clone-view";
                var cloneObject = UObject.Instantiate(template.gameObject, root.transform, false);
                cloned = cloneObject.GetComponent<InteractionView>();
                if (cloned == null || cloned.Pointer == template.Pointer) throw new InvalidOperationException("Offering clone missing");
                stage = "bind-view";
                cloned.Interactable = new MoonReference<IInteractableView>(bowl.Cast<IInteractableView>());
                var resolver = installed.Cast<IMoonTypeResolver>();
                cloned.MainAnimatorRef = template.MainAnimatorRef.Copy();
                cloned.MainAnimatorRef.SetResolver(resolver, bowl, AnimatorLink);

                int animations = 0, instances = 0, materials = 0;
                var timeline = cloned.EffectiveTimeline;
                var records = timeline?.EntityRecords;
                if (timeline == null || timeline.Pointer == template.EffectiveTimeline?.Pointer ||
                    records == null || records.Count != 9) throw new InvalidOperationException("Offering timeline was not independently cloned");
                var firstModel = first.transform.parent.Find("bowlMetalA_Model");
                var secondModel = bowl.transform.parent.Find("bowlMetalA_Model");
                if (firstModel == null || secondModel == null) throw new InvalidOperationException("Bowl model missing");
                var decalMap = new Dictionary<IntPtr, GameObject>();
                stage = "clone-decals";
                foreach (string name in new[] { "bloodFlowDecal_1", "bloodFlowDecal_2", "bloodFlowDecal_2 (1)" })
                {
                    var source = firstModel.Find(name);
                    if (source == null || secondModel.Find(name) != null) throw new InvalidOperationException("Unexpected blood decal hierarchy");
                    var copy = UObject.Instantiate(source.gameObject, secondModel, false);
                    decals.Add(copy);
                    copy.name = name;
                    decalMap.Add(source.gameObject.Pointer, copy);
                }
                stage = "adapt-blood-route";
                bool bloodAligned = RitualBloodRoute.TryApply(firstModel, decalMap);
                stage = "bind-tracks";
                foreach (var record in records)
                {
                    var item = record?.TimelineItem;
                    if (item == null) continue;
                    var component = item.TryCast<Component>();
                    if (component == null || !component.transform.IsChildOf(cloneObject.transform))
                        throw new InvalidOperationException("Timeline item still belongs to the source hierarchy");
                    var animation = item.TryCast<ForsakenAnimationPlayer>();
                    if (animation != null && animation.EffectiveAnimation?.name == "sacrifice")
                    {
                        var reference = animation.Animator.Copy();
                        reference.SetResolver(resolver, bowl, AnimatorLink);
                        animation.Animator = reference;
                        ++animations;
                    }
                    var spawn = item.TryCast<InstantiationAnimator>();
                    if (spawn != null)
                    {
                        var reference = spawn.InstantiationTransform.Copy();
                        reference.SetResolver(resolver, bowl, LeftHandLink);
                        spawn.InstantiationTransform = reference;
                        ++instances;
                    }
                    var material = item.TryCast<FloatMaterialPropertyAnimator>();
                    if (material != null)
                    {
                        var previous = material.Target?.SafeResolve(null);
                        if (previous == null || !decalMap.TryGetValue(previous.Pointer, out var next))
                            throw new InvalidOperationException("Blood material target is not the first-bowl decal");
                        material.Target = new MoonReference<GameObject>(next);
                        ++materials;
                    }
                }
                if (animations != 1 || instances != 2 || materials != 1)
                    throw new InvalidOperationException("Unexpected offering tracks: " + animations + "/" + instances + "/" + materials);
                stage = "activate-register";
                root.SetActive(true);
                registering = true;
                bowl.RegisterActionView(cloned);
                __result = cloned;
                TrialRepairLog.Write("[ritual-view] CREATED bowl=" + bowl.EntityRef.Raw + "; view=" + cloned.Pointer +
                    "; template=" + template.Pointer + "; tracks=9; handVfx=2; bowlDecals=" + decals.Count + "; bloodRouteAligned=" + bloodAligned + "; original timeline retained");
            }
            catch (Exception e)
            {
                // Only undo objects/references created by this attempt.
                try
                {
                    if (registering && bowl != null && cloned != null) bowl.UnregisterActionView(cloned);
                    if (bowl != null && installed != null && bowl.m_dataResolver?.Pointer == installed.Pointer) bowl.m_dataResolver = original;
                    if (root != null) { root.SetActive(false); UObject.Destroy(root); }
                    foreach (var decal in decals) if (decal != null) { decal.SetActive(false); UObject.Destroy(decal); }
                }
                catch (Exception cleanup)
                {
                    _enabled = false;
                    TrialRepairLog.Write("[ritual-view] cleanup ERROR; repair disabled: " + cleanup.GetType().Name + ": " + cleanup.Message);
                }
                if (++_errors <= 3) TrialRepairLog.Write("[ritual-view] ERROR stage=" + stage + "; " + e);
                if (_errors >= 3) _enabled = false;
            }
        }

        private static DynamicDataResolver CopyResolver(GeneralInteractableView first, GeneralInteractableView second)
        {
            var source = first.m_dataResolver?.SerializedDataItems;
            if (source == null || source.Count != 2) throw new InvalidOperationException("Unexpected first-bowl resolver");
            var result = new DynamicDataResolver
            {
                SerializedDataItems = new Il2CppSystem.Collections.Generic.List<DynamicDataResolver.SerializedDynamicDataLinkItem>()
            };
            int mask = 0;
            foreach (var item in source)
            {
                var data = item.DataLinkItem;
                int field = item.Guid == AnimatorLink ? 0 : item.Guid == LeftHandLink ? 2 : -1;
                if (data == null || field == -1 || data.ClassID != 1016 || data.FieldID != field ||
                    (int)data.TargetMemberType != 0 || data.TargetObject?.Pointer != first.Pointer)
                    throw new InvalidOperationException("Unexpected first-bowl dynamic link");
                mask |= field == 0 ? 1 : 2;
                result.SerializedDataItems.Add(new DynamicDataResolver.SerializedDynamicDataLinkItem
                {
                    Guid = item.Guid,
                    DataLinkItem = new DynamicDataLinkSerializedData
                    {
                        TargetObject = second, TargetMemberType = data.TargetMemberType, ClassID = data.ClassID, FieldID = data.FieldID
                    }
                });
            }
            if (mask != 3) throw new InvalidOperationException("Missing dynamic link");
            result.OnAfterDeserialize();
            return result;
        }
    }
}
