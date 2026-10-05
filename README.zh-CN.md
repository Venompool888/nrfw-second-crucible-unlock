# 《恶意不息》第二回声解锁 Mod

[![Read in English](media/language-en.svg)](README.md)

解锁《恶意不息》（No Rest for the Wicked）的**第二回声（second Echo / second Crucible）**，修复遭遇战流程，并支持联机游玩。

![第二回声：解锁与遭遇战修复，Build 22928553](media/header.png)

**[前往 Nexus Mods 下载](https://www.nexusmods.com/norestforthewicked/mods/108)** · [安装教程](INSTALL.zh-CN.md) · [观看实机演示](#实机演示)

版本 **`0.8.6-boss-order`** · 已验证 **Steam public Build 22928553** · 非官方社区 Mod

## 章节索引

- [功能介绍](#功能介绍)
- [联机与复活](#联机与复活)
- [实机演示](#实机演示)
- [下载与安装](#下载与安装)
- [兼容性与安全说明](#兼容性与安全说明)
- [技术说明与源码构建](#技术说明与源码构建)
- [许可说明](#许可说明)

## 功能介绍

- **进入第二回声。** 解锁仅在 Mod 加载期间生效，不会永久完成任务，也不会直接修改存档。
- **体验游戏原有的遭遇战流程。** 复用游戏已有的遭遇战序列，修复第二献祭盆、地面血路、房间推进、Boss 内容选择以及 Warrick 的第二阶段。
- **支持联机游玩。** 存活队员击败本层 Boss 后，本场 Boss 战中阵亡的队友可以复活并重新加入。

## 联机与复活

支持联机。在某一层的 Boss 战中，如果有队友阵亡，只要存活成员击败本层 Boss，**本场 Boss 战中阵亡的队友**就会复活，生命值恢复为**各自最大生命值的 50%**。

## 实机演示

观看 Mod 的实际游玩效果：

**[▶ 在 YouTube 观看](https://www.youtube.com/watch?v=Rw-hCcjvWss)** · **[▶ 在哔哩哔哩观看](https://www.bilibili.com/video/BV1zbHH6EEGx)**

## 下载与安装

玩家安装包请从 **[Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108)** 获取。请使用已发布的 Mod ZIP，不要把 GitHub 的源码 ZIP 当成安装包。

> [!IMPORTANT]
> 安装加载器或 Mod 前，请先备份国度与角色存档。本版仅针对 **Steam public Build 22928553** 验证。游戏更新后请等待兼容版本，不要绕过版本检查。

1. 从官方发布页安装兼容的 **[MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases)**，并让它生成游戏的 IL2CPP 程序集。
2. 退出游戏。解压 Mod ZIP，将 `CrucibleUnlock.dll` 复制到 `<游戏目录>/Mods/CrucibleUnlock.dll`。
3. 打开 `UserData/MelonPreferences.cfg`，在唯一的 `[CrucibleUnlock]` 区块中设置以下值。保留文件中已有的其他配置。

   ```ini
   [CrucibleUnlock]
   mode = "runtime-unlock"
   guard_broken_warrick_music = true
   repair_warrick_phase2_target = true
   ```

   两个修复选项在缺省时均为 `false`。要使用本次已验证的遭遇战行为，必须显式开启。
4. 通过 Steam 启动游戏。在 `MelonLoader/Latest.log` 中确认版本检查通过、两个定向读取覆盖已安装，以及两个修复选项均显示为 `True`。

### 安装指南

- **[英文简版安装说明](INSTALL.md)：** 安装、配置、验证与卸载
- **[简体中文详细安装教程](INSTALL.zh-CN.md)：** Windows／Steam 完整步骤、ZIP 放置、校验与故障排查
- **[独立中文版 HTML 教程](INSTALL.zh-CN.html)：** 保存后可在本地浏览器中打开

### 让 AI Agent 帮你安装

在装有 Steam、官方游戏和 AI Agent 客户端的 Windows 电脑上，可把 **[AI Agent 安装任务书](AGENT-INSTALL.zh-CN.md)** 交给 DSH、Codex、WorkBuddy 或豆包桌面端。不需要预装 Git、Python、.NET SDK 或 Mod 工具。Agent 负责安装与离线检查，游戏启动及游戏内验证由你亲自完成。

<details>
<summary><strong>展开并复制安装指令</strong></summary>

```text
请帮我在这台 Windows 电脑上安装《恶意不息》（No Rest for the Wicked）的第二回声 Mod。先打开并完整阅读 GitHub 上的安装任务书：https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/main/AGENT-INSTALL.zh-CN.md ，然后按任务书实际执行下载、版本核对、存档备份、安装和离线复核，不要只总结教程。电脑目前只有 Steam、官方游戏、你这个 AI Agent 客户端，可能还有 Chrome；没有预装 Git、Python、.NET SDK 或 Mod 工具。只从任务书指定的官方来源取文件；如果网页需要我登录、过验证码或手动下载，请告诉我具体该做什么，完成后继续。不要替我启动或操作游戏；游戏内验证由我亲自完成。遇到游戏版本、文件哈希不符或将覆盖现有文件时，停止并报告。最后列出实际安装路径、校验结果及仍需我完成的步骤。
```

</details>

## 兼容性与安全说明

- **已验证范围：** 2026-10-05 在 Steam public **Build 22928553** 上完成过一次九层完整流程，包含预期的七场 Boss 遭遇战、检查点、奖励房，以及返回第二献祭盆。这仅是一个游戏版本上的一次完整测试，不代表后续版本兼容性保证。其他游戏版本及反复重载场景仍未验证。详见[版本记录](CHANGELOG.md)。
- **存档行为：** 解锁只在 Mod 运行期间存在。正常游玩仍可能保存进度；本 Mod 不会把对应任务永久写成完成。
- **更新与卸载：** 不要在游戏运行时替换 DLL。卸载时先退出游戏，再移除 `Mods/CrucibleUnlock.dll`；如需卸载 MelonLoader，请单独处理。详见[卸载与更新说明](INSTALL.zh-CN.md#卸载与更新)。
- **旧版提醒：** 不要使用旧的 `0.4.0` 压缩包，也不要沿用研究工作区中旧版的永久解锁说明。

## 技术说明与源码构建

[`src/`](src/) 下保存的是冻结的 **`0.8.6-boss-order`** 源码快照。

- Mod 在安装运行时钩子之前，会检查**精确匹配的受支持游戏版本**。
- 构建需要 MelonLoader 引用，以及从受支持游戏版本生成的 IL2CPP 互操作程序集。
- 本仓库**不包含**游戏、MelonLoader 二进制文件或生成的互操作程序集。

本地环境要求、构建命令、测试和打包流程见 **[BUILD.md](BUILD.md)**。

## 许可说明

代码采用 **[MIT 许可](LICENSE-CODE.md)**。`src/CrucibleUnlock/Assets/SecondBowlBlood/` 下的六个图像数据文件不在该代码许可范围内；完整适用范围以许可文件为准。

这是一个**非官方社区项目**。
