# 第三试炼 R17：武器音效、外观与巨锤火焰的离线审计

日期：2026-10-11。范围：安装 R15 对应的 Steam build 22928553 出货资源与 R15 游戏日志。全过程未启动或操作游戏，未执行游戏 DLL，未改游戏本体、存档、配置或日志。本文的“存在”指静态资源及引用闭包，不能替代听觉和画面实测。

## 结论与处理

| 房间 | 用户观察 | 离线结论 | 本轮处理 |
|---|---|---|---|
| 4 大剑守卫 | 外观、武器声正常 | 官方 `enemyKeeperGreatSwordViewVariant` 有 104 个 `WwiseSoundAnimator` 和 73 个 `WwiseWeaponAnimator`，可作资源链参照。 | 保持不动。 |
| 5、6 剑盾守卫 | 人物声正常、武器声缺失 | 官方 `enemyKeeperSwordView` 的 11 个 action 均无上述两种武器声音轨；武器根也无该两种组件和 `WwiseWeaponTimelineBinder`。 | 未修。没有逐动作匹配的现成官方声音轨，需制作并校准动作时序。 |
| 7 标签弯刀死潮 | 实际拿斧头且有斧声 | R15 绑定的官方 `enemyDeadTideCurvedSwordData`（157205877300334101）包装器指向 `enemyTanthOutriderVariantView`，根 `WeaponData.Id=2230631566238253937`，14 个 action view 均为 `enemyAxe*`（7 个不同动作 GUID）。这解释了斧外观和斧声；正确弯刀武器数据是 `deadTideCurvedSwordData`（4091733801493987324）及 `deadTideCurvedSwordViewVariant`。 | R17 任务分支已修正第 7 房 weapon binding，并核验闭包和离线门控；此审计未修改公共 JSON/catalog。 |
| 8 大弯刀死潮 | 双手外观正常、武器声缺失 | 官方 `deadTideGreatCurvedSwordViewVariant` 的 14 个 action 均无武器声音轨；根也无两种武器声组件和 Binder。 | 未修；需要逐动作时序。 |
| 10 巨斧死潮 | 武器声缺失 | 官方 `deadTideBruiserGreatAxeAViewVariant` 有 182 条 `WwiseSoundAnimator`，引用 40 个独立事件。40/40 HIRC Event、47/47 Action 及 44 个直接 target 出货；进一步遍历 626 个 hierarchy object、445 个 Sound object，媒体均有实际 DIDX/DATA 或 stream 字节。**资源缺失不是已证根因；运行时播放、host/时间轴连接尚未知。** | 未改声音轨或强制触发。当前可用日志没有第 10 房 spawn/equipment 事件。 |
| 3 巨锤审判者 | 持武器似长矛冲刺，预期喷火而无火焰 | 官方动作库有独立 `Flame Thrower` 与 `Thrust`；`Thrust` 的配置含 100% `ThrustAttackFollowUp`，该 FollowUp 的 Unity timeline **配置了**一个生成 `inquisitorFireDamageOverTimeCascadeData` 的事件。Cascade 的 ViewPrefabRef 命中 WorldLUT 2277 `inquisitorFireZoneCascadeView`；该出货 prefab 有 7 个 `ParticleSystem`、7 个粒子渲染器、2 个网格渲染器和 1 盏灯。**尚未证实游戏中 FollowUp 执行、生成事件发出或粒子显示。**独立 `Flame Thrower` 本身不是所见冲刺的当然后续。 | 未添加虚构火焰或改官方动作。需定位实机所见动作究竟是 Thrust/FollowUp 还是另一冲刺，并获得事件执行证据。 |

对第 5/6/8 房，`WwiseWeaponCompanion.AttackEvent` 的序列化 media GUID 为零，但这**不能单独解释无声**：用户认为正常的第 2 房对应字段也为零，其官方 prefab 却有 200 个 `WwiseSoundAnimator`、73 个 `WwiseWeaponAnimator`。反之仅给私有 clone 赋一个 donor `AttackEvent`，不能使缺少时间轴调用的每次挥击自动发声；`OnWeaponStrikeConnected` 是命中路径而非完整挥击时序。本轮没有采用这种无证据补丁。

## 同动作 donor 比对

在十房 R15 官方 action view 中逐一比较了第 5/6 房 11 个、第 8 房 14 个 action 的 `ForsakenAnimationPlayer.Animation` 引用、速度、动作组件结构及已有声音轨。第 5/6 房相同动作互相共用 clip，但两者均没有声音轨。剑盾的 `enemySwordKeeperDualDashData` 与第 4 房 `enemyKeeperGreatSwordDualDashData` 共用一个非空 animation 引用，前者速度 1.15 且含 3 个 AnimationPlayer，后者速度 1.0 且含 1 个；ActionSection/嵌套 timeline 结构也不同。

第 8 房的 A、AFast、B、C 与大剑 `enemyGreatSwordUnlockableAttackB` 仅共用部分 animation 引用，后者含 6 个 AnimationPlayer、5 个 WeaponColliderAnimator 和 6 个 QuantumTimeScaleAnimator，而弯刀 A 仅分别含 2、1、1 个；Spin 与大剑 `enemyGreatSwordSwipeFollowUp` 共用一个 clip，但其他动作轨结构不同；其余弯刀 action 未找到有声的同 clip donor。因此不存在已证明同时匹配完整动画序列、动作时长和 hitbox/weapon 轨时点的官方声音轨组合。复制第 4 房整套动画或在攻击/命中时泛化 `Post` 会改变时序并可能重复或错位，不能视为修复。

## 日志证据边界

日志分析由专责代理读取并复核：R15 10:20–10:41Z 会话实际只到第 9 房。第 5、6、8 房分别有 `[enemy-audio] equipment` 登记，但均带 `presence-only=true`；它只证明设备对象/字段被读到，不能证明 Wwise `Post` 发出或人耳听见。该会话没有第 10 房设备行。用户完成十房的另一会话缺完整对应 Melon 日志，故第 10 房目前只有用户的听感反馈。第 3 房是否触发 FollowUp 及 fire cascade 亦无可用运行时事件证据。

## 可复核工件

- `artifacts/third-launch/r17-repair/weapon-fx/audit_source.py`、`source-audit.json`：R15 基线绑定、官方 prefab 组件和 action 声轨计数、火焰 Cascade 数据链。
- `audit_donor_matches.py`、`donor-match-audit.json`：十房官方动画引用与候选 donor；空指针已排除。匹配 clip 仅是筛选条件，不等于相同时序。
- `audit_fire_view.py`、`fire-view-audit.json`：只读遍历出货火焰级联 prefab 的可视组件。
- `audit_soundbanks.py`、`soundbank-event-audit.json`，`audit_hirc_closure.py`、`hirc-closure-audit.json`：第 10 房事件 GUID、HIRC Event→Action→直接 target 与 metadata media ID 对实际出货包 DIDX 的核对。
- `artifacts/third-launch/r17-repair/voice/audit_weapon_event_closure.py`、`weapon-event-closure.json`：独立代理更深层的第 10 房 HIRC hierarchy→Sound→decoder media 闭包核对。

后续第 5/6/8 房应按各攻击时间轴明确选取挥击、碰撞、收招声的事件和帧位，再用用户实机核验是否发声、位置是否正确。第 10 房应先有限记录原生 timeline 触发及 Wwise post 结果，定位为何已有资源未听见。第 3 房应先确认所见冲刺的 action ID 和 FollowUp/cascade 实际执行情况。上述工作均不以离线资源存在替代实机结论。
