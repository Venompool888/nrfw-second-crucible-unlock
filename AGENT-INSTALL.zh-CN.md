# 第二回声 Mod：给 AI Agent 的干净电脑安装任务书

把**整个文件**交给 DSH、Codex、WorkBuddy、豆包桌面端等能访问本机文件和网页的 AI Agent，再说一句：“请按这份任务书帮我安装第二回声 Mod。”本文件适用于一台仅装有 Windows、Steam、官方《恶意不息》（No Rest for the Wicked）、AI Agent 客户端和可选 Chrome 的电脑。**不需要预装 Git、Python、.NET SDK、解压软件或本项目源码。**

目标是把已发布的 **Second Crucible Unlock and Encounter Fixes 0.9.5** 安装到正确位置并完成离线检查。它只针对 **Steam public Build 22928553** 验证；第二回声解锁只在 Mod 加载期间有效，并非永久改写存档。直接从 [GitHub Release](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/CrucibleUnlock.dll) 下载单独 DLL；MelonLoader 从[官方 v0.7.3 发布页](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3)获取。手动操作细节见[玩家安装教程](INSTALL.zh-CN.md)。

## 直接交给 Agent 的指令

> 你正在协助我在一台干净的 Windows 电脑上安装《恶意不息》的第二回声 Mod。请把本文件当成工作清单，实际执行能执行的步骤，不要只复述教程。可以使用浏览器、文件管理器和 Windows 自带 PowerShell；如当前客户端不能操作某一步，请具体告诉我需要点击什么，完成后继续。
>
> **操作边界：**不要启动 Steam 中的游戏、游戏程序或游戏启动器，不要发送游戏输入或替我游玩；我会亲自启动和验证。只读检查游戏原文件、Steam 清单、存档和日志；不要修改、覆盖、删除或移动游戏原文件及存档。仅可按本文新增 MelonLoader、Mod DLL 与 Mod 配置。若目标位置已存在同名加载器或 Mod 文件，先识别现状并停下说明，不要直接覆盖。不要修改游戏 DLL、metadata、资源包，不要下载旧版 Mod、绕过版本守卫或使用第三方镜像。账号登录、验证码、付费、浏览器下载确认等需我操作时，请停在该步骤让我完成；不要索取密码或会话令牌。
>
> 先定位实际 Steam 游戏目录和 `appmanifest_1371980.acf`，确认 `NoRestForTheWicked.exe`、`NoRestForTheWicked_Data` 与 `buildid=22928553`。如果游戏版本不符，报告版本并停止安装。确认游戏已关闭、Steam 下载和云同步已结束；若已有存档，将 `DataStore` **复制**到游戏目录外的带时间戳备份目录，保留源文件原样。不存在存档时记录“尚无存档”，继续。
>
> 从官方 GitHub 发布页获取 **MelonLoader v0.7.3 Windows x64**，核对来源和压缩包内容，安装到游戏根目录。只添加新文件；出现与现有文件冲突就停下报告。不要用“安装器已打开”当成安装成功。此阶段不要启动游戏；首次生成 IL2CPP 程序集和运行日志由我稍后亲自启动完成。
>
> 从 [GitHub Release](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/CrucibleUnlock.dll) 直接下载 `CrucibleUnlock.dll`。不要下载自动生成的源码压缩包，也不要从搜索结果、镜像站或历史研究目录找 DLL。核对 DLL 的 SHA-256 为 `EB2753BAE81B22BA2F543B992116F0EE36DF0CD69F06374E8010847BC7F033C0`；不符就停止，不安装。
>
> 在游戏根目录创建 `Mods`（若尚不存在），只把已校验的 DLL 复制到 `Mods/CrucibleUnlock.dll`。在 `UserData/MelonPreferences.cfg` 的**唯一** `[CrucibleUnlock]` 区块写入下面三项，保留文件中其他配置。若该文件不存在，可以新建 UTF-8 纯文本文件，不要产生 `.txt` 后缀。写完读回检查。
>
> ```ini
> [CrucibleUnlock]
> mode = "runtime-unlock"
> guard_broken_warrick_music = true
> repair_warrick_phase2_target = true
> ```
>
> 最后离线复核：Steam 清单版本、游戏根目录、加载器新增文件、DLL 路径与哈希、配置三项、存档备份位置。报告每项结果与尚待我亲自启动后确认的项目。不要把“文件装好”写成“游戏内已验证”。

## Agent 执行时的具体判断

1. **定位游戏。**优先让用户从 Steam 库对游戏点“管理 → 浏览本地文件”，或只读查看 Steam 库的 `steamapps\appmanifest_1371980.acf`。不要把作者电脑的 `D:` 路径套到用户电脑。清单中的 `installdir` 以及实际存在的主程序共同决定路径。若有多个 Steam 库、多个同名目录，先核对清单再选，不要猜。
2. **版本门槛。**只读查看清单的 `buildid`，确认是 `22928553` 且是 `public` 分支。可以进一步只读核对游戏 `GameAssembly.dll` 的 SHA-256 为 `5B00EE90833B1BE2EA73E01CB83E710E09E86199D12AC769BE3C0E82ADD8B4BB`，以及 `NoRestForTheWicked_Data\il2cpp_data\Metadata\global-metadata.dat` 为 `4C8DFE07E5F5178F8EEFD3D079412EC164EBA5DA798EFD2A2AE03AA37DC79502`。任一不符，停止；不要降级游戏或改守卫。
3. **备份。**存档通常在 `%USERPROFILE%\AppData\LocalLow\Moon Studios\NoRestForTheWicked\DataStore`。只复制已有 `DataStore` 到游戏目录外，建议桌面上的 `NRFW-save-backup-YYYYMMDD-HHMMSS`；源目录保持原样。若这台电脑从未运行游戏，可能尚无该目录。
4. **加载器。**官方 v0.7.3 发布页有 Windows x64 手动压缩包。先检查压缩包的顶层路径、游戏根目录现有文件及任何重名目标，再解压到游戏根目录。只新增文件；不能确定是否会覆盖时先停下。Windows 自带“全部解压”或 PowerShell `Expand-Archive` 即可，不用安装额外解压软件。不要把加载器解压到 `Mods` 里。因本任务禁止 Agent 启动游戏，此时只能确认静态文件位置，不能声称加载器已运行。
5. **下载 Mod。**从 GitHub Release 直接下载单独 DLL。核对上文 DLL 哈希；不符就停止，不擅自改用其它包。
6. **配置。**这三个键必须在同一个 `[CrucibleUnlock]` 区块；两个 Boss 修复选项省略时缺省为 `false`。如果 MelonLoader 首次运行后重写配置，需在用户退出游戏后重新读回并核对。不要插入第二个同名区块。

可用 Windows 自带 PowerShell 只读计算哈希，例如 `Get-FileHash -Algorithm SHA256 -LiteralPath '实际完整路径'`。`实际完整路径` 必须由 Agent 在本机查到，不能照抄示例或作者的路径。

## 交付格式与用户验收

Agent 完成离线安装后，请按以下格式交付，路径填写本机实际绝对路径，不贴完整个人日志或账号信息：

```text
Steam Build / 分支：
游戏根目录：
存档备份：路径，或“无现有存档”
MelonLoader：版本、官方来源、已新增的关键文件；未做运行验证
Mod DLL：来源、安装路径、SHA-256
Mod DLL：安装路径、SHA-256
配置：三个键的读回值
离线结论：已就绪 / 停在何处及原因
待用户亲自完成：通过 Steam 启动、到主菜单后退出、查看日志与游戏内第二回声
```

用户本人首次启动后，可把 `<游戏根目录>\MelonLoader\Latest.log` 中的相关片段交给 Agent **只读**核对。预期包括 `Crucible Unlock 0.9.5-husk-hud-candidate 已加载`、`mode = runtime-unlock`、两个修复选项为 `True`、`[runtime-unlock] build guard passed` 和 `[runtime-unlock] installed two targeted read overrides`。日志缺失或版本守卫失败都不算验证通过。游戏内献祭、Boss 与层数体验由用户亲自操作和判断；首次生成 IL2CPP 程序集可能耗时较长。日志分享前遮去用户名、路径等个人信息。

如需卸载，只在游戏退出后处理本 Mod 新增的 `Mods\CrucibleUnlock.dll`；MelonLoader 的卸载按[官方说明](https://github.com/LavaGang/MelonLoader#un-install)单独进行。不要为卸载本 Mod 删除整个 `Mods`、`UserData` 或存档目录。
