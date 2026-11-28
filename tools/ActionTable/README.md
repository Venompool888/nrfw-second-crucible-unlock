# ActionTable：离线动作／表情对照表（T-014 第二阶段）

只读读取本机游戏的 Quantum 资产包，导出 GUID 对照表，供 `NRFWNecromancyReactions.json` 填 `who`、`gesture`、`action`。不启动游戏，不写游戏目录，不算哈希。

**导出的表是游戏数据，只留在本机，不要提交进本仓库。** 仓库里只放这个脚本。

## 运行

需要 Python 3 和 UnityPy（1.25 测过），不依赖别的包。

```
python -I tools/ActionTable/build_action_table.py --bundle-dir "<游戏目录>/NoRestForTheWicked_Data/StreamingAssets/aa/StandaloneWindows64" --out "<仓库外的输出目录>"
```

脚本读 `qdb_assets_all_*.bundle`（资产类型树）和同一安装的 `StreamingAssets/quantumDatabase.bin`（运行时主记录），全量一次几秒。结束时打印对已知 GUID 的自检结果。

## 输出

| 文件 | 内容 |
|---|---|
| `assets.tsv` | 全部带 GUID 的 Quantum 资产：guid、类名、资产路径、名字 |
| `enemies.json` / `enemies.md` | 每个 NpcData：名字、GUID、档位（`EnemyType`）、AI 控制器，以及它引用到的每个动作的名字、GUID、类名和引用链（AI 组／选项名） |
| `gestures.tsv` | 全部表情：`…GestureData`（`CollectableGestureData`，填进 `gesture`）和对应的 `…Data` 动作 |

## 引用链

- AI：`NpcGenericData.ControllerData` 或 `NpcHumanoidEnemyData.ControllerData` → `AiControllerData.ActionList.ActionGroups[].Actions[].Data`，连招走 `…Actions[].ComboParams[].Action`。
- 武器／副手（人形敌人的招式大多在这里）：`HumanoidEnemyAiControllerData.Weapon` / `.OffHandItem` → `EnemyWeaponData.ActionList` 或 `.UnlockActions.Actions[].Action`。
- 特质：`AiControllerData.AffixMoveset` → `AffixMovesetData.ActionList.Actions[].Action`。
- 本体动作、受击／死亡反应、动作的后续动作也会列出并标明来源。填反应规则时，优先用标为「AI」或「武器／副手」的攻击动作。

## 限制

- 列出的是配置里的引用，不代表实战一定会触发（未模拟等级、多人、特质、静音等运行时条件）。没有扫描场景，也没有扫描原生代码追加的动作。
- 有的动作资产没有任何 NPC 引用，例如沃里克目录下的 `mutantSoldierAttackOverheadShortAOEData`（`1578638975510328898`）。它照样能被反应表强制执行（T-014 试玩已执行过），但不在沃里克自己的 AI 列表里。
- `EnemyType` 在本版数据里有 Normal、Boss、Critters、Giant、Elite、PlotEnemy 六种。
