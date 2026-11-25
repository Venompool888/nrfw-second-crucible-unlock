# NRFW Necromancy（死灵起尸恢复）

独立 MelonLoader 插件 `NRFWNecromancy.dll`，不依赖 CrucibleUnlock。目标公测 Steam Build 22928553。

## 做什么

1. **补系统**：后缀挂 `SystemSetup.CreateSystems`，递归检查返回的系统树（含 `_children`）。缺 `CorpseMarkerSystem` 或 `RaisedMinionSystem` 时追加到根数组；会话前几帧再检查 `IsEnabledInHierarchy`，关着就 `Enable`。
2. **发符文（一次性）**：`grant_runes_once=true` 时，本地英雄绑定满约 300 个已验证帧后，对背包和仓库里数量为 0 的符文调用 `InventoryAPI.PreviewAwardItemImmediate(..., NewInventoryItemSource.Loot)`，各发 1 枚，然后把配置改成 `false` 并保存。不用 `Cheat` 来源。
3. **诊断日志**：系统是否存在、是否运行，以及前若干次 `CanRaiseNearby`、起尸 `Execute`、治疗随从 `Execute` 的结果。写入 `UserData/NRFWNecromancy.log` 和 MelonLoader 日志的 `[necromancy]` 行。

| 符文 | AssetGuid | 动作 | 要点 |
|---|---|---|---|
| raiseMinion | 2160606268309502004 | 22455697660660456 | 固定召出 npcRisenWildlingSwordA（2598229157892424643） |
| revive | 565977864064423183 | 943531419627853566 | `UseCorpseNpcType=true`，起来的是尸体原本的敌人 |
| healMinions | 4408421902141782894 | 2754443711296488034 | 随从各回复 50 点 |

三枚都是长杖／魔杖符文（CompatibleClasses `[25, 24]`），`ThomasApproved=true`，也都不在 `StrippedAssetGuidsSet` 中。两枚起尸符文：100 专注，搜尸半径 8，尸体 300 秒内有效，最多 5 名随从，随从生命/伤害按普通 0.5、精英/巨型 0.25、Boss 0.1 缩放，随从命中给玩家回 2 专注。治疗符文另耗 50 专注。

## 配置

`UserData/MelonPreferences.cfg`：

```
[NRFWNecromancy]
grant_runes_once = true
```

想再发一次就把它改回 `true`；已持有的符文会跳过。

## 构建与离线核验

```powershell
./.dotnet/dotnet.exe build src/Necromancy/Necromancy.csproj -c Release
./.dotnet/dotnet.exe run --project tools/NecromancyHookAudit -c Release -- vendor/generated src/Necromancy/bin/Release/NRFWNecromancy.dll
```

审计只读 PE 元数据，核对 7 个 hook 签名唯一存在、两个系统有无参构造、发放 API 签名，以及编译产物确实按名挂了这些方法。

## 限制

- 未实机验证：系统原本是否注册、召唤物阵营与跟随、尸体提示 UI 都要看实机日志。
- 符文没有本地化名字，界面可能显示为空名或内部名。
- 仅单机：联机时各端系统树必须一致，否则可能失步。
- 已发进背包的符文属于存档内容，卸载插件不会删除。
