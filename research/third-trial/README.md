# 第三试炼恢复敌人候选

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
- 入口要求当前Frame资产、runtime缓存和**当前global Unity resolver**全部就绪；旧context Update不能宣告新世界ready。失败保留第三Step false和原Horde选择null门控；若入口hook本身无法安装，明确报模块不可用。
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
