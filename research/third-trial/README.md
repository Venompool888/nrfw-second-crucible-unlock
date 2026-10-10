# 第三试炼恢复敌人候选

当前已安装R6：R5实机第三献祭、路线和模型/战斗缓存已通过，刷怪在首回调因误把新NPC spawner当原始出生点而封闭。R6保持原8byte Spawn接口，通过Identity.Scope回源并交叉核对两份IntegrationGroup，再沿原白名单/房间归属改NPC参数；postfix有限记录原生保存的NPCref及返回entity，预测/确认帧分别记录，诊断不改变生成或控制。原生时序见 [R5刷怪来源证据](../../artifacts/third-launch/r5-empty-room-native-review.md)。最终真实源码刷怪回归50项通过（冻结R5同harness50项/37失败），其它Runtime87/Views101/ThirdRuntime423/storage154/entry33/presentation41和policy通过，7目标8callback及弩枪/JIT/unsafe/stub审计通过，Release0警告0错误。实际敌人、清层、奖励和重进须R6新会话用户验证。

R6代码f543a25e9096a84682804dc4189aa6a338bdc0d4，DLL SHA256 fa0295c5745a532b06b010fe219788a0587dc1f3813352038dfc9fdc375e3886，zip SHA256 29069ea097c1898c4a8623de0a1aaa38a3b3453ef3b5a55009234c86c1ec1f85。18:48:26 UTC备份校验并安装，18:48:57 UTC按已有授权Steam启动；记录/回滚位于artifacts/third-launch/installation-r6-20261009T184826534Z，实际新会话看artifacts/third-launch/log-review-r6.md。冻结包初次回归48项；后续两项纯诊断补充的50项最终记录另存artifacts/third-launch/r6-final-spawn-validation.json，生产源码/DLL未改变。

当前修复候选为R5，接续同一任务分支和9186fe3基线；后来正式main 7db4e4a尚未同步或合并。R4主线程视图注册成功后，进入世界在私有View GUID -611686018427387902发生foreign runtime cache collision，第三入口关闭；R4于18:11:27 UTC正常退出。

R5代码提交4e29d9aa6b22a6fb7f4a12cb82f0e707d4e1071a；候选DLL SHA256 e98b83fa2193bb5d67f06994de474a57547cc8e0545b3ed36611f23526489518，zip SHA256 34ab0869f1986cc9b610b80c7035e0448f26221021db07d29215349dd2f1ce78。18:28:10 UTC已安装，18:28:30 UTC依原授权Steam启动，PID16916；启动/helper/build guard通过，等待用户本人进入世界和第三献祭。备份及直接恢复试玩前0.9.17的rollback位于artifacts/third-launch/installation-r5-20261009T182810267Z，详细本次运行以artifacts/third-launch/log-review-r5.md为准。

R5让simulation层拥有唯一EntityViewData，完整bootstrap（资产→storage/树/NPC附件→playlist）成功后发布按manager pointer隔离的快照；Unity只创建原对象的wrapper，不能提前另造同GUID数据。当前Core.ResourceManager与Frame.Context.ResourceManager须同pointer，十项动态DB/storage/global wrapper须对应同对象。快照保留manager/view强引用，不缓存Frame/storage。入口READY和source remap、四来源pointer冲突均有有界诊断。静态默认建局链证据与未闭合的FindAsset优先级边界见 [R4缓存碰撞复核](../../artifacts/third-launch/r4-storage-cache-collision-review.md)。

实际源码离线回归Runtime87、Views101、ThirdRuntime423、storage154、entry33、presentation41通过，policy与第三/弩枪hook、JIT/unsafe/stub审计通过；冻结R4在同harness下先失败再验证R5。构建0警告0错误。测试不加载执行游戏DLL，也不替代用户第三献祭、刷怪及重进验证。若同manager的新session重新创建不同view pointer，严格identity保护仍会封闭第三入口；这一后续生命周期待实机验证。

本次用户已选择：第一试炼普通竞技场、第二试炼式逐房清场/转场/最终奖励、十个战斗房，每房一种恢复敌人。一个房间仍会沿官方出生点生成多只该类型敌人；不是一房只放一只。

基线：正式 main `9186fe3bf67da5a832e9ba37c17badf05f2adf3c`，CrucibleUnlock 0.9.16 + NRFWBowgunAudioSync 0.3.3。本任务新分支 `feature/third-trial-restored-enemies-20261010`。唯一未合并依赖是用户上一项敌人接线工作的 `15e1c17e4196b882da0da2ce04436963dd65b00c` 中恢复模块和bindings；这是实现本请求所必需的同一批敌人，不包括其他AI分支。

第三雕像的 quest step 3364572276970703445 只做本次进程内读覆盖，不调用Set/Complete/save。原第三未完成Horde source 5973112006018325 在选中第三入口时替换为私有DynamicExplicit playlist -611686018427287904。第一、第二、原第三playlist和director strategy不改。

私有playlist深拷贝第二路线：保留禁普通loot、允许治疗/经验、RespiteRoomRange=(5,5)、最终随机Reward(type4)，使用第三SpawnPoint 641656453459156268。普通战斗房使用Arena(type2)，source采用Roguelite(type1)，所以清场、敌人缩放、回声商店和结算走官方第一试炼普通怪规则。未把第二试炼专属Boss300痕迹、2000回声上限、Boss修复泛化到第三。数值和平衡后续再调。

| 战斗房 | 敌人配置 | 官方竞技场 | 出生点数 |
|---|---|---|---:|
| 1 | 长戟审判者 | ArenaA | 8 |
| 2 | 大剑审判者 | ArenaD1 | 12 |
| 3 | 巨锤审判者 | ArenaE1 | 13 |
| 4 | 大剑守卫 | ArenaE | 6 |
| 5 | 左剑盾守卫 | ArenaI | 11 |
| 6 | 右剑盾守卫 | ArenaJ | 12 |
| 7 | 弯刀死潮 | ArenaM1 | 13 |
| 8 | 大弯刀死潮 | ArenaP1 | 23 |
| 9 | 弩手死潮 | ArenaW | 11 |
| 10 | 巨斧死潮壮汉 | LargeWaterfallA | 12 |


## 实际接线

- 43个独立动态资产：十套NPC/Host/Controller/View，以及三件移除已知坏动作的私有敌人武器。模板和其他敌人的共享资产不改。View使用本地已保留prefab的GUID/名字，空cosmetic配置，战斗可用性仍待实机。
- Runtime storage：40个独立对象、50个类型缓存条目（十个controller同pointer分别注册ControllerData和EntityData）、必要静态Team/Interaction/Spawner依赖、十棵按新controllerGuid重建的行为树、十个按新npcGuid建立的默认RuntimeNpcAttachments。Add前全批检查，重复初始化同pointer读回，无覆盖已有key。native缓存没有支持的删除API；失败后保留未被第三路线使用的私有对象直到进程结束，封闭入口，避免dispose后悬空引用。
- 官方DynamicNpcSpawnerData.Spawn前缀只替ref AssetRefNpcData参数，保留位置/旋转/难度/巡逻/多人条件/miss和生命周期。scope检查私有source、实体所属chunk、原生integration ID→owner node映射、十房生成entry全Nuggets与121个GUID白名单；预加载按出生点所属房间选profile，预测与verified同规则。
- 各Arena常驻基础层和所有固定辅助Nugget均逐项离线复解，排除原NPC/额外Spawner/BossGate/DirectorZone/WaveSpawner；不是只根据名字推测。
- 第三盆克隆第一盆九轨时间线，重新绑定角色/两手VFX；血流以第三地面独立TRS和connector制作六张独立RGBA贴图，与第二资源/缓存隔离。
- 入口要求当前Frame资产、runtime缓存和**当前global Unity resolver**全部就绪；主线程泵先核对当前Core.AssetCtx身份，退休context不能宣告新世界ready。失败保留第三Step false和原Horde选择null门控；若入口hook本身无法安装，明确报模块不可用。
- 若部分怪已生成后再失败，私有第三source暂停原Roguelite.UpdateActiveRun，不能因只杀已生成怪而错误清房。死亡/离开/复活仍在独立原director，guard不改这些接口；它不额外创造Arena传送出口。

## 证据与复现

[rooms.json](rooms.json) 是嵌入生产manifest。[chosen-ten-arenas.json](chosen-ten-arenas.json) 保存基础层、辅助Nugget和每个出生点的QPURE来源、位置、标签、多人条件等。

`export_arena_manifest.py` 与 `select_ten_arenas.py` 只读项目内Quantum SQLite及QPURE导出，输出任务 `artifacts/first-trial-spawning/`。依赖项目既有 Python `../item-db/.venv/Scripts/python.exe`，不会启动游戏或载入执行游戏DLL。

存储及原生接口复核见[production-storage-review.md](production-storage-review.md)、[storage-members-and-closure.md](storage-members-and-closure.md)、[dynamic-spawn-plan-review.md](dynamic-spawn-plan-review.md)。原生反汇编完整产物留在任务artifacts，可用根项目元数据/PE工具再提取。

## 验证与交付边界

Release构建及各离线检查具体计数见任务 `artifacts/third-trial/` 和测试包 `verification.json`。实际AI动作、出生数量、视图、清层/奖励、死亡重进、多人仍由用户验证。用户已单独授权切换并启动本任务候选；不修改源存档或原游戏文件、不合并main、不发布。

候选开关 `[CrucibleUnlock] third_trial_restored_enemies = true`，与既有 `mode = "runtime-unlock"` 配合。只想暂时关闭第三改动可设false并重启；第二/弩枪正式功能仍保留。安装/切换必须游戏退出，先备份当前自研插件和配置，保持游戏原文件不动；由用户本人游玩测试。

## R2：初始化顺序修复（2026-10-10）

首次候选启动在官方内容初始化时失败：旧 StartOfFrameSystem.OnInit postfix 提前插入共享敌对 TeamData，随后官方 ExportRandomAssets 再次 Add 同一 GUID 370725232116713263，抛 duplicate key 并终止该会话。首包与失败日志保留为历史证据，不作为可游玩验收。

0.9.16-third-trial-candidate-r2 把第三完整 bootstrap 及独立恢复模块可选 simulation hook 都移到 InitializeContentSystem.OnInit postfix，等待官方随机池、活动等初始化返回后再注册私有资产。原生调用链与签名证据见 [init-order-review.md](init-order-review.md)。该回调本身不代表官方初始化成功，实际启动结果必须从新会话日志核对。

真实生产 Install 注册/派发回归先复现旧钩子的重复 key，再确认新钩子 official content exported=True、pooled=1。StartOfFrame 阶段私有资产数为0，官方初始化后为43；复用官方同一 Team pointer，重复 mod callback 不重复写入。实际 ThirdTrialRuntime 源码375项、实际 storage150项通过；另7组离线检查全通过，Release0警告0错误，8组第三 hook、unsafe interop及throwing stub审计通过。房间、敌人和生成规则与首包一致。

## R3：献祭视图 JIT 兼容候选（2026-10-10）

R2启动时十棵树与SIMULATION READY成功，随后用户确认自动闪退。Windows记录CoreCLR 0xc0000005/80131506；34MB转储只读归档在 `artifacts/third-launch/r2-crash-evidence/`。项目SDK同版本6.0.36的DAC/SOS读取转储，未执行游戏DLL；异常线程正首次JIT `RitualViewRepair.AfterGetActionView`。微软匹配PDB将故障定位到 `ClassLoader::PropagateCovariantReturnMethodImplSlots`（官方v6.0.36 class.cpp1290），父虚方法slot21反查MethodDesc为空。转储未保留故障类型，不能宣称唯一根因。

最贴合的静态候选是Il2CppArrayBase<T>.GetEnumerator：它带PreserveBaseOverrides并协变覆盖抽象父方法，旧回调两处组件数组foreach直接引用它。R3把组件数组、timeline记录和resolver列表的四处interop foreach改为索引遍历。保持实际接线、九轨检查、对象归属与失败回滚；托管List<GameObject>清理遍历保留。

Harmony callback缩为71 IL bytes，无局部变量，只筛选缺失Offering并调用独立NoInlining helper；Install在Patch前用RuntimeHelpers.PrepareMethod编译完整helper，日志区分preparing与compilation ready，避免把原来首编译崩溃简单推迟到献祭。它不执行helper本体，也不保证所有callee已编译，实际献祭仍须用户测试。

新增 [RitualViewJitAudit](../../tools/RitualViewJitAudit/README.md) 只读PE审计：R2 RED17项失败，R3 GREEN0失败；确认array GetEnumerator和native List Enumerator引用均0，PrepareMethod先于Patch，签名及ref结果不变。实际Runtime375项、献祭/血流41项、已有policy与8组第三/15组弩枪hook审计通过，Release0警告0错误，unsafe/stub0失败。R2日志没有VIEWS READY或完整世界初始化证据；R3实际启动与献祭状态另见本轮安装/日志记录，不以离线通过代替实机结果。

## R4：第三盆视图准备活性修复（2026-10-10）

R3已成功运行第二盆献祭，但第三入口仍不可用。17:49:07 UTC日志目标明确为secondStatueBowlInteraction，第二血流/九轨播放成功；没有第三目标、VIEWS READY或第三刷怪。会话17:50:24 UTC正常退出，无新CoreCLR崩溃。

原生核验确认官方UpdateAsyncQuantumViewAssetLoading.Tick直接调用UpdatePendingLoads/InvokePendingCallbacks，绕过被挂接的RuntimeAssetResolutionContext.Update。R4删除该无活性钩子，由已有ModMain.OnUpdate在Unity主线程每帧调用PumpCurrentViews，每次重新读取官方Core.AssetCtx；先核对当前身份再注册，resolver重建立即撤销ready，十项global wrapper lookup全部通过才发布ready。等待ctx/LUT/global resolver分阶段有界日志，停用与退出后不读Unity。没有缓存Frame、放松入口或改变刷怪条件。

官方QdbTask路径可以不写InitializeQuantumAssetDatabase.GeneratedLut。PoolingSystem.LoadPoolingData实际调用ViewFrame.LoadAssetWrapper<WorldLUTAsset>，精确GUID8265928748392077239（native MethodSpec673909/11610/13086）；R4在静态LUT空时只读当前ctx已加载的该wrapper，要求WorldLUTAsset类型及AssetGuid匹配，再沿用十个prefab GUID/DebugNames严格检查。未加载时重试，不主动加载或写官方LUT/池。

新增真实EnemyRestorationRuntime编排测试：冻结R3 RED缺逐帧入口、Ensure调用0；R4 GREEN覆盖迟到LUT、当前ctx丢失/交换、十项回读、全局lookup失败及shutdown。真实Views测试冻结R3复现cached LUT无法恢复，R4通过52项，含无静态写入、严格GUID/名字校验、源pointer保留和失败回滚。PE审计新增ModMain.OnUpdate→PumpCurrentViews→Core.AssetCtx调用证据，第三hook现为7组；旧R3 JIT隔离继续保留。完整离线结果和最终DLL哈希见R4包verification.json；实际第三献祭及十房仍须用户游玩验证。

R4代码提交7b7581f，DLL f423265a2f1f3c84245841a2ec444c16a67e890da0ce6ee7176a4108608bf502。本轮按已有明确授权备份、切换并于18:04:59 UTC通过Steam启动，PID49060；18:05:18.9917844 UTC实际VIEWS READY profiles=10，已证明主线程泵与官方缓存路径完成注册和global回读。第三献祭、刷怪及十房验收仍待用户。安装/回滚路径见artifacts/third-launch/current-installation.json，回滚保留试玩前原0.9.17-player-defaults和完整配置；原游戏文件、三个独立插件未改。正式main已另行更新至7db4e4a，本候选继续任务原基线9186fe3，未同步或合并新主线。

## R5–R7：献祭通过后空房间调试（2026-10-10）

R5修复Unity/simulation重复EntityViewData后，实际10TREE、完整simulation/storage、同manager Unity视图、第三入口和第三盆献祭均通过。首个刷怪被错误的组件检查阻止：Spawn中的entity是新NPC spawner，DynamicSpawnerComponent实际在Identity.Scope指向的原始出生点。R6沿Scope交叉核对source/new IntegrationGroup，保留原房间、白名单和readiness；离线50项通过，但真实18:50:37 UTCscope读报NullReferenceException，无REPLACED/RESULT。异常捕获没有stack，实际失败行未知；18:51:26 UTC正常退出。见 [R6空房间日志](../../artifacts/third-launch/r6-empty-room-log-review.md)。

R7将四种组件读取换成官方EntityRefExtensions.Get指针路径，仅在当前Frame callback内读取已验证layout的值字段，无boxed组件wrapper或跨Frame指针缓存。官方AOT实例、unboxed布局及caller访问证据见 [原生组件读取审查](../../artifacts/third-launch/r6-native-component-get-review.md)。Frame.Get缺少显式AOT实例本身不能证明R6根因，因为存在fully-shared generic；R7同时加入phase、entity和完整exception诊断，让下一次实机失败能定位到具体调用。

真实刷怪源码harness改为原生布局的raw buffer，并拒绝旧boxed泛型读取；冻结R6先RED、R7再GREEN。新PE审计检查四组件官方pointer调用、禁止Frame.Get与raw-pointer class wrapper。具体最终计数、commit和DLL hash见R7冻结包verification.json。R7是下一次用户进房测试候选，实际原生RESULT、可见模型、战斗和十房流程尚未验收。继续原任务分支，正式main7db4e4a未同步或合并。

R7 source `44d7ef746d9bf1b96b59f44dac1d2a1ba1d5fc4c`，DLL `55268f38272f5d575b823fb2d0ec96601244f783cb86a5d2a2ae655e5cf5d5de`，zip `0fd6783f6016196ae0d6a1fba97fa36e768a933b26d5cad36561e961d275c282`（1524746bytes）。最终13组离线验证通过、Release0warning0error，raw spawning62/0、冻结R6同harness62/38。19:03:58 UTC备份并切换，19:04:33 UTC按已有授权Steam启动，PID25208。完整备份与原始0.9.17回滚见 `artifacts/third-launch/installation-r7-20261009T190358577Z/`；新会话证据见 [R7日志](../../artifacts/third-launch/log-review-r7.md)。不把启动或离线通过作为实际刷怪验收。

## R8：QListPtr空值比较的实机根因修复

R7实际19:05:44 UTC第三盆成功，19:05:57 UTC进入generated-entry后NRE：Il2CppObjectBaseToPtrNotNull→QListPtr.op_Equality→GeneratedEntryMatches。原生生成wrapper要求两个operand均为非空对象，`pointer == null`的第二参数直接违背它；这一次完整stack与生成IL相互印证。R8仅把它改为ReferenceEquals(pointer,null)，保留Ptr及所有entry/ownership/readiness守卫。实际生产PE已无此opEquality，见 [空值比较审查](../../artifacts/third-launch/r8-qlistptr-null-review.md)。

冻结R7相同harness RED66/33，R8 GREEN66/0；生产source `2a557584daaa2f88d0b3937afdc95b96004ca440`，DLL `fbbac8cc70de6f1e2d93dbc0d050d3aaf957f3ef9e11c49c5deabc1145ce7c69`。Release0warning0error及五组相关检查通过，未改模块引用R7验证，不计作本次新跑结果。19:09:27 UTC备份并切换，19:09:50 UTC按已有授权Steam启动，PID51848；原0.9.17rollback保留于 `artifacts/third-launch/installation-r8-20261009T190927115Z/`。新日志 [R8复核](../../artifacts/third-launch/log-review-r8.md) 决定首房原生RESULT，用户本人确认可见敌人及战斗后才有可玩证据；十房/奖励/重进另验，任务分支未合main。

## R9：控制器基类缓存与合法敌人视图根

R8实际REPLACED verified=True，但没有RESULT；原生Spawn报EntityData/-611686018427387903缺失，此GUID是控制器。官方ExportRandomAssets给ControllerData同对象分别Add(EntityData)与Add(ControllerData)，Add重载不会自动级联。R9新增同pointer基类slot预检/Ensure/readiness，与真实原生调用链闭合。冻结R8源码在真实类型继承/typed Add harness复现同GUID缺失，R9 storage226/0、实际runtime423/0。见 [原生缓存证据](../../artifacts/third-launch/r8-controller-entity-cache-review.md)。

R8 Player还累计2465次missing EntityView，随后too many exceptions终止，19:11:09 UTC WER同PID51848 GameAssembly c0000409，非正常quit。离线Unity结构确认七个误选ViewVariant是武器，左右shield是任务NPC GenericNpcEntityView；十项binding现指向原view_template对应的五类合法HumanoidEnemyView根。Views只复制官方wrapper的PrefabRef/ViewConfigs/ViewConfigsRef，保持canonical simulation data的pointer/Guid/Path与CharacterType/EnemyFaction配置；实际nonGeneric GetComponent检查root有EntityView/HumanoidEnemyView。公开非泛型TryFindWrapperImpl避免新增closed generic AOT依赖。独立资源加载ref用LoadOpCreate→Done→Result，不调用实例化/激活的LoadOpResult；每ctx/pool最多五根保留到process结束，重进/context累积尚需实测。见 [视图结构与接线](../../artifacts/third-launch/r9-view-root-review.md) 和 [独立审查](../../artifacts/third-launch/r9-storage-pool-independent-review.md)。

R9 source `a886af5edbb3254cae17ba69ef66bbeaf1a04dd2`，DLL `a5cd6b761978b4f09b5645107c4798f661fca80e645eb0fa3d9e68421239d796`，zip `4acd2fd3c40bf09d2a237853b7cfda2747dfa7c5b02f410820e5ed1a923b44d3`（1527220bytes）。最终17组离线检查全通过、Release0warning0error；Views127、storage226、Spawn66、runtime87、thirdRuntime423、entry33、presentation41、catalog53及policy/thirdABI/bowgun/JIT/unsafe/四dependency stubs通过。19:24:15 UTC备份切换，19:24:40 UTC按已有授权Steam启动，PID15040。完整备份与试玩前原0.9.17回滚为 `artifacts/third-launch/installation-r9-20261009T192415973Z/`；新会话 [R9日志](../../artifacts/third-launch/log-review-r9.md) 核对RESULT与异常。实际外观/武器/动作、十房/奖励/死亡重进由用户验证，不合main。

R9启动观察截至19:27:03 UTC：本次banner/hash/buildguard/hooks及helper compilation ready通过，PID仍Responding。Latest停于19:24:55 UTC等待simulation publication；Player最后19:25:05 UTC IntroSequence，尚无world/TREE/SIM/PUBLISH/VIEWS/entry/spawn记录。未观察到明确失败，但未触发不等于初始化或刷怪验收。当前安装状态为等待用户进入世界和第三首房；回滚配置hash已核对实际启动后prefs文件更新。

## R9 后续游玩与动作表现缺口

用户已到显示第十层，报告整体正常、部分敌人动作不全。本次同一R9日志04:05 UTC进入世界、04:27:58 UTC正常退出，十棵树及视图、第三入口/献祭就绪；前九种类型有REPLACED/RESULT且全部verified=True，日志为有界诊断而非总实例计数。休息房每五战斗房插入，显示楼层不等于战斗类型序号；第十战斗类型、奖励及死亡重进仍待验证。

完整 [游玩日志复核](../../artifacts/third-launch/r9-ten-floor-evidence/report.md) 保存原日志/hash/逐项计数：50次MissingActionView/17个动作，19次装备wrapper回调NRE、4次Quantum View加载失败、6次未登记武器prefab错误。大剑审判者与弩手动作已有出货ActionView，但私有模拟武器缺Unity EnemyWeaponDataAsset wrapper；大剑守卫的官方weapon wrapper另引用未在WorldLUT登记的PrefabRef。R10继续在本任务分支修复接线，不以QDB记录存在或空AnimatorController证明动画出货，不使用角色根充当武器。未合main。

## R10：武器与招架视图接线

为三个已裁掉不可用攻击的私有模拟武器补Unity EnemyWeaponDataAsset wrapper，保留exact canonical pointer、Guid/Path和裁后moveset；复制对应原武器PrefabRef，通过非泛型官方加载和实际EnemyWeaponView根验证。七个未修改的官方武器不纳入此桥，原prefab WeaponData保持原GUID供碰撞配置读取。十character+三private weapon wrapper全部读回同模拟pointer才继续ready。冻结真实R9 Runtime源码为RED对照，新Runtime额外门控守卫与招架模板；同一context的asset identity改变、manager/context竞态及关闭后Unity访问均有回归。

守卫原weapon GUID4494428972466418711的PrefabRef不在WorldLUT，但QDB bundle保留真正 `enemyKeeperGreatSwordViewVariant` / EnemyWeaponView以及七个攻击子ActionView。桥从七个官方MeleeAttackDataAsset.View回溯到同一真实武器根；只给第三私有keeper NPC用官方PoolingSystem.CloneSync和HumanoidEnemyView.OnItemViewInstantiated装备。原actor `humanoidEnemyKeeperGreatSwordViewVariant` 是角色根，不可当武器。未修改全局pool索引/bitset/queues。装备原生callback中途失败时核对MainWeaponView实际ownership，未接管副本用官方Unequip取消自己的ActionView，再清理；已交原生持有的副本沿官方生命周期。

左右剑盾守卫与亡潮大弯刀的ParriedHitReaction指向同一动作2122162585542657617，原角色/武器映射无该视图。资源中有官方parriedHitReact clip与13轨TimelineActionView子树；只在原GetActionView结果为空时复制这个子树，独立复制Animator/Sound引用并重绑当前CharacterAnimator、MainWeaponView.transform与WeaponSoundHost。Rig定义须与角色同pointer；所有timeline item须在克隆层级。走官方OnInstantiatedFromPool/注册，回池前或OnDestroy取消注册/OnReturned/deactivate/Destroy，每步异常独立清理；源子树与source resolver不改。

Unity侧范围由当前PredictedFrame/EntityRef的原生NpcAttachmentComponent证明，组件unboxed144bytes、EntityViewDataRef索引+16/NpcDataRef索引+24均int32。官方EntityRefExtensions.Get AOT599544，storage索引1based；双私有GUID、lookup索引、DynamicAssetDB同pointer和NPC Host/Controller/View/Team引用必须全部匹配，不使用prefab QuantumAsset推断当前敌人。四个短NoInlining回调、隔离helper启动前PrepareMethod、全部interop索引遍历；同一主模块Harmony事务安装，context reset/停止无Unity读取撤回template。

独立复核见 [私有武器桥](../../artifacts/third-launch/r10-weapon-bridge-review.md) 与 [owner/守卫/招架](../../artifacts/third-launch/r10-owner-keeper-reaction-review.md)。构建、离线结果和安装状态以R10冻结verification/current-installation为准；准备/接线日志有界记录，不能代替用户确认可见动作。四条Quantum View加载失败尚未全部证实因果；显示第十层、实际第十战斗类型、最终奖励及死亡重进应分别记录。任务未合main。

R10源码 `10cc144ddac6ca4c7b4dcb4c628032c0b73e5b45`，DLL `66de7a46e5243e69598b53235a19f539f36bb5a2afdac5456d4ae430102290ad`，zip `c4e7f87c5645cd7d049023c694695463fe43cbdf47fc5fd8b0fea289fdd7f5a6`（1539151bytes）。05:09:53 UTC备份切换，05:10:08 UTC按本任务已有授权启动；05:10:21 UTC PID12736 CoreCLR 80131506/c0000005自动退出，第三路线没有触发。离线25组通过不构成实机成功，R10冻结包与失败证据保留。

精确转储SHA256 `9ea4b9e4c116b187b12189b0d2c1c3c733c07c515ef63034bd33933113700b54` 的SOS调用栈为RuntimeHelpers.PrepareMethod→RitualViewRepair.Install。Ritual四方法与R9 canonical IL一致；故障在献祭helper预编译，不能认定新增动作钩子是根因。ActionHooks在更早的EnemyRestorationRuntime.Install内已返回，TrialRepairLog等OnUpdate才输出，日志缺行不说明它未执行。CoreCLR故障函数与R2相同，但转储未捕获具体MethodTable；两处闭合Il2CppArrayBase类型是静态候选，不能称唯一根因。详见 [R10新会话](../../artifacts/third-launch/log-review-r10.md)、[转储复核](../../artifacts/third-launch/r10-crash-evidence/native-jit-review.md) 和 [R9/R10对照](../../artifacts/third-launch/r10-startup-jit-review.md)。

## R11：献祭模板发现不导入原生数组

R11只替换献祭修复的组件发现路径与启动诊断，六份敌人武器/招架/owner/runtime接线源码与R10哈希相同。发现代码独立为partial [RitualViewRepair.Template.cs](../../src/CrucibleUnlock/RitualViewRepair.Template.cs)，用托管Stack与Transform.childCount/GetChild遍历根及inactive后代，通过非泛型GetComponent(Type)取目标组件；须满足已核出货盆层级每GO每目标类型至多一个组件的前提，不泛称任意场景的全组件枚举等价。嵌套第一盆仍重复计数，单模板/名字/GUID/bound/同场景策略保持；4096节点上限及组件异常均拒绝。克隆InteractionView查找也改为非泛型。

FindOfferingTemplate、CopyResolver和完整RepairOffering均在Patch前PrepareMethod，三段启动日志直接由loader线程输出。保留预编译以避免把同类风险推迟至献祭；ModMain此前先flush已有诊断。所用非泛型GetComponent与Transform调用经生成PE元数据确认有真实runtime_invoke实现，未执行游戏DLL。

Release0warning0error，DLL `7d2f63b7c1d8dc4f30a43791eef6e26e9a1c9a5a7512a3679c3d8b5c19ca0db6`；本版新跑13组通过：实际发现源码15项、policy含献祭/血流范围、Ritual PE、第三12回调/11原生target、unsafe及8组dependency stub。扩展Ritual PE检查冻结R10 RED8原生数组引用，R11 GREEN0；回调71ILbytes、0原生枚举器。未改模块的R10验证以源hash校验后引用，不计作本次新跑。离线结果不证明实机JIT或动画已修复；安装及新会话结果以current-installation为准。任务未合main。
