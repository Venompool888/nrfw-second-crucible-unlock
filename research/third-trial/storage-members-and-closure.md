# Storage闭包与可用interop成员

2026-10-10，仅metadata/原生字节/生成interop静态检查。当前源码尚在实现中。

## Resolve缺失行为与查找接口

`InstanceDataStorageSystem.Resolve(NpcDataId)` [44553]、Resolve(RuntimeNpcAttachmentsId)[44559]等在shared TryLookup失败后new `InstanceDataLookupException` 并throw，**不是返回default ref**。后者指令0x18598b20b→0x18598b222，throw0x18598b292。相同key已有其他pointer必须视为冲突，不可catch所有错误后Add。

可在各`storage.m_storageNpcData`/`m_storageControllerData`/`m_storageEntityData`/`m_storageEntityViewData`/`m_storageNpcSpawnerData`/`m_storageTeamData`/`m_storageInteractionControllerData`/`m_storageRuntimeNpcAttachments`/`m_storageTree`泛型Storage上用`TryLookup(long id,out int index)`，shared native0x184fa7600：缺失false/index0，命中true/index1-based。然后用typed Resolve/Get或Storage.Get(index)验证pointer；完整原生body没有GUID符号检查。生成out Int32 ABI与此前有问题的out QList wrapper不同。

| 类型 | 可用成员 | typed storage编号 |
|---|---|---|
| NpcDataRef | `m_index:Int32`、`IsDefault`、`Get(Frame)` | Add44385/Get44441/Resolve44553 |
| ControllerDataRef | 同上，Get返回ControllerData | Add44386/Get44442/Resolve44554 |
| InteractionControllerDataRef | 同上 | Add44387/Get44443/Resolve44555 |
| EntityViewDataRef | 同上 | Add44388/Get44444/Resolve44556 |
| EntityDataRef | 同上 | Add44389/Get44445/Resolve44557 |
| TeamDataRef | 同上 | Add44390/Get44446/Resolve44558 |
| RuntimeNpcAttachmentsRef | 同上 | Add44391/Get44447/Resolve44559 |
| NpcSpawnerDataRef | 同上 | Add44392/Get44448/Resolve44560 |
| TreeRef | 同上，Get返回Tree | Add44393/Get44449/Resolve44561 |
| 对应 XxxId | `InternalId:Int64`、`IsDefault`、`.ctor(data)`、`Resolve(Frame)` | ID仅表示key |

以上Ref没有IsValid，非default应检查`!ref.IsDefault`，并且不能在default上Get。NPCRef/ControllerRef/ViewRef等生成IL经InteropInspect只读PE核对，Get使用runtime_invoke且RaiseExceptionIfNecessary，因此native缺key/越界错误会透传为interop异常，不能把null判断当完整错误处理。

## SerializedNpcAttachments的完整闭包

共有8字段：Id:Int64；Controller:AssetRefControllerData；EntityData:AssetRefEntityData；NpcData:AssetRefNpcData；EntityView:AssetRefEntityViewData；InteractionController:AssetRefInteractionControllerData；TeamData:AssetRefTeamData；Spawner:AssetRefNpcSpawnerData。FromSerializedNpcBlock[41170]对全部7个非零ref分别storage.Resolve；无额外raw字段。raw.Id应等恢复NpcGuid；raw导出后对所有非零依赖先确认Frame/context资产类型与pointer，再typedStorageEnsure；FromSerialized后对所有refs.Get(Frame)pointer逐项读回。

这只能闭合附件本身，控制器在出生时另有Tree依赖。

## Critical：Controller必须有独立Tree

`ControllerData.GetTreeRef` [49102,RVA0x5b197d0] 取controller.Guid（native+0x18）tailcallstorage.Resolve(TreeId)[44561]。`AiControllerData.OnCreateEntityInstance`[48914,RVA0x5b08260] 0x185b0830a virtual调用GetTreeRef，0x185b0832d storage.Get(TreeRef)，按Tree.InstanceMemorySize分配帧内实例内存；未见自动创建Tree。

使用恢复`AiControllerData.CreateBehaviorTree(IAssetResolutionContext)`[48883,RVA0x5b02890]重新构建；context必须能解析动态private weapon及完整static动作。Tree公开字段是`OwnerGuid:AssetGuid`（native+0x38），不是Id/Guid。`storage.Add(Tree)`[44393]在0x185982d1c取OwnerGuid作为key。CreateBehaviorTree本体只new Tree(rootNode)并virtual OnTreeCreated，未写OwnerGuid；应显式`tree.OwnerGuid=controller.Guid`后Add，确认`controller.GetTreeRef(frame).Get(frame).Pointer==tree.Pointer`、OwnerGuid和InstanceMemorySize。以controllerGuid为key独立建树可反映已裁剪的privateweapon动作；不能重用原controller Guid的Tree缓存。模拟写入仍仅合法回调当前Frame，尚未执行上述接口。

## 私有EnemyWeaponData不进WeaponData storage

EnemyWeaponData Type8178→EnemyItemData8176→AssetObject；WeaponData Type9095→AssetObject，只有Class字段，两者不是继承关系。不存在EnemyWeaponData typedStorage，不应TryCast<WeaponData>或Add(WeaponData)。

HumanoidEnemyAiControllerData.CreateCombatActionSubtree[48444,RVA0x5ae1150]在0x185ae12a6与0x185ae12d9按主/副手GUID调用0x1807342a0；AssetRefEnemyWeaponData.Get[65784,RVA0x5f3bc10]在0x185f3bc56 tailjmp同thunk；HumanoidEnemyData.TryGetEnemyWeapon[48474,RVA0x5ae2570]从HumanoidEnemyComponent.MainWeapon读取GUID并在0x185ae2621调用同thunk。所以这里是IAssetResolutionContext AssetRef查询，非WeaponDataRef runtimeStorage查询。Frame.DynamicAssetDB注册+对应context Get同pointer验证满足这一链的前置；构建AI树时也要使用能见动态武器的Frame/context。WeaponCollider及动作仍保留static refs，依赖合法性不能由这项pointer检查替代。
