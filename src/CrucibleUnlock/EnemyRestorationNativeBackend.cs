using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppQuantum;
using Il2CppQuantum.AiControl;
using Il2CppPhoton.Deterministic;

namespace CrucibleUnlock
{
    // Only constructed from the real simulation OnInit callback, never a polled Frame.
    internal sealed class EnemyRestorationNativeBackend : IEnemyRestorationBackend
    {
        private readonly Frame _frame;
        private readonly Dictionary<long,IntPtr> _attemptedPointers=new Dictionary<long,IntPtr>();
        public EnemyRestorationNativeBackend(Frame frame) { _frame=frame ?? throw new ArgumentNullException(nameof(frame)); }
        public object Find(long guid) => _frame.DynamicAssetDB.FindAsset(new AssetGuid { Value=guid });

        public object Clone(long sourceGuid, EnemyAssetRole role)
        {
            var source=_frame.Context.ResourceManager.GetAsset(new AssetGuid { Value=sourceGuid });
            if(source==null || !IsRole(source,role)) throw new InvalidOperationException("Enemy source missing/type mismatch: " + sourceGuid);
            // Round-trip with the game's actual asset serializer; no shallow sharing of
            // mutable action containers, movement settings or NPC affix maps.
            var serializer=_frame.Context.AssetSerializer;
            var bytes=IAssetSerializerExtensions.AssetToByteArray(serializer,source);
            var clones=IAssetSerializerExtensions.AssetsFromByteArray(serializer,bytes);
            if(clones==null || clones.Length!=1 || clones[0]==null || clones[0].Pointer==source.Pointer || !IsRole(clones[0],role))
                throw new InvalidOperationException("Enemy deep clone failed: " + sourceGuid);
            return clones[0];
        }

        public object CreateView() => new EntityViewData();
        private static bool IsRole(AssetObject a, EnemyAssetRole role) => role switch {
            EnemyAssetRole.View => a.TryCast<EntityViewData>()!=null,
            EnemyAssetRole.Weapon => a.TryCast<EnemyWeaponData>()!=null,
            EnemyAssetRole.Host => a.TryCast<HumanoidEnemyData>()!=null,
            EnemyAssetRole.Controller => a.TryCast<HumanoidEnemyAiControllerData>()!=null,
            EnemyAssetRole.Npc => a.TryCast<NpcHumanoidEnemyData>()!=null,
            _ => false
        };

        public void Configure(object obj, EnemyAssetDefinition d)
        {
            var a=(AssetObject)obj;
            a.Guid=new AssetGuid { Value=d.Guid }; a.Path=d.Path;
            var p=d.Profile;
            switch(d.Role)
            {
                case EnemyAssetRole.Host:
                    var host=a.Cast<HumanoidEnemyData>();
                    host.ViewData=new AssetRefEntityViewData { Id=new AssetGuid { Value=p.ViewGuid } };
                    host.IsBoss=false;
                    break;
                case EnemyAssetRole.Controller:
                    var ai=a.Cast<HumanoidEnemyAiControllerData>();
                    RequireWeapon(p.WeaponGuid);
                    if(p.OffhandGuid!=0) RequireWeapon(p.OffhandGuid);
                    ai.Weapon=new AssetRefEnemyWeaponData { Id=new AssetGuid { Value=p.BlockedActions.Length>0?p.PrivateWeaponGuid:p.WeaponGuid } };
                    ai.OffHandItem=new AssetRefEnemyWeaponData { Id=new AssetGuid { Value=p.OffhandGuid } };
                    ai.ActionList.ActionGroups=new Il2CppReferenceArray<ActionSelectionGroup>(0);
                    ai.UnlockActions.Actions=new Il2CppReferenceArray<UnlockActionSelection>(0);
                    ai.AffixMoveset=new AssetRefAffixMovesetData();
                    break;
                case EnemyAssetRole.Npc:
                    var npc=a.Cast<NpcHumanoidEnemyData>();
                    npc.EntityData=new AssetRefHumanoidEnemyData { Id=new AssetGuid { Value=p.HostGuid } };
                    npc.ControllerData=new AssetRefHumanoidEnemyAiControllerData { Id=new AssetGuid { Value=p.ControllerGuid } };
                    npc.ViewDataOverride=new AssetRefEntityViewData { Id=new AssetGuid { Value=p.ViewGuid } };
                    npc.TeamData=new AssetRefTeamData { Id=new AssetGuid { Value=p.TeamGuid } };
                    npc.CharacterType=(CharacterType)p.CharacterType; npc.EnemyFaction=(EnemyFaction)p.EnemyFaction;
                    npc.UseRandomGender=false; npc.SpawnRate=new FP { RawValue=0 };
                    npc.NativeRegions=new Il2CppSystem.Collections.Generic.List<GameplayBalanceRegions>();
                    // LootSource is a native value struct projected as a managed class.
                    // Its generated parameterless constructors collide; the getters box
                    // the existing native value, and the setters copy the complete value.
                    var emptyLoot=new Il2CppStructArray<AssetRefLootSourceData>(0);
                    ClearLoot(npc,"LootCollection",emptyLoot);
                    ClearLoot(npc,"LootCollectionTwoPlayer",emptyLoot);
                    ClearLoot(npc,"LootCollectionThreePlayer",emptyLoot);
                    ClearLoot(npc,"LootCollectionFourPlayer",emptyLoot);
                    break;
                case EnemyAssetRole.Weapon:
                    PruneWeapon(a.Cast<EnemyWeaponData>(),p.BlockedActions);
                    break;
            }
        }

        private static void ClearLoot(NpcData npc, string fieldName,
            Il2CppStructArray<AssetRefLootSourceData> empty)
        {
            // LootSource is a native value struct projected as a class. Its NpcData
            // setter copies the whole struct with cpblk, skipping the GC barrier.
            // Write the sole native reference field in place through IL2CPP instead.
            var outer=IL2CPP.il2cpp_class_get_field_from_name(
                Il2CppClassPointerStore<NpcData>.NativeClassPtr,fieldName);
            var lootClass=Il2CppClassPointerStore<LootSource>.NativeClassPtr;
            var inner=IL2CPP.il2cpp_class_get_field_from_name(lootClass,"Sources");
            uint alignment=0;
            var size=IL2CPP.il2cpp_class_value_size(lootClass,ref alignment);
            if(outer==IntPtr.Zero || inner==IntPtr.Zero || size<=0 ||
                !IL2CPP.il2cpp_class_is_valuetype(lootClass))
                throw new InvalidOperationException("NPC loot field ABI is incompatible: " + fieldName);
            var innerOffset=checked((int)IL2CPP.il2cpp_field_get_offset(inner))-2*IntPtr.Size;
            if(innerOffset<0 || innerOffset+IntPtr.Size>size)
                throw new InvalidOperationException("NPC loot source offset is invalid.");
            var offset=checked((int)IL2CPP.il2cpp_field_get_offset(outer)+innerOffset);
            IL2CPP.il2cpp_gc_wbarrier_set_field(npc.Pointer,IntPtr.Add(npc.Pointer,offset),empty.Pointer);
        }

        private void RequireWeapon(long guid)
        {
            var asset=_frame.Context.ResourceManager.GetAsset(new AssetGuid { Value=guid });
            if(asset==null || asset.TryCast<EnemyWeaponData>()==null)
                throw new InvalidOperationException("Original enemy weapon/shield is missing: " + guid);
        }

        private static bool Keep(ActionSelection entry, long[] blocked)
        {
            if(entry==null) throw new InvalidOperationException("Null weapon action selection.");
            var combos=entry.ComboParams;
            return EnemyRestorationRegistration.KeepAction(entry.Data.Id.Value,
                combos==null?Array.Empty<long>():combos.Select(c=>c.Action.Id.Value),blocked);
        }
        private static void PruneWeapon(EnemyWeaponData weapon, long[] blocked)
        {
            int removed=0;
            var groups=weapon.ActionList?.ActionGroups;
            if(groups==null || weapon.UnlockActions==null || weapon.UnlockActions.Actions==null)
                throw new InvalidOperationException("Enemy weapon action containers are missing.");
            for(int i=0;i<groups.Length;i++)
            {
                var group=groups[i];
                if(group==null || group.Actions==null)
                    throw new InvalidOperationException("Enemy weapon action group is missing.");
                var before=group.Actions;
                var keep=before.Where(a=>Keep(a,blocked)).ToArray(); removed+=before.Length-keep.Length;
                if(keep.Length!=before.Length) SetGroupActions(groups,i,BuildActionArray(keep));
            }
            var unlocks=weapon.UnlockActions.Actions;
            var keptUnlocks=unlocks.Where(u=>Keep(u.Action,blocked)).ToArray(); removed+=unlocks.Length-keptUnlocks.Length;
            if(keptUnlocks.Length!=unlocks.Length)
                weapon.UnlockActions.Actions=BuildUnlockArray(keptUnlocks);
            if(removed==0 || !WeaponIsPruned(weapon,blocked))
                throw new InvalidOperationException("Expected incomplete weapon action was not fully pruned.");
        }

        private static Il2CppReferenceArray<ActionSelection> BuildActionArray(ActionSelection[] actions)
        {
            var result=new Il2CppReferenceArray<ActionSelection>(actions.Length);
            var elementSize=IL2CPP.il2cpp_array_element_size(IL2CPP.il2cpp_object_get_class(result.Pointer));
            if(elementSize!=IntPtr.Size)
                throw new InvalidOperationException("Action reference array ABI is incompatible.");
            for(int i=0;i<actions.Length;i++)
            {
                if(actions[i]==null) throw new InvalidOperationException("Null retained weapon action.");
                var target=IntPtr.Add(result.Pointer,checked(4*IntPtr.Size+i*elementSize));
                IL2CPP.il2cpp_gc_wbarrier_set_field(result.Pointer,target,actions[i].Pointer);
            }
            return result;
        }

        private static Il2CppReferenceArray<UnlockActionSelection> BuildUnlockArray(UnlockActionSelection[] unlocks)
        {
            // Native UnlockActionSelection contains only MinLevel (byte) and
            // Action (reference). Populate a new value array without the
            // generated indexer's barrier-free CopyBlock.
            var result=new Il2CppReferenceArray<UnlockActionSelection>(unlocks.Length);
            var klass=Il2CppClassPointerStore<UnlockActionSelection>.NativeClassPtr;
            uint alignment=0;
            var size=IL2CPP.il2cpp_class_value_size(klass,ref alignment);
            var elementSize=IL2CPP.il2cpp_array_element_size(IL2CPP.il2cpp_object_get_class(result.Pointer));
            var levelField=IL2CPP.il2cpp_class_get_field_from_name(klass,"MinLevel");
            var actionField=IL2CPP.il2cpp_class_get_field_from_name(klass,"Action");
            if(levelField==IntPtr.Zero || actionField==IntPtr.Zero || size<=0 ||
                size!=elementSize || !IL2CPP.il2cpp_class_is_valuetype(klass))
                throw new InvalidOperationException("Unlock action array ABI is incompatible.");
            var levelOffset=checked((int)IL2CPP.il2cpp_field_get_offset(levelField))-2*IntPtr.Size;
            var actionOffset=checked((int)IL2CPP.il2cpp_field_get_offset(actionField))-2*IntPtr.Size;
            if(levelOffset<0 || levelOffset>=size || actionOffset<0 || actionOffset+IntPtr.Size>size)
                throw new InvalidOperationException("Unlock action field offsets are invalid.");
            for(int i=0;i<unlocks.Length;i++)
            {
                var item=unlocks[i];
                if(item==null || item.Action==null) throw new InvalidOperationException("Null retained unlock action.");
                var element=IntPtr.Add(result.Pointer,checked(4*IntPtr.Size+i*elementSize));
                Marshal.WriteByte(IntPtr.Add(element,levelOffset),item.MinLevel);
                IL2CPP.il2cpp_gc_wbarrier_set_field(result.Pointer,
                    IntPtr.Add(element,actionOffset),item.Action.Pointer);
            }
            return result;
        }

        private static void SetGroupActions(Il2CppReferenceArray<ActionSelectionGroup> groups,
            int index, Il2CppReferenceArray<ActionSelection> actions)
        {
            // ActionSelectionGroup is a native value struct with reference fields.
            // ReferenceArray<T> reads it by boxing; mutating that box does not
            // update the array. Its generated indexer writes by CopyBlock without
            // a GC barrier, so write only the changed reference through IL2CPP.
            var groupClass=Il2CppClassPointerStore<ActionSelectionGroup>.NativeClassPtr;
            uint alignment=0;
            var valueSize=IL2CPP.il2cpp_class_value_size(groupClass,ref alignment);
            var arrayClass=IL2CPP.il2cpp_object_get_class(groups.Pointer);
            var elementSize=IL2CPP.il2cpp_array_element_size(arrayClass);
            var field=IL2CPP.il2cpp_class_get_field_from_name(groupClass,"Actions");
            if(field==IntPtr.Zero || valueSize<=0 || valueSize!=elementSize ||
                !IL2CPP.il2cpp_class_is_valuetype(groupClass))
                throw new InvalidOperationException("Action group array ABI is incompatible.");
            var boxedOffset=checked((int)IL2CPP.il2cpp_field_get_offset(field));
            var offset=boxedOffset-2*IntPtr.Size;
            if(offset<0 || offset+IntPtr.Size>elementSize || index<0 || index>=groups.Length)
                throw new InvalidOperationException("Action group field offset is invalid.");
            // The installed Interop.Runtime ArrayStartPointer uses an Il2CppObject
            // header plus bounds and max_length: four pointer-sized fields on x64.
            var target=IntPtr.Add(groups.Pointer,checked(4*IntPtr.Size+index*elementSize+offset));
            IL2CPP.il2cpp_gc_wbarrier_set_field(groups.Pointer,target,actions.Pointer);
        }

        private static bool WeaponIsPruned(EnemyWeaponData weapon, long[] blocked)
        {
            var groups=weapon.ActionList?.ActionGroups;
            var unlocks=weapon.UnlockActions?.Actions;
            if(groups==null || unlocks==null) return false;
            int retained=0;
            foreach(var group in groups)
            {
                if(group==null || group.Actions==null) return false;
                foreach(var action in group.Actions)
                {
                    if(!Keep(action,blocked)) return false;
                    retained++;
                }
            }
            foreach(var unlock in unlocks)
            {
                if(unlock==null || !Keep(unlock.Action,blocked)) return false;
                retained++;
            }
            return retained>0;
        }

        public bool Matches(object obj, EnemyAssetDefinition d)
        {
            var a=obj as AssetObject;
            if(a==null || a.Guid.Value!=d.Guid || a.Path!=d.Path || !IsRole(a,d.Role)) return false;
            var p=d.Profile;
            switch(d.Role)
            {
                case EnemyAssetRole.Host:
                    var host=a.Cast<HumanoidEnemyData>();
                    return host.ViewData.Id.Value==p.ViewGuid && !host.IsBoss;
                case EnemyAssetRole.Controller:
                    var ai=a.Cast<HumanoidEnemyAiControllerData>();
                    return ai.Weapon.Id.Value==(p.BlockedActions.Length>0?p.PrivateWeaponGuid:p.WeaponGuid) &&
                        ai.OffHandItem.Id.Value==p.OffhandGuid && ai.AffixMoveset.Id.Value==0 &&
                        ai.ActionList.ActionGroups.Length==0 && ai.UnlockActions.Actions.Length==0;
                case EnemyAssetRole.Npc:
                    var n=a.Cast<NpcHumanoidEnemyData>();
                    return n.EntityData.Id.Value==p.HostGuid && n.ControllerData.Id.Value==p.ControllerGuid &&
                        n.EntityDataAssetRef.Id.Value==p.HostGuid && n.ControllerDataAssetRef.Id.Value==p.ControllerGuid &&
                        n.ViewDataOverride.Id.Value==p.ViewGuid && n.TeamData.Id.Value==p.TeamGuid &&
                        (int)n.CharacterType==p.CharacterType && (int)n.EnemyFaction==p.EnemyFaction &&
                        !n.UseRandomGender && n.SpawnRate.RawValue==0 && n.NativeRegions!=null && n.NativeRegions.Count==0 &&
                        EmptyLoot(n.LootCollection) && EmptyLoot(n.LootCollectionTwoPlayer) &&
                        EmptyLoot(n.LootCollectionThreePlayer) && EmptyLoot(n.LootCollectionFourPlayer);
                case EnemyAssetRole.Weapon: return WeaponIsPruned(a.Cast<EnemyWeaponData>(),p.BlockedActions);
                default:return true;
            }
        }
        private static bool EmptyLoot(LootSource loot) => loot!=null && loot.Sources!=null && loot.Sources.Length==0;
        public void Register(object asset, EnemyAssetDefinition d)
        {
            var a=(AssetObject)asset;
            if(((ulong)d.Guid>>62)!=3 || _attemptedPointers.ContainsKey(d.Guid) ||
                !Matches(a,d) || Find(d.Guid)!=null)
                throw new InvalidOperationException("Enemy asset changed or GUID occupied before registration: " + d.Path);
            // Native DynamicAssetDB.AddAsset(asset, explicitGuid) rejects an
            // asset whose own Guid is already nonzero. The explicit ID is applied
            // by AddAsset after its collision and dynamic-type checks.
            a.Guid=new AssetGuid { Value=0 };
            // Record ownership only when about to enter the native insertion.
            // The caller includes this slot in rollback even if AddAsset throws.
            _attemptedPointers.Add(d.Guid,a.Pointer);
            _frame.AddAsset(a,new AssetGuid { Value=d.Guid });
        }
        public void Remove(EnemyAssetDefinition d)
        {
            if(!_attemptedPointers.TryGetValue(d.Guid,out var attemptedPointer)) return;
            var found=Find(d.Guid);
            if(found==null) { _attemptedPointers.Remove(d.Guid); return; }
            if(((AssetObject)found).Pointer!=attemptedPointer)
                throw new InvalidOperationException("Refusing to remove an asset with a different native pointer: " + d.Path);
            if(!_frame.DisposeAsset(new AssetGuid { Value=d.Guid })) throw new InvalidOperationException("Enemy rollback disposal failed: " + d.Path);
            _attemptedPointers.Remove(d.Guid);
        }
    }
}
