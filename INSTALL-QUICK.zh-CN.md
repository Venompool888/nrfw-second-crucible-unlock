# Second Crucible（第二试炼）Mod｜四步安装

[English](INSTALL-QUICK.md)

Windows Steam 玩家用这个版本即可。准备好官方游戏、浏览器和文件资源管理器。

适用：**Steam public Build 22928553 · Mod 0.9.16**

## 1 · 下载 MelonLoader

点击下载 [MelonLoader 官方 0.7.3 安装器](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.Installer.exe)。

## 2 · 安装加载器

打开刚下载的安装器。它通常会列出电脑中检测到的 Steam 游戏：找到 **No Rest for the Wicked／恶意不息**，选择 **0.7.3**，点击 **Install**。

如果列表中没有游戏，点击 **Add Game Manually**，手动选中游戏安装目录里的 `NoRestForTheWicked.exe`，然后安装。

## 3 · 下载 Mod

从 GitHub [直接下载 CrucibleUnlock.dll（0.9.16）](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.16/CrucibleUnlock.dll)及[NRFWBowgunAudioSync.dll（0.3.3）](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.16/NRFWBowgunAudioSync.dll)。后者用于弩枪键鼠输入和音效；两个文件都无需解压。

## 4 · 放入两个 DLL，写好配置

1. 在 Steam 库中右键游戏 → **管理** → **浏览本地文件**。打开游戏安装目录下的 `Mods` 文件夹；没有就新建。把下载的两个 DLL 复制进去，最终位置应分别是 `游戏安装目录\Mods\CrucibleUnlock.dll` 和 `游戏安装目录\Mods\NRFWBowgunAudioSync.dll`
2. 用记事本打开 `游戏安装目录\UserData\MelonPreferences.cfg`。如果文件或文件夹不存在，就在游戏安装目录中新建。请打开 Windows 的“显示文件扩展名”，确认文件不是 `MelonPreferences.cfg.txt`
3. 搜索 `[CrucibleUnlock]`。已有这个区块就修改其中的值；没有就把下面整段加到文件末尾。保留其他 Mod 的配置，且不要建立第二个 `[CrucibleUnlock]` 区块

```ini
[CrucibleUnlock]
mode = "runtime-unlock"
guard_broken_warrick_music = true
repair_warrick_phase2_target = true
```

保存并关闭配置文件。已有强化弩枪时，副手装备后使用当前举盾绑定发射（Xbox LB 或键鼠举盾键）。如果以前改过配置，请保持 `bowgun_weapon_class = 30`；PS 具体型号尚未专项实测。

**或者直接用现成的配置文件。你是哪种情况？**

- **全新安装**——还没有 `UserData\MelonPreferences.cfg`：下载 [`MelonPreferences.cfg`](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.16/MelonPreferences.cfg)，放进 `游戏安装目录\UserData\`，上面手写那几步可以跳过；其余选项 MelonLoader 首次启动时会自己补上。
- **已经装了别的 Mod**——文件已存在：**不要覆盖它。** 所有 Mod 的设置都在这同一个文件里，覆盖等于把别人的设置一起删掉。请只把上面那段 `[CrucibleUnlock]` 合并进你自己的文件；想保险就先备份一份。

**完成。现在由你本人通过 Steam 启动游戏，前往试炼中心的第二个血盆体验第二试炼。**

如果游戏版本不是 Build 22928553，或安装后无法正常使用，请联系作者。

[返回项目首页](README.zh-CN.md)
