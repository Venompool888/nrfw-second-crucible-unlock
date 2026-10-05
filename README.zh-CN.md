# 《恶意不息》第二回声解锁 Mod

[![Read in English](media/language-en.svg)](README.md)

解锁《恶意不息》（No Rest for the Wicked）的**第二回声（second Echo / second Crucible）**，修复遭遇战流程，并支持联机游玩。

![第二回声：解锁与遭遇战修复，Build 22928553](media/header.png)

**[前往 Nexus Mods 下载](https://www.nexusmods.com/norestforthewicked/mods/108)** · [四步安装](INSTALL-QUICK.zh-CN.html) · [完整安装教程](#下载与安装) · [观看实机演示](#实机演示)

版本 **`0.8.6-boss-order`** · 已验证 **Steam public Build 22928553** · 非官方社区 Mod

## 章节索引

- [功能介绍](#功能介绍)
- [联机与复活](#联机与复活)
- [实机演示](#实机演示)
- [下载与完整安装教程](#下载与安装)
  - [准备与版本要求](#install-prepare)
  - [第 0 步：退出游戏并备份存档](#install-backup)
  - [第 1 步：找到游戏目录并核对版本](#install-locate)
  - [第 2 步：安装 MelonLoader](#install-loader)
  - [第 3 步：解压并放置 Mod DLL](#install-files)
  - [第 4 步：设置三个必填选项](#install-config)
  - [第 5 步：启动并确认加载成功](#install-verify)
  - [ZIP 与 DLL 校验及完成清单](#install-checksums)
  - [故障排查](#install-troubleshooting)
  - [卸载与更新](#install-uninstall)
  - [让 AI Agent 帮你安装](#install-agent)
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

这份教程写给第一次手动安装 Mod 的 Windows／Steam 玩家。对应的 Mod 是 **Second Crucible Unlock and Encounter Fixes（0.8.6）**，下载页在 [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108?tab=files)。只看最少步骤可打开[四步安装 HTML](INSTALL-QUICK.zh-CN.html)；下文按原中文详细教程完整展开，也可单独阅读[中文 Markdown 版](INSTALL.zh-CN.md)或[独立 HTML 版](INSTALL.zh-CN.html)。请使用已发布的 Mod ZIP，不要把 GitHub 的源码 ZIP 当成安装包，也不要使用旧的 `0.4.0` 包。

> **先看这句：**下载的 ZIP **不要放进游戏目录后就直接启动**，也不要把整个 ZIP 放进 `Mods`。要先解压，再让文件最终位于 `游戏安装目录\Mods\CrucibleUnlock.dll`。

<a id="install-prepare"></a>

### 你需要准备什么

- Windows 版 Steam 游戏《恶意不息》（No Rest for the Wicked）。本版只针对 **Steam public Build 22928553** 验证。游戏更新后，等 Mod 发布兼容版本；不要绕过版本检查。
- [Nexus Mods 文件页](https://www.nexusmods.com/norestforthewicked/mods/108?tab=files)的 **Main files → Crucible Unlock 0.8.6 → Manual download** 下载的 ZIP。
- 兼容的 **MelonLoader 0.7.x**。作者实测环境为 0.7.3 x64。到 [MelonLoader 官方发布页](https://github.com/LavaGang/MelonLoader/releases)或使用[官方安装器](https://github.com/LavaGang/MelonLoader.Installer)获取，不要从不明镜像站下载。
- 能解压 ZIP 的工具：Windows 文件资源管理器自带“全部解压”即可。

本 Mod 的 ZIP **不包含 MelonLoader 或游戏文件**。MelonLoader 是要先装好的加载器；Mod 自身的游戏内文件只有 `CrucibleUnlock.dll`。

<a id="install-backup"></a>

### 第 0 步：退出游戏并备份存档

1. 正常退出游戏，确认 Steam 中不再显示“正在运行”，并等待 Steam 下载和云同步结束。安装或更新过程中不要让游戏保持运行。
2. 按 `Win + R`，粘贴以下路径并按回车：

   ```text
   %USERPROFILE%\AppData\LocalLow\Moon Studios\NoRestForTheWicked
   ```

3. 把其中的 `DataStore` **复制**到桌面或另一个游戏目录外的备份位置。复制完成后保留原目录原样，不要移动或剪切它。若你还使用 Steam 云存档，先等 Steam 同步完成。建议将副本命名为 `NRFW-save-backup-日期-时间`，并检查备份确实存在。
4. 如果这台电脑从未运行游戏，可能还没有 `DataStore`；记录“没有现有存档”后继续。不要为了创建备份而移动、覆盖或删除存档。

备份是为了防范加载器、游戏更新或正常游玩时的意外情况；本 Mod 不会直接修改存档来永久完成第二回声，但游戏正常游玩仍会保存进度。移除 Mod 不会回滚这些正常游玩造成的存档变化。

<a id="install-locate"></a>

### 第 1 步：找到真正的游戏安装目录

在 Steam 库中右键《恶意不息》→**管理**→**浏览本地文件**。弹出的文件夹就是下文所说的“游戏安装目录”。不用猜盘符；每个人的 Steam 库位置可能不同。

请在这个文件夹中确认能看到 `NoRestForTheWicked.exe` 和 `NoRestForTheWicked_Data`。例如，游戏可能装在：

```text
D:\SteamLibrary\steamapps\common\NoRestForTheWicked\
```

这个路径只是**示例**，不是要求每个人都使用 D 盘。后面的 `Mods`、`MelonLoader` 和 `UserData` 都应直接位于你找到的游戏安装目录内。

**核对 Steam 版本：**在同一个 Steam 库的 `steamapps` 目录里找到 `appmanifest_1371980.acf`，用记事本只读打开，查找 `"buildid"`，值必须为 `22928553`，并确认使用的是 `public` 分支。如果有多个 Steam 库，结合清单中的 `installdir` 和实际主程序确定目录。版本不符就停止安装，等待兼容版本；不要降级游戏、修改原文件或绕过守卫。

**可选：核对游戏原文件。**用 Windows PowerShell 执行以下只读命令，将示例替换成实际完整路径：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '实际完整路径'
```

本版守卫期望的 SHA-256：

| 游戏原文件 | 期望 SHA-256 |
|---|---|
| `<游戏目录>\GameAssembly.dll` | `5B00EE90833B1BE2EA73E01CB83E710E09E86199D12AC769BE3C0E82ADD8B4BB` |
| `<游戏目录>\NoRestForTheWicked_Data\il2cpp_data\Metadata\global-metadata.dat` | `4C8DFE07E5F5178F8EEFD3D079412EC164EBA5DA798EFD2A2AE03AA37DC79502` |

如果选择核对，任一文件哈希不符都应停止，不要修改游戏原文件来“匹配”哈希。

<a id="install-loader"></a>

### 第 2 步：安装 MelonLoader（已经装好可跳到第 3 步）

推荐使用 [MelonLoader 官方安装器](https://github.com/LavaGang/MelonLoader.Installer)：打开安装器，选择《恶意不息》；如果没有自动列出，就用安装器的 **Add Game Manually** 指向游戏。选择兼容的 **0.7.x** 版本并安装。官方安装器的游戏选择和安装方式见其[说明](https://github.com/LavaGang/MelonLoader.Installer#melonloader-installation)。

**也可以手动安装实测的 0.7.3 x64：**

1. 打开 [MelonLoader 官方 v0.7.3 发布页](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3)，下载 Windows x64 手动安装包 `MelonLoader.x64.zip`。
2. 在“下载”文件夹中右键加载器 ZIP → **全部解压**，检查解压内容和目标目录。
3. 按官方包的目录结构，把加载器文件放到**游戏安装目录根部**，与 `NoRestForTheWicked.exe` 同层；不要放进 `Mods`。
4. 复制前检查目标位置有无同名文件。如果已有加载器或将覆盖其他文件，先确认来源与版本，再决定更新方案，不要直接覆盖。

安装完成后，在游戏安装目录检查是否出现 `MelonLoader` 文件夹。本次实测的 MelonLoader 0.7.3 安装还在游戏主程序同层生成了 `version.dll`；不同安装方式的文件清单可能不同，**不要以是否存在 `dobby.dll` 判断安装成败**。最终以启动日志显示实际加载器版本（例如 `MelonLoader v0.7.3 Open-Beta`）且加载器正常运行来确认。

首次安装加载器时，通过 Steam 启动一次游戏，让它生成 IL2CPP 互操作程序集与配置文件；这次启动可能比平常久。进入主菜单后正常退出，再继续下一步。如果加载器没有成功启动，先核对安装位置并查看日志，再继续安装 Mod。不要在游戏运行时复制或替换 Mod DLL。

本节是玩家手动安装流程。若由 AI Agent 按[安装任务书](AGENT-INSTALL.zh-CN.md)协助，可以先放置加载器、Mod 和配置，完成离线检查，再由你本人首次启动和核验；Agent 不替你启动或操作游戏。静态文件存在不等于加载器已经运行。

<a id="install-files"></a>

### 第 3 步：把 N 网 ZIP 解压到正确位置

1. 在 N 网文件页点击 **Main files** 下的 **Manual download**。若出现下载方式选择，按页面提示完成下载。
2. 到 Windows 的“下载”文件夹，找到下载的 `.zip`。右键它，选择**全部解压**。解压目标可以暂时放在“下载”文件夹里。
3. 打开解压出来的文件夹。这个包的内容应是：

   ```text
   ZIP 解压目录\
   ├── Mods\
   │   └── CrucibleUnlock.dll
   ├── CHANGELOG.md
   ├── INSTALL.md
   └── SHA256SUMS.txt
   ```

4. 复制前，按下方[文件校验说明](#install-checksums)核对本次 ZIP 和解压 DLL。若核对结果不符，停止安装。打开其中的 `Mods` 文件夹，**复制** `CrucibleUnlock.dll`。
5. 回到第 1 步打开的**游戏安装目录**。打开里面的 `Mods` 文件夹；如果没有，就在游戏安装目录新建一个名称恰好为 `Mods` 的文件夹。把 DLL 粘贴进去。
6. 最后检查完整位置：

   ```text
   <你的游戏安装目录>\Mods\CrucibleUnlock.dll
   ```

例如你的主程序位于 `D:\SteamLibrary\steamapps\common\NoRestForTheWicked\NoRestForTheWicked.exe`，那么 DLL 应在 `D:\SteamLibrary\steamapps\common\NoRestForTheWicked\Mods\CrucibleUnlock.dll`。

`CHANGELOG.md`、`INSTALL.md` 和 `SHA256SUMS.txt` 是阅读与校验用文件，**不必**放进 `Mods`。ZIP 解压后的完整文件夹也不必放进游戏目录。

**常见放错位置：**

```text
错误：<游戏目录>\Mods\CrucibleUnlock-0.8.6-boss-order.zip
错误：<游戏目录>\CrucibleUnlock-0.8.6-boss-order\Mods\CrucibleUnlock.dll
错误：<游戏目录>\Mods\Mods\CrucibleUnlock.dll
正确：<游戏目录>\Mods\CrucibleUnlock.dll
```

如果 `Mods` 中已有旧的 `CrucibleUnlock.dll`，先退出游戏，再把旧文件复制到游戏目录**外**作为备份，然后放入新版。不要同时保留两个不同名字的旧版／新版 Crucible Unlock DLL。

<a id="install-config"></a>

### 第 4 步：设置三个必填选项

打开游戏安装目录下的 `UserData\MelonPreferences.cfg`。可以右键文件→**打开方式**→**记事本**。如果没有 `UserData` 文件夹，就在游戏安装目录新建 `UserData`；如果配置文件不存在，就在该文件夹中新建 UTF-8 纯文本文件并命名为 `MelonPreferences.cfg`。手动安装时先按第 2 步确认加载器正常启动；Agent 离线安装时也可以提前新建，首次运行后再重新核对。在资源管理器中先打开“显示文件扩展名”，避免得到 `MelonPreferences.cfg.txt`。

在文件里找到 `[CrucibleUnlock]`。如果已有这个标题，**在同一个标题下修改**对应的键；不要再添加第二个 `[CrucibleUnlock]`。如果完全没有，移到文件末尾，空一行后加入以下内容：

```ini
[CrucibleUnlock]
mode = "runtime-unlock"
guard_broken_warrick_music = true
repair_warrick_phase2_target = true
```

如果配置文件里有其他 Mod 的设置，**保留它们**，只改本 Mod 的这三项。保存文件后重新打开检查一遍：`runtime-unlock` 有英文双引号；两个 `true` 没有引号、不是 `false`。这两个首 Boss 相关开关在缺省时为 `false`，所以只写 `mode` 不够。

最终目录大致如下，其他游戏或加载器文件可以更多：

```text
NoRestForTheWicked\                 ← 游戏安装目录，不要求盘符相同
├── NoRestForTheWicked.exe
├── NoRestForTheWicked_Data\
├── MelonLoader\                    ← 加载器
├── version.dll                     ← 本次实测安装中的加载器文件
├── Mods\
│   └── CrucibleUnlock.dll         ← 本 Mod 放在这里
└── UserData\
    └── MelonPreferences.cfg       ← 三项设置在这里
```

<a id="install-verify"></a>

### 第 5 步：启动并确认加载成功

通过 **Steam** 正常启动游戏。进入主菜单后，可以先退出，再用记事本打开：

```text
<游戏安装目录>\MelonLoader\Latest.log
```

在日志里用 `Ctrl + F` 依次找：

```text
Crucible Unlock 0.8.6-boss-order 已加载
mode = runtime-unlock
guard_broken_warrick_music = True
repair_warrick_phase2_target = True
[runtime-unlock] build guard passed
[runtime-unlock] installed two targeted read overrides
```

同时搜索 `MelonLoader` 并确认日志显示实际版本，例如 `MelonLoader v0.7.3 Open-Beta`。`0.7.x` 表示版本系列，不是应逐字搜索的日志内容。

这些文字说明 DLL、配置、游戏版本检查和临时解锁补丁已加载。只看到“已加载”，但没有 `build guard passed` 或两个读取覆盖的记录，**还不能算安装成功**。若日志里有其他内容或顺序不同，以这些关键项是否出现为准。

接着由你在游戏中去第二回声的献祭盆测试。这个解锁仅在 Mod 正常加载期间生效；它不会把对应任务步骤永久写成完成。

如果首次运行重写了配置，在完全退出游戏后重新打开配置文件，核对三个值。区分三个结果：DLL 在正确目录只是文件放置完成；日志关键项齐全才是运行加载通过；献祭、Boss 与后续楼层正常，需要你亲自在游戏中验证。

<a id="install-checksums"></a>

### 文件校验：核对本次 0.8.6 ZIP 与 DLL

本次 N 网主文件的 ZIP SHA-256 为：

```text
F3733D6DF54D4E515D5810DC3B0B6657D86FB1850430493FF62ECAB45C9E22CF
```

在“下载”文件夹的空白处打开 PowerShell，输入 `Get-FileHash`，后面加上你下载 ZIP 的完整路径，例如：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\你的用户名\Downloads\CrucibleUnlock-0.8.6-boss-order.zip'
```

浏览器保存的文件名可能带有 N 网自动添加的文字或编号；**以你实际下载的文件名替换示例**。只有同一发布包才应与上面哈希相同；未来更新的 ZIP 会不同。

解压后还要核对 `Mods\CrucibleUnlock.dll`；其 SHA-256 应为：

```text
F456A46C79A8F9C272946BB00BD555D127FFE770E1EDD7D757B44F6EDA5472CC
```

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '你的解压目录\Mods\CrucibleUnlock.dll'
```

可在资源管理器中右键文件选择“复制文件地址”，将命令引号内的示例替换为实际路径。**任一哈希不符，先停止安装并检查是否选错文件。**这些数值只适用于本次 `0.8.6` 发布包；未来更新包不同，应以对应版本的新说明为准，不要忽略不符结果继续安装。

**离线安装完成清单：**

- [ ] Steam `public` Build 为 `22928553`，游戏目录已由 Steam 实际定位
- [ ] 已有 `DataStore` 已复制到游戏目录外并检查副本；若无存档，已记录
- [ ] 兼容的官方 MelonLoader 文件在游戏根目录；本次实测版本为 `0.7.3 x64`
- [ ] 本次 Nexus ZIP 与解压 DLL 的 SHA-256 均匹配
- [ ] 只有一个版本的 Mod DLL，路径为 `Mods\CrucibleUnlock.dll`
- [ ] 配置中只有一个 `[CrucibleUnlock]` 区块，三个设置已保存并重新打开核对

完成离线清单后，仍需按[第 5 步](#install-verify)由你本人启动、看日志并进行游戏内测试。

<a id="install-troubleshooting"></a>

### 装不上时按现象排查

| 现象 | 先检查什么 |
|---|---|
| 找不到游戏文件夹 | 从 Steam 的“管理 → 浏览本地文件”重新打开，不要照抄示例 D 盘路径。 |
| 没有 MelonLoader 日志 | 确认加载器在主程序同层，以及你是否已通过 Steam 启动过一次。 |
| 游戏安装目录没有 `Mods` 或 `MelonLoader` | 先按第 2 步安装 MelonLoader，并确认指向了包含游戏 `.exe` 的目录。 |
| `Mods` 里只有 ZIP，看不到 `CrucibleUnlock.dll` | ZIP 还没有按第 3 步解压和复制。 |
| 日志没有 `Crucible Unlock 0.8.6-boss-order 已加载` | 检查 DLL 的完整路径、加载器是否启动、是否拿错了旧版压缩包。 |
| 日志显示 `mode = probe` | 配置没有保存到正确的 `UserData\MelonPreferences.cfg`，或存在重复的 `[CrucibleUnlock]`；核对文件扩展名。 |
| 日志中两个修复选项为 `False` | 按第 4 步在同一 `[CrucibleUnlock]` 区块下显式写 `true`，保存后完全退出并重启游戏。 |
| 日志里版本守卫失败，或没有 `build guard passed` | 确认 Steam public Build 是否为 **22928553**，并可只读核对上列两个游戏原文件的哈希；游戏更新后等待适配版。不要绕过检查。 |
| DLL 加载但第二回声没有开放 | 核对 `runtime-unlock` 和 `installed two targeted read overrides`，然后在正确的第二回声入口测试。 |
| 首次启动很慢 | MelonLoader 可能正在生成 IL2CPP 程序集；先让它完成，再根据日志判断。 |

排查时可向作者提供**相关日志片段**，但分享前先检查并遮去个人用户名、磁盘路径、账号及其他 Mod 的私人配置。说明游戏 Build、MelonLoader 版本、Mod ZIP 版本和具体卡在哪一步，比只说“没用”更容易定位。

<a id="install-uninstall"></a>

### 卸载与更新

- **仅卸载本 Mod：**退出游戏，删除或移出 `Mods\CrucibleUnlock.dll`。其他 Mod 和加载器可以保留。配置中的 `[CrucibleUnlock]` 可以保留，不会让已移除的 DLL 生效。
- **更新本 Mod：**退出游戏，备份旧 DLL，再把新发布包中的 DLL 放到相同位置；查看新版本说明是否要求调整配置。不要在运行中替换 DLL。
- **卸载 MelonLoader：**若还装有其他依赖它的 Mod，先处理那些 Mod。具体步骤请按 [MelonLoader 官方卸载说明](https://github.com/LavaGang/MelonLoader#un-install)操作；不要为了卸载本 Mod 而直接删除整个 `Mods`、`UserData` 或存档目录。

### 独立安装指南

- **[英文简版安装说明](INSTALL.md)：** 安装、配置、验证与卸载
- **[简体中文详细安装教程](INSTALL.zh-CN.md)：** Windows／Steam 完整步骤、ZIP 放置、校验与故障排查
- **[独立中文版 HTML 教程](INSTALL.zh-CN.html)：** 保存后可在本地浏览器中打开
- **[四步安装 HTML](INSTALL-QUICK.zh-CN.html)：** 只保留下载、安装与配置的关键步骤

<a id="install-agent"></a>

### 让 AI Agent 帮你安装

在装有 Steam、官方游戏和 AI Agent 客户端的 Windows 电脑上，可把 **[AI Agent 安装任务书](AGENT-INSTALL.zh-CN.md)** 交给 DSH、Codex、WorkBuddy 或豆包桌面端。不需要预装 Git、Python、.NET SDK 或 Mod 工具。Agent 负责安装与离线检查，游戏启动及游戏内验证由你亲自完成。

**交给 Agent 时的操作边界：**只从指定的 Nexus／MelonLoader 官方来源下载。游戏原文件、Steam 清单、存档和日志只读检查；存档只复制备份，保留原件不动。按任务书新增加载器、Mod DLL 和配置；发现同名文件、版本或哈希不符时先停止并报告，不直接覆盖，不修改游戏 DLL、metadata 或资源包。需要账号登录、验证码、付费或下载确认时，由你按任务书完成；不要提供密码或会话令牌。Agent 不启动游戏，也不发送游戏输入。

**让 Agent 交付这些实际结果：**

```text
Steam Build / 分支：
游戏根目录：
存档备份：路径，或“无现有存档”
MelonLoader：版本、官方来源、新增的关键文件；是否仍待运行验证
Nexus ZIP：实际文件名、来源、SHA-256
Mod DLL：实际安装路径、SHA-256
配置：三个键的读回值
离线结论：已就绪 / 停在何处及原因
待你亲自完成：通过 Steam 启动、到主菜单后退出、查看日志与游戏内第二回声
```

报告实际路径和校验结果即可，不要贴完整个人日志或账号信息。“文件装好”不等于“游戏内已验证”；首次运行后若配置被重写，退出游戏后重新读回核对。

**可直接复制的安装指令：**

```text
请帮我在这台 Windows 电脑上安装《恶意不息》（No Rest for the Wicked）的第二回声 Mod。先打开并完整阅读 GitHub 上的安装任务书：https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/main/AGENT-INSTALL.zh-CN.md ，然后按任务书实际执行下载、版本核对、存档备份、安装和离线复核，不要只总结教程。电脑目前只有 Steam、官方游戏、你这个 AI Agent 客户端，可能还有 Chrome；没有预装 Git、Python、.NET SDK 或 Mod 工具。只从任务书指定的官方来源取文件；如果网页需要我登录、过验证码或手动下载，请告诉我具体该做什么，完成后继续。不要替我启动或操作游戏；游戏内验证由我亲自完成。遇到游戏版本、文件哈希不符或将覆盖现有文件时，停止并报告。最后列出实际安装路径、校验结果及仍需我完成的步骤。
```

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

