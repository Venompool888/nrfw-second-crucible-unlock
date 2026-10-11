using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;
using Il2CppMoon.Forsaken;
using UnityEngine;

namespace CrucibleUnlock
{
    internal static partial class RitualViewRepair
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int FindOfferingTemplate(Transform statues, out GeneralInteractableView first, out InteractionView template)
        {
            first = null; template = null;
            int count = 0, visited = 0;
            var candidates = new Stack<Transform>();
            candidates.Push(statues);
            while (candidates.Count != 0)
            {
                var node = candidates.Pop();
                if (node == null || ++visited > 4096) throw new InvalidOperationException("Invalid statue hierarchy");
                // The shipped bowl hierarchy has at most one target component
                // of each type per GameObject. This includes inactive nodes and
                // the root, without importing Il2CppArrayBase<T> into this JIT.
                var candidate = node.gameObject.GetComponent(Il2CppType.Of<GeneralInteractableView>())?.TryCast<GeneralInteractableView>();
                for (int i = node.childCount - 1; i >= 0; --i) candidates.Push(node.GetChild(i));
                if (candidate == null || candidate.Data?.Name != "firstStatueBowlInteraction" ||
                    candidate.DataAsset.Id.Value != RitualViewPolicy.BowlDataGuid || !candidate.IsBoundToEntity) continue;
                var views = new Stack<Transform>();
                views.Push(candidate.transform);
                int viewNodes = 0;
                while (views.Count != 0)
                {
                    var child = views.Pop();
                    if (child == null || ++viewNodes > 4096) throw new InvalidOperationException("Invalid offering hierarchy");
                    var view = child.gameObject.GetComponent(Il2CppType.Of<InteractionView>())?.TryCast<InteractionView>();
                    for (int i = child.childCount - 1; i >= 0; --i) views.Push(child.GetChild(i));
                    if (view != null && view.QuantumAsset?.AssetGuid.Value == TrialRepairPolicy.OfferingGuid)
                    { first = candidate; template = view; ++count; }
                }
            }
            return count;
        }
    }
}
