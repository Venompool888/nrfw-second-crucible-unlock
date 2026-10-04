# 第二回声 Mod：从下载 ZIP 到进入游戏的完整安装教程

这份教程写给第一次手动安装 Mod 的 Windows／Steam 玩家。对应的 Mod 是 **Second Crucible Unlock and Encounter Fixes（0.8.6）**，下载页在 [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108?tab=files)。源码、更新记录和简版英文说明在本仓库。

> **先看这句：**下载的 ZIP **不要放进游戏目录后就直接启动**，也不要把整个 ZIP 放进 `Mods`。要先解压，再让文件最终位于 `游戏安装目录\Mods\CrucibleUnlock.dll`。

## 你需要准备什么

- Windows 版 Steam 游戏《恶意不息》（No Rest for the Wicked）。本版只针对 **Steam public Build 22928553** 验证。游戏更新后，等 Mod 发布兼容版本；不要绕过版本检查。
- [Nexus Mods 文件页](https://www.nexusmods.com/norestforthewicked/mods/108?tab=files)的 **Main files → Crucible Unlock 0.8.6 → Manual download** 下载的 ZIP。
- 兼容的 **MelonLoader 0.7.x**。作者实测环境为 0.7.3 x64。到 [MelonLoader 官方发布页](https://github.com/LavaGang/MelonLoader/releases)或使用[官方安装器](https://github.com/LavaGang/MelonLoader.Installer)获取，不要从不明镜像站下载。
- 能解压 ZIP 的工具：Windows 文件资源管理器自带“全部解压”即可。

本 Mod 的 ZIP **不包含 MelonLoader 或游戏文件**。MelonLoader 是要先装好的加载器；Mod 自身的游戏内文件只有 `CrucibleUnlock.dll`。

## 第 0 步：退出游戏并备份存档

1. 正常退出游戏，确认 Steam 中不再显示“正在运行”。安装或更新过程中不要让游戏保持运行。
2. 按 `Win + R`，粘贴以下路径并按回车：

   ```text
   %USERPROFILE%\AppData\LocalLow\Moon Studios\NoRestForTheWicked
   ```

3. 把其中的 `DataStore` **复制**到桌面或另一个备份位置。复制完成后保留原目录原样，不要移动或剪切它。若你还使用 Steam 云存档，先等 Steam 同步完成。

备份是为了防范加载器、游戏更新或正常游玩时的意外情况；本 Mod 不会直接修改存档来永久完成第二回声，但游戏正常游玩仍会保存进度。

## 第 1 步：找到真正的游戏安装目录

在 Steam 库中右键《恶意不息》→**管理**→**浏览本地文件**。弹出的文件夹就是下文所说的“游戏安装目录”。不用猜盘符；每个人的 Steam 库位置可能不同。

请在这个文件夹中确认能看到 `NoRestForTheWicked.exe` 和 `NoRestForTheWicked_Data`。例如，游戏可能装在：

```text
D:\SteamLibrary\steamapps\common\NoRestForTheWicked\
```

这个路径只是**示例**，不是要求每个人都使用 D 盘。后面的 `Mods`、`MelonLoader` 和 `UserData` 都应直接位于你找到的游戏安装目录内。

## 第 2 步：安装 MelonLoader（已经装好可跳到第 3 步）

推荐使用 [MelonLoader 官方安装器](https://github.com/LavaGang/MelonLoader.Installer)：打开安装器，选择《恶意不息》；如果没有自动列出，就用安装器的 **Add Game Manually** 指向游戏。选择兼容的 **0.7.x** 版本并安装。官方安装器的游戏选择和安装方式见其[说明](https://github.com/LavaGang/MelonLoader.Installer#melonloader-installation)。

安装完成后，在游戏安装目录检查是否出现 `MelonLoader` 文件夹。本次实测的 MelonLoader 0.7.3 安装还在游戏主程序同层生成了 `version.dll`；不同安装方式的文件清单可能不同，**不要以是否存在 `dobby.dll` 判断安装成败**。最终以启动日志出现 `MelonLoader v0.7.x` 且加载器正常运行来确认。

首次安装加载器时，通过 Steam 启动一次游戏，让它生成 IL2CPP 互操作程序集与配置文件；这次启动可能比平常久。进入主菜单后正常退出，再继续下一步。如果加载器没有成功启动，先核对安装位置并查看日志，再继续安装 Mod。不要在游戏运行时复制或替换 Mod DLL。

## 第 3 步：把 N 网 ZIP 解压到正确位置

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

4. 打开其中的 `Mods` 文件夹，**复制** `CrucibleUnlock.dll`。
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

## 第 4 步：设置三个必填选项

打开游戏安装目录下的 `UserData\MelonPreferences.cfg`。可以右键文件→**打开方式**→**记事本**。如果第 2 步已经确认 MelonLoader 正常启动，但仍没有 `UserData` 文件夹，就先在游戏安装目录新建 `UserData`；如果只有配置文件不存在，就在该文件夹中新建纯文本文件并改名为 `MelonPreferences.cfg`。在资源管理器中先打开“显示文件扩展名”，避免得到 `MelonPreferences.cfg.txt`。

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

## 第 5 步：启动并确认加载成功

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

这些文字说明 DLL、配置、游戏版本检查和临时解锁补丁已加载。只看到“已加载”，但没有 `build guard passed` 或两个读取覆盖的记录，**还不能算安装成功**。若日志里有其他内容或顺序不同，以这些关键项是否出现为准。

接着由你在游戏中去第二回声的献祭盆测试。这个解锁仅在 Mod 正常加载期间生效；它不会把对应任务步骤永久写成完成。

## 可选：核对 ZIP 是否为本次 0.8.6 发布包

本次 N 网主文件的 ZIP SHA-256 为：

```text
F3733D6DF54D4E515D5810DC3B0B6657D86FB1850430493FF62ECAB45C9E22CF
```

如果要核对，在“下载”文件夹的空白处打开 PowerShell，输入 `Get-FileHash`，后面加上你下载 ZIP 的完整路径，例如：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\你的用户名\Downloads\CrucibleUnlock-0.8.6-boss-order.zip'
```

浏览器保存的文件名可能带有 N 网自动添加的文字或编号；**以你实际下载的文件名替换示例**。只有同一发布包才应与上面哈希相同；未来更新的 ZIP 会不同。

## 装不上时按现象排查

| 现象 | 先检查什么 |
|---|---|
| 游戏安装目录没有 `Mods` 或 `MelonLoader` | 先按第 2 步安装 MelonLoader，并确认指向了包含游戏 `.exe` 的目录。 |
| `Mods` 里只有 ZIP，看不到 `CrucibleUnlock.dll` | ZIP 还没有按第 3 步解压和复制。 |
| 日志没有 `Crucible Unlock 0.8.6-boss-order 已加载` | 检查 DLL 的完整路径、加载器是否启动、是否拿错了旧版压缩包。 |
| 日志显示 `mode = probe` | 配置没有保存到正确的 `UserData\MelonPreferences.cfg`，或存在重复的 `[CrucibleUnlock]`；核对文件扩展名。 |
| 日志中两个修复选项为 `False` | 按第 4 步在同一 `[CrucibleUnlock]` 区块下显式写 `true`，保存后完全退出并重启游戏。 |
| 日志里版本守卫失败，或没有 `build guard passed` | 确认 Steam public Build 是否为 **22928553**；游戏更新后等待适配版。不要绕过检查。 |
| DLL 加载但第二回声没有开放 | 核对 `runtime-unlock` 和 `installed two targeted read overrides`，然后在正确的第二回声入口测试。 |
| 首次启动很慢 | MelonLoader 可能正在生成 IL2CPP 程序集；先让它完成，再根据日志判断。 |

排查时可向作者提供**相关日志片段**，但分享前先检查并遮去个人用户名、磁盘路径及其他 Mod 的私人配置。说明游戏 Build、MelonLoader 版本、Mod ZIP 版本和具体卡在哪一步，比只说“没用”更容易定位。

## 卸载与更新

- **仅卸载本 Mod：**退出游戏，删除或移出 `Mods\CrucibleUnlock.dll`。其他 Mod 和加载器可以保留。配置中的 `[CrucibleUnlock]` 可以保留，不会让已移除的 DLL 生效。
- **更新本 Mod：**退出游戏，备份旧 DLL，再把新发布包中的 DLL 放到相同位置；查看新版本说明是否要求调整配置。不要在运行中替换 DLL。
- **卸载 MelonLoader：**若还装有其他依赖它的 Mod，先处理那些 Mod。具体步骤请按 [MelonLoader 官方卸载说明](https://github.com/LavaGang/MelonLoader#un-install)操作；不要为了卸载本 Mod 而直接删除整个 `Mods` 或 `UserData` 文件夹。

## 相关链接

- [Nexus Mods 玩家下载页](https://www.nexusmods.com/norestforthewicked/mods/108?tab=files)
- [简版英文安装说明](INSTALL.md)
- [版本记录](CHANGELOG.md)
- [MelonLoader 官方项目](https://github.com/LavaGang/MelonLoader)
