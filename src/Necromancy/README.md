# NRFW Necromancy（死灵起尸恢复）

独立 MelonLoader 插件 `NRFWNecromancy.dll`，不依赖 CrucibleUnlock。目标公测 Steam Build 22928553。工单：T-001。

## 做什么

1. **补系统**：后缀挂 `SystemSetup.CreateSystems`。实机确认原版生成的 105 个系统里没有 `CorpseMarkerSystem` 和 `RaisedMinionSystem`，插件把它们追加到根数组；会话前几帧再检查 `IsEnabledInHierarchy`，关着就 `Enable`。
2. **宝典可镶死灵符文**：后缀挂 `RunesAPI.Weapon.IsRuneCompatible` 的两个重载，只放行「治疗宝典（或无类别武器）+ 三枚原版死灵符文或已注册的 Boss 符文」，其他组合保持原判定。
3. **按角色发放（每个角色一次）**：`grant_character` 填的角色读档约 5 秒后，背包里没有治疗宝典就新建一本金色宝典，再把三枚符文直接写进它的符文槽 1–3（运行时先核对原生布局，不符就不写），然后请求 `ArmamentReload`；背包里没有散装符文时各补发 1 枚。发过的角色名记进 `granted_characters`。
4. **随从着色**：从 `RaisedMinionSystem.GetMinionFormationSlot` 记下随从实体，在主线程给它的 `EntityView` 补挂原版的 `RaisedMinionVisualEffect`（敌人模型上原本没有这个组件）。
5. **随从不误伤**：原生伤害入口按 `RaisedMinionComponent.Owner` 识别玩家召出的随从，拦截玩家↔随从和随从↔随从的伤害。范围技逐目标判断，投射物通过 `InstigatorRef` 追溯发射者并跳过受保护目标。玩家↔玩家以及与敌人的伤害交给原版结算。日志 `minion damage BLOCKED` 区分伤害拦截和投射物碰撞拦截。
6. **随从不自动回满、可选时限**：前缀挂 `HealthSystem.UpdateEnemyHealthRegen`，对随从跳过敌人脱战回血。原版数据没有随从时限；`minion_lifetime_seconds` 到时用 `DamageAPI.Kill` 结束随从，`CorpseMarkerSystem.OnNpcDied` 对到期随从不留尸体。
7. **传回距离**：`RaiseMinionActionData.MaxDistanceBeforeTeleport`（原版 20 米）乘 `minion_teleport_distance_multiplier`；后缀挂 `Frame.FrameEvents.RaisedMinionTeleported` 记每次随从传送（`minion TELEPORTED … moved=… m`）。
8. **诊断日志**：受击来源从原生 `EventDamageResolved` 内存读 `PayloadProxyGroup` 代理槽（互操作的托管副本截断了定长缓冲，`Main` 等读出来无效）；写入 `UserData/NRFWNecromancy.log` 和 MelonLoader 日志的 `[necromancy]` 行。

| 物品 | AssetGuid | 动作 | 要点 |
|---|---|---|---|
| raiseMinion | 2160606268309502004 | 22455697660660456 | 固定召出 npcRisenWildlingSwordA（2598229157892424643） |
| revive | 565977864064423183 | 943531419627853566 | `UseCorpseNpcType=true`，起来的是尸体原本的敌人 |
| healMinions | 4408421902141782894 | 2754443711296488034 | 随从各回复 50 点 |
| 治疗宝典 tomeOfHealing（副手） | 3937520191132810478 | 2452854199486692768（正式「治疗」符文动作） | 无正常获取途径 |

三枚符文都是长杖／魔杖符文（CompatibleClasses `[25, 24]`），`ThomasApproved=true`，都不在 `StrippedAssetGuidsSet` 中，运行时符文表序号 117／22／243。起尸：100 专注，搜尸半径 8，尸体 300 秒内有效，最多 5 名随从，随从生命/伤害按普通 0.5、精英/巨型 0.25、Boss 0.1 缩放，随从命中给玩家回 2 专注。治疗随从另耗 50 专注。

## 配置

`UserData/MelonPreferences.cfg`：

```
[NRFWNecromancy]
grant_character = "你的角色名"
granted_characters = ""
minion_lifetime_seconds = 600
minion_teleport_distance_multiplier = 3
```

- `grant_character` 默认为空，即不发放；填上角色名（不区分大小写）才会给这个角色发。
- 想给已发过的角色再发一次，从 `granted_characters` 里删掉它的名字。
- `minion_lifetime_seconds` 填 0 表示随从一直存在，直到被打死。
- `minion_teleport_distance_multiplier`：随从离玩家多远才传送回来，乘在原版 20 米上（默认 3 = 60 米，1 = 原版）。
- `minion_npc`（T-010）：raiseMinion 召出谁。默认 `native`（原版野人剑士）。也可填 `warrick_boss`（变异者沃里克）、`warrick_enemy`（巨型档）、`wildling`（原版野人剑士）或 NpcData GUID；已有配置不会自动重置。目标解析不到 NpcData 时保持原版。revive 和 Boss 符文不受此项影响。
- `minion_max_active`（T-010）：换了召唤目标时 raiseMinion 同时在场上限，默认 1，0 = 原版 5，不会超过原版。沃里克按 Boss 档缩放（生命、伤害 ×0.1）。

### 表情反应（T-014，取代 T-012 的写死规则）

- 反应表在 `UserData/NRFWNecromancyReactions.json`。文件不存在时插件按 `roar_gesture`／`roar_action` 写出默认表，里面只有一条 T-012 的规则：你的沃里克（Boss 版、巨型版）在你做「凶残之力」时放延迟范围攻击，每只随从冷却 3 秒。改完存盘，下次进游戏或读档时生效（会话开始时检查文件修改时间）。
- 每条规则：`who`（NPC 资产 GUID／`profile:名字`／`tier:boss|giant|elite|normal`／`*`，可写一组）、`gesture`（表情 GUID）、`action`（动作 GUID，一个或一组，最多 16 个）、`pick`（`first`／`random`／`cycle`）、`cooldown`（秒，0–600，默认 3）、`mode`（目前只有 `force`）、`name`（日志用）。GUID 建议写成带引号的字符串。
- 匹配：发表情时，你召出的每只随从各自取最具体的一条规则（NPC GUID ＞ profile ＞ 档位 ＞ `*`，同级取文件里靠前的），各放各的招；没有规则的随从不受影响。冷却按随从计。`random` 用（随从, 模拟时间）的哈希选，不动游戏的随机数。
- `profile:` 要等 T-013（召唤物分档配置）合入才会匹配，现在写了只会在日志提示一次。
- 写错的规则被跳过，日志 `reactions rule REJECTED rule #n: 原因`；整个文件读不了（JSON 坏、没有 `rules`、`version` 不是 1）时日志 `reactions file INVALID`，并退回内置的沃里克规则。`mode: prefer`（让 AI 自己选招时加权）还没做，写了会被拒。
- `roar_enabled`：总开关，默认 `true`。`roar_gesture`／`roar_action` 现在只在生成默认表时用；已有反应表时改它们没有效果，删掉 JSON 文件即可按新值重新生成。
- 日志：每次做表情 `gesture GUID=… matched=yes|no`（前 60 次）；放招 `reaction minion entity=… npc=名字/档位 rule=#n(name) action GUID=… layer=1 -> 结果 executingNow=…`；+0.5 s／+1.5 s `reaction trace … executing=…`；没人放招时 `reaction gesture matched but no minion commanded: … [每只随从的原因]`；动作 GUID 不是 ActionData 时 `reaction action not resolved: GUID (asset type …)`。
- 已知 GUID：表情凶残之力 bestialPower `2126111680215882670`、fisticuffs `1770795828947168763`；沃里克的动作：延迟范围攻击 `4386907888524794362`、下劈短震波 `1578638975510328898`、跳劈（快）`2630019745959013014`。插剑震波内／中／外圈（`3660829114871757151` 等）是招式生成的震波实体，不是动作。其他敌人的动作 GUID 要靠离线读包（T-014 第二阶段的本地对照表）。

### Boss 召唤符文（T-015）

- `UserData/NRFWNecromancyBossRunes.json` 首次运行时生成，默认沃里克和步战回声骑士各一枚。仍需 8 米内有尸体；各自独立计数，默认每种最多 1 只，可同时在场。沿用原版起尸专注消耗和 Boss 倍率，可镶长杖、魔杖、治疗宝典。
- JSON 是数组，支持注释和尾逗号。每条设唯一 `key`（小写字母、数字、下划线）、唯一 `slot`（整数 0–255）、`npc`（正数 NpcData GUID，建议用字符串）；可配 `enabled`（默认 `true`）、`max_active`（1–5，默认 1）、`icon` 和 `name_zh`／`name_en`／`desc_zh`／`desc_en`。非法条目跳过并记日志；整文件读不出则用默认表。改完重启游戏。
- `slot` 决定存档 GUID，发放后不要改或复用。基数 `0x0000543031350000 + slot × 0x10`；物品 `I` 和动作 `A` 分别加 1、2，再置 `0xC000000000000000`，内部 ItemData 键加 3，再置 `0x8000000000000000`。`key` 用于发放记录。
- `boss_runes_enabled` 默认 `true`；`text_language` 为 `zh`（默认）或 `en`。沿用 `grant_character`，但和宝典分开记录：模拟注册和 Unity 包装均就绪后，角色缺哪枚就发哪枚散装。`granted_boss_runes` 用逗号分隔 `角色名:key`，不区分大小写；移除某对可重新检查发放。
- 图标放 `UserData/NRFWNecromancyBossRunes/`，`icon` 只填文件名，例如 `warrick.png`。建议 **512×512 透明底 PNG**，主体居中，左右各留约 45 px、上下各留约 25 px。留空、缺图或解码失败时用原版图标。
- **卸载前备份角色档**：卸载、关闭或删掉对应配置后，读档会跳过散装的未知 GUID，武器槽里的该符文会变成空槽；此时另存就会丢失。先用备份测试发放、镶嵌、施放和退出重读；本阶段只记录缺失的英雄 ActionView，不做重绑。

## 构建与离线核验

```powershell
./.dotnet/dotnet.exe build src/Necromancy/Necromancy.csproj -c Release
./.dotnet/dotnet.exe run --project tools/NecromancyHookAudit -c Release -- vendor/generated src/Necromancy/bin/Release/NRFWNecromancy.dll
./.dotnet/dotnet.exe run --project tests/NecromancyReactionTests -c Release
./.dotnet/dotnet.exe run --project tests/NecromancyBossRuneTests -c Release
```

审计只读 PE 元数据：核对全部 hook 签名唯一存在、调用的接口签名、类型继承，并测试纯逻辑（符文放行、布局校验、按角色发放、敌我规则、时限换算、传回距离倍数）。`NecromancyReactionTests` 不需要游戏文件，测反应表的读取、匹配优先级、冷却、选招和必须被拒的写法。

## 限制

- 三枚符文没有本地化名字，界面可能显示为空名或内部名；宝典图标可能是占位图。
- 仅单机：联机时各端系统树必须一致，否则可能失步。
- 跨区域传送时如果游戏重建了随从实体，时限会重新计时（待实机日志确认）。
- 原版 GUID 的已发物品属于存档内容，卸载插件不会删除；自制 Boss 符文例外（见上面的卸载风险）。
- `boss_tome_character`（0.18.1）：给该角色一次性发一本新的金色治疗宝典，所有就绪的 Boss 符文直接写进宝典符文槽；记录为 `granted_boss_runes` 里的 `角色:@tome`。同一角色的散装 Boss 符文若已镶在宝典上则不再另发。
