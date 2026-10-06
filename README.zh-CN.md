# 《恶意不息》Second Crucible（第二试炼）Mod

[![Read in English](media/language-en.svg)](README.md)

[![简体中文快速安装](media/install-quick-zh-CN.svg)](INSTALL-QUICK.zh-CN.md)
[![English Quick Install](media/install-quick-en.svg)](INSTALL-QUICK.md)

解锁《恶意不息》（No Rest for the Wicked）的**第二试炼（Second Crucible）**，修复遭遇战流程，并支持联机游玩。

![第二试炼：解锁与遭遇战修复，Build 22928553](media/header.png)

**[直接下载 0.9.5 ZIP](download/0.9.5/CrucibleUnlock-0.9.5.zip)** · [直接下载 DLL](download/0.9.5/Mods/CrucibleUnlock.dll) · [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108) · [四步安装](INSTALL-QUICK.zh-CN.md)

版本 **`0.9.5`** · 支持 **Steam public Build 22928553** · 非官方社区 Mod

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

## 联机与复活

支持联机。在某一层的 Boss 战中，如果有队友阵亡，只要存活成员击败本层 Boss，**本场 Boss 战中阵亡的队友**就会复活，生命值恢复为**各自最大生命值的 50%**。

## 实机演示

观看 Mod 的实际游玩效果：

**[▶ 在 YouTube 观看](https://www.youtube.com/watch?v=Rw-hCcjvWss)** · **[▶ 在哔哩哔哩观看](https://www.bilibili.com/video/BV1zbHH6EEGx)**

## 下载与安装

Windows Steam 玩家按四步操作：下载 MelonLoader 0.7.3、安装加载器、下载 [0.9.5 ZIP](download/0.9.5/CrucibleUnlock-0.9.5.zip)、放入 DLL 并设置三项配置。

[![简体中文快速安装](media/install-quick-zh-CN.svg)](INSTALL-QUICK.zh-CN.md)
[![English Quick Install](media/install-quick-en.svg)](INSTALL-QUICK.md)

遇到问题再看[详细安装与排错教程](INSTALL-DETAILED.zh-CN.md)。

## 兼容性与安全说明

- **已验证范围：** 2026-10-05 在 Steam public **Build 22928553** 上完成过一次九层完整流程，包含预期的七场 Boss 遭遇战、检查点、奖励房，以及返回第二献祭盆。这仅是一个游戏版本上的一次完整测试，不代表后续版本兼容性保证。其他游戏版本及反复重载场景仍未验证。详见[版本记录](CHANGELOG.md)。
- **0.9.5 验证状态：**离线检查通过；堕落外壳普通头顶血条的最后修正尚未由玩家实机确认。
- **存档行为：** 解锁只在 Mod 运行期间存在。正常游玩仍可能保存进度；本 Mod 不会把对应任务永久写成完成。
- **更新与卸载：** 不要在游戏运行时替换 DLL。卸载时先退出游戏，再移除 `Mods/CrucibleUnlock.dll`；如需卸载 MelonLoader，请单独处理。详见[卸载与更新说明](INSTALL.zh-CN.md#卸载与更新)。
- **旧版提醒：** 不要使用旧的 `0.4.0` 压缩包，也不要沿用研究工作区中旧版的永久解锁说明。

## 技术说明与源码构建

[`src/`](src/) 下保存的是冻结的 **`0.9.5-husk-hud-candidate`** 源码快照。

- Mod 在安装运行时钩子之前，会检查**精确匹配的受支持游戏版本**。
- 构建需要 MelonLoader 引用，以及从受支持游戏版本生成的 IL2CPP 互操作程序集。
- 本仓库**不包含**游戏、MelonLoader 二进制文件或生成的互操作程序集。

本地环境要求、构建命令、测试和打包流程见 **[BUILD.md](BUILD.md)**。

## 许可说明

代码采用 **[MIT 许可](LICENSE-CODE.md)**。`src/CrucibleUnlock/Assets/SecondBowlBlood/` 下的六个图像数据文件不在该代码许可范围内；完整适用范围以许可文件为准。

这是一个**非官方社区项目**。
