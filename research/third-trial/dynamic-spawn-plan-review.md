# 精确方案补充审查：十个不同 Arena、Roguelite、动态 NPC 参数替换

## R7补充：组件指针与异常阶段

R6第三入口和献祭通过，但首次scope检查NullReferenceException，旧捕获没有stack，失败对象未知。官方EntityRefExtensions.Get有Identity/IntegrationGroup/DynamicSpawner/DynamicNpcSpawner专用AOT实例并返回原始ECS组件指针，R7在Has和layout检查后于当前callback即时读取所需blittable值，不构造boxed wrapper、不缓存pointer。仍保留下文全部source、owner、manifest和entry守卫，仅改NPCref；失败加入phase/source/new/full exception。Frame.Get缺少显式MethodSpec不能单独证明shared-generic失败，playlist entry boxed投影也保留为未知边界。具体原生证据见 [组件读取复核](../../artifacts/third-launch/r6-native-component-get-review.md)。实际Spawn源码raw-buffer测试62/0、冻结R6同harness62/38；R7是否消除真实NRE须新会话和用户游玩确认。

## R6更正：Spawn参数中的新实体与原始出生点

R5实机第三献祭和路线已通，但刷怪回调把Spawn的EntityRef参数误当原始出生点，检查错误的DynamicSpawnerComponent，因而封闭NPC生成。原生完整时序见 [R5原生来源审查](../../artifacts/third-launch/r5-empty-room-native-review.md)。该参数实际是InstantiationAPI.Create刚创建的新NPC spawner，已有DynamicNpcSpawnerComponent、IdentityComponent与复制的IntegrationGroupComponent；其Identity.Scope才指向原始出生点实体。出生点GUID须读取该source的DynamicSpawnerComponent.DynamicSpawnerData，不能读新实体稍后才填的SpawnerData，不能把DynamicNpcSpawnerData资产Guid当出生点Guid。

R6保留原8byte Spawn hook，沿Identity.Scope回源，交叉核对新/source的IntegrationId与owner节点Id/Entity，再应用原chunk/bucket/nugget、integration map、生成entry及出生点白名单。仅改NpcDataRef，官方位置/旋转/难度/生命周期保持。下文旧建议中的“spawner”一词应区分这两个实体；原直接在Spawn参数上要求generic DynamicSpawnerComponent的理解已经由原生证据纠正。修复后实际原生返回和房内敌人仍须新会话用户测试。

2026-10-10。用户确认每房一种恢复怪，十个普通 Arena 战斗房，沿用第二试炼的逐房/奖励设置，第三入口。这个方案取代前文 BossC 直接挂普通内容的候选：不同普通 Arena + 原生 Roguelite 清层/scaling 避免了重复 BossC bucket切换和当前层绑定缺失两项风险。该结论仍要求最终选出的十个 chunk Guid 在生成后的实际 playlist 中唯一，包含的 ordinary Nugget 和场景位置相符。

## DynamicNpcSpawnerData.Spawn 的实际 lookup

原生 method45140，RVA0x59b2780，完整证据见 native.txt。

| 顺序 | 指令地址 | 实际行为 |
|---|---|---|
| 1 | 0x1859b289b | IsSpawned(frame,spawner)，已有实体时返回现有实体，不生成第二次 |
| 2 | 0x1859b28b5 | AssetRefNpcData.Get(frame)，读取替换后的参数资产 |
| 3 | 0x1859b28e3..0x1859b28ea | 取该NPC.Guid（native+0x18）作为RuntimeNpcAttachmentsId，InstanceDataStorageSystem.Resolve[44559] |
| 4 | 0x1859b2904 | storage.Get(RuntimeNpcAttachmentsRef)[44447]，得到以NpcGuid为Id的default附件 |
| 5 | 0x1859b2915..0x1859b29d6 | 将默认附件各runtime refs复制到调用local组件 |
| 6 | 0x1859b29e9 | 按输入NPC参数GUID重新Resolve(NpcDataId)[44553]，写local NpcDataRef |
| 7 | 0x1859b2a6f..0x1859b2a78 | 使用原生参数position与rotation；随后FindFreeSpot修正 |
| 8 | 0x1859b2ca3 / 0x1859b2cda | 使用输入patrolRouteId或原生nearest patrol fallback |
| 9 | 0x1859b2d51 | SpawningAPI.SpawnEntity，difficulty来自原输入 |
| 10 | 0x1859b2db6..0x1859b2dc9 | DynamicNpcSpawnerComponent记录替换后的NPCref/GUID与已出生状态 |
| 11 | 0x1859b2eee / 0x1859b2f00 | 原生OnNpcSpawned signal及NpcSpawned event |

这里**不查NpcInstanceData.PureId缓存**，不需要新建instance-key的NpcBlock。每种恢复怪需要一个**Id=恢复NpcGuid**的default RuntimeNpcAttachments。保持官方生成调用、仅替8byteAssetRefNpcData可避开修改原content/NpcInstance共享对象的问题；重进/respawn也会读组件里已存的动态ref。

## Storage注册静态门控

强类型 `InstanceDataStorageSystem.Add(NpcData)` [44385,RVA0x5982950] / `Add(RuntimeNpcAttachments)` [44391,RVA0x5982bf0] 只检查对象与storage非空，读取对象Guid或Id，tailcall共享 `Storage<T>.Add` thunk **0x184fa74d0**。

共享Add完整body已补入native.txt末尾：0x184fa7559把64bitId原样传给Dictionary插入，behavior=2；随后把native对象引用追加List并返回1-basedindex。无正GUID、typebits、AssetGuidType、exact nativeclass检查。负DynamicExplicit Guid不会因此拒绝；派生NpcHumanoid类可经NpcData类型存储。**重复key使用抛异常插入行为，不能把Add当幂等ensure。** 也不能因相同key或path就认为对象归本mod所有；检查same native pointer。

推荐初始化：

1. Frame.DynamicAssetDB已经注册所有恢复资产，逐个验证Guid/path及NPC→host/controller/view关系。
2. 在当前frame.InstanceDataStorageSystem逐个Add或验证现存同pointer的NPC、host、controller、view，以及它们引用的必要static类型。默认block.ExportNpcBlock所引用Spawner、Team、InteractionController等非零ref也须能在storage.Resolve；原生Export只导出refs，不能假设所有static依赖均已加载。后续核验纠正：private EnemyWeaponData不是WeaponData，不加WeaponData storage，仅验证其AssetRef.Get(Frame/context)可见；控制器须独立CreateBehaviorTree(Frame/context)，设置Tree.OwnerGuid=controller.Guid后Add(Tree)，见 storage-members-and-closure.md。
3. 对恢复NPC调用其实际virtual `ExportNpcBlock(frame,new SerializedNpcAttachments)`，验证raw.Id/NpcData均是恢复NpcGuid，EntityData/Controller/View为恢复独立资产。随后 `RuntimeNpcAttachments.FromSerializedNpcBlock(frame,raw)`[41170]，验证所有非零refs.Get(frame)对应准备对象，最后storage.Add(defaultBlock)。FromSerialized只查询refs不添加。
4. 按storage.Pointer建立一次性owned-ready标识，不按Frame.Pointer反复Add；frame copy/预测可能共享同一InstanceDataStorageSystem，而Frame可更换。重复ensure查lookup+pointer；发生冲突/failure不得继续宣称ready。

## 推荐入口与spawn门控

- 在开放第三入口之前完成build guard、十种Frame资产、当前storage default块及当前Unity context的十种view readiness；任一未就绪，不开放一个会生成普通原怪后被IsSpawned永久锁定的第三run。
- 每次native spawn prefix重新确认sourceplaylist是mod第三GUID、spawner存在且有DynamicNpcSpawnerComponent、containingnode匹配第三runtimeplaylist中的**combat entry**，且entry→恢复profile为固定映射。可以预加载其他combatentry，映射必须从该spawner所属chunk确定，不能从仅当前floor确定。
- 休息房/RewardA/入口atrium及第一/第二试炼调用原样通过；未知第三node/未知ordinarygroup记录失败，不能泛化到所有第三NPC或Boss/minion。若ordinary content具有其他生成类型，必须离线证明它们覆盖在DynamicNpcSpawnerData.Spawn链，不能用这一hook宣称覆盖所有普通怪。
- 要改的NPCref为native8byte值类型；验证prefix对该参数按值替换的Harmony/Il2CppDetour ABI，返回EntityRef及其他七参数保持不变。独立静态interop证据只证明生成wrapper使用native8byte值参数，不证明Harmony写回运行成功。
- 不用自建逐帧spawn循环。原生IsSpawned及streaming unload/毁灭负责重复生成与清理；保证无挂到两个不同内容nugget的同一spawner名单、无跨combatentry复用chunk。
- 不以frame.IsVerified跳过所有预测frame的NPC参数替换，否则预测画面/实体会继续原怪并与verified不同。对预测frame读取本次callbackframe及共享已就绪immutablestorage；禁止在Unity Update拿cachedFrame插入模拟实体。

本文件是静态接口支持与需实现的门控，尚未观察production storage桥或prefix最终源码，尚未实机验证。
