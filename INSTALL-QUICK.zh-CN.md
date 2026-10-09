# Second Crucible（第二试炼）Mod｜四步安装

[English](INSTALL-QUICK.md)

适用于 Windows Steam 玩家。准备好官方游戏、浏览器和文件资源管理器。

适用：**Steam public Build 22928553 · Mod 0.9.17 预发布候选版**。本候选仅完成离线核验，尚未实机测试；0.9.16 仍是最新稳定版。

## 1 · 下载 MelonLoader

下载 [MelonLoader 官方 0.7.3 安装器](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.Installer.exe)。

## 2 · 安装加载器

打开安装器，找到 **No Rest for the Wicked／恶意不息**，选择 **0.7.3** 并点击 **Install**。如果列表中没有游戏，点击 **Add Game Manually**，选择游戏安装目录里的 `NoRestForTheWicked.exe`。

## 3 · 下载两个 Mod DLL

从 GitHub 下载 [CrucibleUnlock.dll（0.9.17）](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/CrucibleUnlock.dll)和 [NRFWBowgunAudioSync.dll（0.3.3）](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/NRFWBowgunAudioSync.dll)，无需解压。完整弩枪输入和音效仍依赖独立的音频／输入组件。

## 4 · 把两个 DLL 放进 Mods

在 Steam 库中右键游戏 → **管理** → **浏览本地文件**。打开游戏安装目录下的 `Mods` 文件夹；没有就新建。把两个 DLL 复制进去，最终位置应分别为 `游戏安装目录\Mods\CrucibleUnlock.dll` 和 `游戏安装目录\Mods\NRFWBowgunAudioSync.dll`。

加载器首次启动时会自动创建 `UserData\MelonPreferences.cfg`。Mod 默认启用已验收的运行行为和修复，全新安装无需修改配置。只有想调整 Boss 痕迹数量时，才编辑现有 `[CrucibleUnlock]` 区块中的 `boss_trace_drop_amount`。允许范围为 0–1000，默认 300，0 表示目标 Boss 不掉落痕迹。请在游戏退出时编辑，并保留其他 Mod 的配置区块。旧 `mode`、修复开关和 `bowgun_weapon_class` 均会忽略；弩枪类别 30 为内置默认。详见[完整设置说明](src/CrucibleUnlock/PLAYER-CONFIG-0.9.17.md)。

可选的 [`MelonPreferences.cfg` 示例](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/MelonPreferences.cfg)只包含 Crucible Unlock 设置，并非必需。不要用它覆盖已经存在的共享配置文件。

**完成。现在由你本人通过 Steam 启动游戏，前往试炼中心的第二个血盆体验第二试炼。** 0.9.17 候选尚未实机测试；如果你需要已测试版本，请使用 0.9.16 稳定版。

如果游戏版本不是 Build 22928553，或安装后无法正常使用，请联系作者。

[返回项目首页](README.zh-CN.md)
