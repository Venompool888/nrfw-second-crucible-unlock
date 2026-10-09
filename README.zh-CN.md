# 《恶意不息》Second Crucible（第二试炼）Mod

[![Read in English](media/language-en.svg)](README.md)

[![简体中文快速安装](media/install-quick-zh-CN.svg)](INSTALL-QUICK.zh-CN.md)
[![English Quick Install](media/install-quick-en.svg)](INSTALL-QUICK.md)

解锁《恶意不息》（No Rest for the Wicked）的**第二试炼（Second Crucible）**，修复遭遇战流程，并支持联机游玩。

![第二试炼：解锁与遭遇战修复，Build 22928553](media/header.png)

**[下载 0.9.17 预发布核心 DLL](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/CrucibleUnlock.dll)** · **[下载弩箭音效／输入组件（0.3.3）](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/NRFWBowgunAudioSync.dll)** · **[图文安装教程](https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/v0.9.17/docs/index.html)** · [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108) · [四步安装](INSTALL-QUICK.zh-CN.md)

最新稳定版：**`0.9.16`** · 候选预发布版：**`0.9.17`**（仅离线核验，尚未实机测试）· 支持 **Steam public Build 22928553** · 非官方社区 Mod

## 章节索引

- [功能介绍](#功能介绍)
- [联机与复活](#联机与复活)
- [实机演示](#实机演示)
- [下载与安装](#下载与安装)
- [兼容性与安全说明](#兼容性与安全说明)
- [技术说明与源码构建](#技术说明与源码构建)
- [许可说明](#许可说明)

## 功能介绍

- **进入第二试炼。** 解锁仅在 Mod 加载期间生效，不会永久完成任务，也不会直接修改存档。
- **体验游戏原有的遭遇战流程。** 复用游戏已有的遭遇战序列，修复第二献祭盆、地面血路、房间推进、Boss 内容选择以及 Warrick 的第二阶段。
- **支持联机游玩。** 存活队员击败本层 Boss 后，本场 Boss 战中阵亡的队友可以复活并重新加入。
- **获得 Boss 痕迹并选择更多回声。** 第二试炼符合条件的 Boss 默认掉落 300 痕迹；当前角色的回声上限提高至 2000。
- **修正 Boss 界面。** 背誓人与堕落外壳的血条和名称按场景修正；堕落外壳名称覆盖游戏现有的 12 种语言。

- **使用已装备的强化弩枪。** 副手装备后，用游戏当前的举盾动作发射：Xbox 手柄 LB，或键鼠当前举盾绑定。额外的音效／输入 DLL 接通键鼠，并将弩枪发射音效绑定到箭矢释放。本版不会直接发放武器。

## 联机与复活

支持联机。在某一层的 Boss 战中，如果有队友阵亡，只要存活成员击败本层 Boss，**本场 Boss 战中阵亡的队友**就会复活，生命值恢复为**各自最大生命值的 50%**。

## 实机演示

观看 Mod 的实际游玩效果：

**[▶ 在 YouTube 观看](https://www.youtube.com/watch?v=Rw-hCcjvWss)** · **[▶ 在哔哩哔哩观看](https://www.bilibili.com/video/BV1zbHH6EEGx)**

## 下载与安装

使用 0.9.17 预发布版时，下载并安装 MelonLoader 0.7.3，再下载两个 DLL 并放进 `Mods`。核心会默认启用已验收的运行行为，无需设置偏好文件。只有想修改 Boss 痕迹数量时，才编辑现有 `[CrucibleUnlock]` 区块中的 `boss_trace_drop_amount`。需要已实机测试的版本时，请使用 0.9.16 稳定版。

[![简体中文快速安装](media/install-quick-zh-CN.svg)](INSTALL-QUICK.zh-CN.md)
[![English Quick Install](media/install-quick-en.svg)](INSTALL-QUICK.md)

**[图文安装教程（截图版）](https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/v0.9.17/docs/index.html)** · [English screenshot guide](https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/v0.9.17/docs/index.htmlen.html)

## 兼容性与安全说明

- **已验证范围：** 2026-10-05 在 Steam public **Build 22928553** 上完成过一次九层完整流程，包含预期的七场 Boss 遭遇战、检查点、奖励房，以及返回第二献祭盆。这仅是一个游戏版本上的一次完整测试，不代表后续版本兼容性保证。其他游戏版本及反复重载场景仍未验证。详见[版本记录](CHANGELOG.md)。
- **0.9.17 预发布版：**仅完成离线构建与纯托管入口检查，尚未实机测试；0.9.16 仍是最新稳定版。
- **0.9.16 验证状态：**本地记录了 24 发弩箭（键鼠阶段 18 发、Gamepad 阶段 6 发），无输入线程异常，前 16 发有独立音效 ID 与释放同帧对照。PS 具体型号、可听起点尚未验证；两次回池／复用清理缺逐发日志，未见恢复错误。此前堕落外壳头顶血条修正未在本轮专项重测。
- **存档行为：** 解锁只在 Mod 运行期间存在。正常游玩仍可能保存进度；本 Mod 不会把对应任务永久写成完成。
- **更新与卸载：** 不要在游戏运行时替换 DLL。卸载时先退出游戏，再移除 `Mods/CrucibleUnlock.dll` 与 `Mods/NRFWBowgunAudioSync.dll`；如需卸载 MelonLoader，请单独处理。
- **旧版提醒：** 不要使用旧的 `0.4.0` 压缩包，也不要沿用研究工作区中旧版的永久解锁说明。

## 技术说明与源码构建

[`src/`](src/) 下包含 **0.9.17 玩家默认配置预发布候选版**及未变更的 **0.3.3** 音效／输入组件源码。核心行为改动尚未实机测试；0.9.16 仍是最新稳定版。

- Mod 在安装运行时钩子之前，会检查**精确匹配的受支持游戏版本**。
- 构建需要 MelonLoader 引用，以及从受支持游戏版本生成的 IL2CPP 互操作程序集。
- 本仓库**不包含**游戏、MelonLoader 二进制文件或生成的互操作程序集。

本地环境要求、构建命令、测试和打包流程见 **[BUILD.md](BUILD.md)**。

## 许可说明

代码采用 **[MIT 许可](LICENSE-CODE.md)**。`src/CrucibleUnlock/Assets/SecondBowlBlood/` 下的六个图像数据文件不在该代码许可范围内；完整适用范围以许可文件为准。

这是一个**非官方社区项目**。
