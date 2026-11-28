# 工单看板

工单在 GitHub 上：https://github.com/Venompool888/nrfw-second-crucible-unlock/issues （标签：类型、优先级、area:模块、待审批、待决定、待试玩验证、已验证）。本地文件和 GitHub issue 一一对应。流程见 `README.md` 和 `../DEVELOPMENT-PROCESS.md`。

状态：待审批 → 待做 → 进行中 → 测试中 → 已合入 → 试玩验证通过。审批通过前不动工。

## 待审批（1）

| 工单 | GitHub | 类型 | 优先级 | 模块 | 标题 |
|---|---|---|---|---|---|
| [T-016](T-016.md) | [#24](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/24) | bug | P2 | necromancy | Boss 召唤符文召出的随从在试炼场景不跟随玩家（只靠 20 米传回追上） |

## 待做（0）

## 进行中（0）

## 测试中（1）

| 工单 | GitHub | 类型 | 优先级 | 模块 | 标题 |
|---|---|---|---|---|---|
| [T-015](T-015.md) | [#23](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/23) | feature | P2 | necromancy | 自制 Boss 召唤符文（每枚召一种 Boss、带自己的图标）——首批沃里克、回声骑士 |

## 已合入（5）

| 工单 | GitHub | 类型 | 优先级 | 模块 | 标题 | 合入 |
|---|---|---|---|---|---|---|
| [T-014](T-014.md) | [#20](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/20) | feature | P2 | necromancy | 通用表情 → 招式反应（任何召唤物、多种表情各有反应）——第一阶段 + 多招 `pick`；第二、四阶段未做 | PR #21 · `a05a963` |
| [T-002](T-002.md) | [#3](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/3) | task | P2 | workflow | 采用 OriCoop 工作流（工单、分支、分层测试、PR） | PR #11 · `9fdcb86` |
| [T-001](T-001.md) | [#2](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/2) | feature | P2 | necromancy | 恢复未放出的死灵起尸玩法（raiseMinion / revive / healMinions + 治疗宝典） | PR #14 · `6a3e998` |
| [T-004](T-004.md) | [#5](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/5) | bug | P2 | necromancy | 治疗宝典镶不上死灵符文 | PR #14 · `6a3e998` |
| [T-005](T-005.md) | [#6](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/6) | bug | P3 | necromancy | 召唤出的随从没有颜色区别 | PR #14 · `6a3e998` |

## 试玩验证通过（8）

| 工单 | GitHub | 类型 | 优先级 | 模块 | 标题 | 合入 | 验证 |
|---|---|---|---|---|---|---|---|
| [T-003](T-003.md) | [#4](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/4) | bug | P1 | necromancy | 起尸系统在原版里没有注册，起尸无法运行 | PR #14 · `6a3e998` | 2026-11-28 · 0.17.0 日志 |
| [T-006](T-006.md) | [#7](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/7) | bug | P2 | necromancy | 随从与玩家互相误伤（联机玩家之间的友伤保持不变） | PR #14 · `6a3e998` | 2026-11-28 · 0.17.0 日志（仅「随从不伤召唤者」） |
| [T-007](T-007.md) | [#8](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/8) | bug | P2 | necromancy | 随从跟着传送后血量回满 | PR #14 · `6a3e998` | 2026-11-28 · 0.15.16 日志 |
| [T-008](T-008.md) | [#9](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/9) | feature | P3 | necromancy | 随从在场时限 | PR #14 · `6a3e998` | 2026-11-28 · 0.15.16 日志 |
| [T-009](T-009.md) | [#12](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/12) | feature | P3 | necromancy | 随从传回距离拉远到 3 倍 | PR #14 · `6a3e998` | 2026-11-28 · 0.17.0 日志 |
| [T-010](T-010.md) | [#13](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/13) | feature | P2 | necromancy | 起尸召出的随从改成沃里克 | PR #18 · `ab6f0a6` | 2026-11-28 · 0.17.0 日志 |
| [T-011](T-011.md) | [#15](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/15) | bug | P1 | necromancy | 0.13.1 战斗中崩溃：寻路实体缺导航组件；随从按水平距离与高度差决定是否传回 | PR #19 · `0936148` | 2026-11-28 · 0.17.0 日志 |
| [T-012](T-012.md) | [#16](https://github.com/Venompool888/nrfw-second-crucible-unlock/issues/16) | feature | P2 | necromancy | 发表情「怒吼」时让召出的沃里克放插剑震波 | PR #18 · `ab6f0a6` | 2026-11-28 · 0.15.16 画面 + 0.17.0 日志 |
