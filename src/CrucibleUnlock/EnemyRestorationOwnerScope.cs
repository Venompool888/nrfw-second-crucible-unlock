using System;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;
using EntityView = Il2CppMoon.Forsaken.EntityView;

namespace CrucibleUnlock
{
    // A Unity view's serialized QuantumAsset still belongs to the shipped
    // prefab. Resolve its CURRENT simulation attachment to identify one of our
    // private NPCs; never infer ownership from a prefab name or cached Frame.
    internal static class EnemyRestorationOwnerScope
    {
        internal static unsafe bool TryGetProfile(EntityView view, EnemyRestorationCatalog catalog,
            out EnemyRestorationProfile profile)
        {
            profile = null;
            if (view == null || catalog?.Profiles == null || !view.IsBoundToEntity)
                return false;
            var entity = view.EntityRef;
            if (entity.Raw == 0) return false;
            var frame = view.PredictedFrame;
            if (frame == null || !EnemyRestorationRuntime.IsSimulationViewsReady(frame)) return false;
            var manager = frame.Context?.ResourceManager;
            var currentManager = Core.ResourceManager;
            if (manager == null || currentManager == null || manager.Pointer == IntPtr.Zero ||
                manager.Pointer != currentManager.Pointer || !frame.Has<NpcAttachmentComponent>(entity))
                return false;
            if (NpcAttachmentComponent.SIZE != 144 || NpcDataRef.SIZE != 4 || EntityViewDataRef.SIZE != 4)
                throw new InvalidOperationException("Native NPC attachment component layout mismatch.");
            // EntityRefExtensions.Get<T> is an official AOT specialization and
            // returns an UNBOXED component pointer. NpcAttachmentComponent has
            // reference-bearing fields and must never be constructed at it.
            var pointer = EntityRefExtensions.Get<NpcAttachmentComponent>(entity, frame);
            if (pointer == null)
                throw new InvalidOperationException("NPC attachment Has=true/Get=null.");
            var raw = (byte*)pointer;
            int viewIndex = *(int*)(raw + 16); // EntityViewDataRef.m_index
            int npcIndex = *(int*)(raw + 24);  // NpcDataRef.m_index
            if (viewIndex <= 0 || npcIndex <= 0) return false;
            var storage = frame.InstanceDataStorage;
            var viewSlot = storage?.m_storageEntityViewData;
            var npcSlot = storage?.m_storageNpcData;
            if (viewSlot?.m_storage == null || npcSlot?.m_storage == null ||
                viewIndex > viewSlot.m_storage.Count || npcIndex > npcSlot.m_storage.Count)
                return false;
            var viewData = viewSlot.m_storage[viewIndex - 1];
            var npcData = npcSlot.m_storage[npcIndex - 1]?.TryCast<NpcHumanoidEnemyData>();
            if (viewData == null || npcData == null || viewData.Pointer == IntPtr.Zero || npcData.Pointer == IntPtr.Zero)
                return false;
            long viewGuid = viewData.Guid.Value;
            long npcGuid = npcData.Guid.Value;
            var candidates = catalog.Profiles;
            for (int i = 0; i < candidates.Length; ++i)
            {
                var candidate = candidates[i];
                if (candidate != null && candidate.Index == i && candidate.ViewGuid == viewGuid &&
                    candidate.NpcGuid == npcGuid &&
                    viewSlot.m_lookup.TryGetValue(viewGuid, out int mappedView) && mappedView == viewIndex &&
                    npcSlot.m_lookup.TryGetValue(npcGuid, out int mappedNpc) && mappedNpc == npcIndex &&
                    frame.DynamicAssetDB.FindAsset(new AssetGuid { Value = viewGuid })?.Pointer == viewData.Pointer &&
                    frame.DynamicAssetDB.FindAsset(new AssetGuid { Value = npcGuid })?.Pointer == npcData.Pointer &&
                    viewData.Path == candidate.Path("view") && npcData.Path == candidate.Path("npc") &&
                    npcData.EntityData.Id.Value == candidate.HostGuid &&
                    npcData.ControllerData.Id.Value == candidate.ControllerGuid &&
                    npcData.ViewDataOverride.Id.Value == candidate.ViewGuid &&
                    npcData.TeamData.Id.Value == candidate.TeamGuid)
                {
                    profile = candidate;
                    return true;
                }
            }
            return false;
        }
    }
}
