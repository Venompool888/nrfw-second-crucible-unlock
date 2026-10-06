# Second Crucible（第二试炼）Mod｜四步安装

[English](INSTALL-QUICK.md) · [HTML 文件](INSTALL-QUICK.zh-CN.html) · [下载 HTML](https://github.com/Venompool888/nrfw-second-crucible-unlock/raw/refs/heads/main/INSTALL-QUICK.zh-CN.html)

Windows Steam 玩家用这个版本即可。准备好官方游戏、浏览器和文件资源管理器。

适用：**Steam public Build 22928553 · Mod 0.9.5**

## 1 · 下载 MelonLoader

点击下载 [MelonLoader 官方 0.7.3 安装器](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.Installer.exe)。

## 2 · 安装加载器

打开刚下载的安装器。它通常会列出电脑中检测到的 Steam 游戏：找到 **No Rest for the Wicked／恶意不息**，选择 **0.7.3**，点击 **Install**。

如果列表中没有游戏，点击 **Add Game Manually**，手动选中游戏安装目录里的 `NoRestForTheWicked.exe`，然后安装。

## 3 · 下载 Mod

从 GitHub [直接下载 0.9.5 ZIP](download/0.9.5/CrucibleUnlock-0.9.5.zip)，也可以在 [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108?tab=files) 下载同版本。ZIP 内含 `Mods/CrucibleUnlock.dll`。

## 4 · 放入 DLL，写好配置

1. 右键 ZIP，选择**全部解压**，找到里面的 `Mods\CrucibleUnlock.dll`
2. 在 Steam 库中右键游戏 → **管理** → **浏览本地文件**。打开游戏安装目录下的 `Mods` 文件夹；没有就新建。把 `CrucibleUnlock.dll` 复制进去，最终位置应是 `游戏安装目录\Mods\CrucibleUnlock.dll`
3. 用记事本打开 `游戏安装目录\UserData\MelonPreferences.cfg`。如果文件或文件夹不存在，就在游戏安装目录中新建。请打开 Windows 的“显示文件扩展名”，确认文件不是 `MelonPreferences.cfg.txt`
4. 搜索 `[CrucibleUnlock]`。已有这个区块就修改其中的值；没有就把下面整段加到文件末尾。保留其他 Mod 的配置，且不要建立第二个 `[CrucibleUnlock]` 区块

```ini
[CrucibleUnlock]
mode = "runtime-unlock"
guard_broken_warrick_music = true
repair_warrick_phase2_target = true
```

保存并关闭配置文件。

**完成。现在由你本人通过 Steam 启动游戏，前往试炼中心的第二个血盆体验第二试炼。**

如果游戏版本不是 Build 22928553，或安装后无法正常使用，请查看[详细安装与排错教程](INSTALL-DETAILED.zh-CN.md)或联系作者。HTML 版可下载后在浏览器打开。

[返回项目首页](README.zh-CN.md)
