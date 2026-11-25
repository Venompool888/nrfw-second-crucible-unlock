# 工单与分支流程

整个仓库的流程见 `docs/DEVELOPMENT-PROCESS.md`。这里是工单的细则。

## 工单

- 每个问题一张工单：`docs/tickets/T-xxx.md`，总览在 `BOARD.md`，同时是公开仓库的一个 issue，两边一一对应。
- **编号不撞**：取号前看 `docs/tickets/` 和 `gh issue list -R Venompool888/nrfw-second-crucible-unlock --state all`，用最大编号加 1，建好马上提交。
- 每张工单写：类型、优先级（P0 最急，到 P3）、模块、状态、负责会话、分支、合入提交、相关工单、进度记录。
- bug 必须有：在做什么、复现步骤、实际结果、期望结果、证据（日志文件名和时间点，脱敏后）。
- 状态：`awaiting-approval`（GitHub 标签「待审批」）→ `todo` → `in-progress` → `testing` → `done`（已合入 main）→ `verified`（试玩确认）。
- 设计问题待定时加标签「待决定」。

## 标签

- 类型：`feature`、`task`、`bug`
- 优先级：`P0`–`P3`
- 模块：`area:necromancy`、`area:workflow` 等
- 流程：`待审批`、`待决定`、`待试玩验证`、`已验证`

## 分支

- 每张工单一个分支 `t/T-xxx-slug`，每个分支一个 worktree（`..\crucible-mod-wt\T-xxx`），不在主目录切分支。
- 一组紧密相关的 bug 若在同一分支里修完（例如 T-001 的子问题），在各自工单里写明「随 T-xxx 合入」，PR 里一起 `Closes`。
