# 第三试炼恢复敌人候选

当前修复候选为R5，接续同一任务分支和9186fe3基线；后来正式main 7db4e4a尚未同步或合并。R4主线程视图注册成功后，进入世界在私有View GUID -611686018427387902发生foreign runtime cache collision，第三入口关闭；R4于18:11:27 UTC正常退出。

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
- Runtime storage：40个独立资源cache、必要静态Team/Interaction/Spawner依赖、十棵按新controllerGuid重建的行为树、十个按新npcGuid建立的默认RuntimeNpcAttachments。Add前全批检查，重复初始化同pointer读回，无覆盖已有key。native缓存没有支持的删除API；失败后保留未被第三路线使用的私有对象直到进程结束，封闭入口，避免dispose后悬空引用。
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
