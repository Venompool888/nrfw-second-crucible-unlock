# Crucible Unlock for No Rest for the Wicked

Source code for the **second Echo / second Crucible** mod, version `0.8.6-boss-order`.

## Gameplay demonstration

- [Watch on Bilibili](https://www.bilibili.com/video/BV1zbHH6EEGx)
- [Watch on YouTube](https://www.youtube.com/watch?v=Rw-hCcjvWss)

## What the mod does

- Temporarily unlocks the second Crucible while the mod is loaded. It does not permanently complete a quest or directly edit a save.
- Reuses the game's existing encounter sequence and supplies repairs for the second offering bowl, room progression, and Warrick's second phase.
- Checks the exact supported game build before installing its runtime hooks.

The mod was tested on the Steam public **Build 22928553**. A full run of the nine floors on 2026-10-05 showed the seven expected Boss encounters, checkpoint, reward room, and return to the second bowl. This is one tested run on one build, not a compatibility guarantee for later builds. See [version history](CHANGELOG.md).

## Player installation

Get the player archive from [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108) and follow [INSTALL.md](INSTALL.md). A detailed Windows and Steam guide is available in [简体中文](INSTALL.zh-CN.md). Do not use the old `0.4.0` archive or its persistent-unlock instructions from the research workspace.

The [standalone Chinese HTML guide](INSTALL.zh-CN.html) can be saved and opened locally in a browser.

### 让 AI Agent 帮你安装（简体中文）

在只有 Steam、官方游戏和 AI Agent 客户端的 Windows 电脑上，把下面整段直接复制给 DSH、Codex、WorkBuddy 或豆包桌面端。完整步骤见[第二回声 Mod 的 AI Agent 安装任务书](AGENT-INSTALL.zh-CN.md)。

```text
请帮我在这台 Windows 电脑上安装《恶意不息》（No Rest for the Wicked）的第二回声 Mod。先打开并完整阅读 GitHub 上的安装任务书：https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/main/AGENT-INSTALL.zh-CN.md ，然后按任务书实际执行下载、版本核对、存档备份、安装和离线复核，不要只总结教程。电脑目前只有 Steam、官方游戏、你这个 AI Agent 客户端，可能还有 Chrome；没有预装 Git、Python、.NET SDK 或 Mod 工具。只从任务书指定的官方来源取文件；如果网页需要我登录、过验证码或手动下载，请告诉我具体该做什么，完成后继续。不要替我启动或操作游戏；游戏内验证由我亲自完成。遇到游戏版本、文件哈希不符或将覆盖现有文件时，停止并报告。最后列出实际安装路径、校验结果及仍需我完成的步骤。
```

## Source and builds

The source under `src/` is the frozen `0.8.6-boss-order` source. Building it requires MelonLoader references and game-specific generated IL2CPP interop assemblies from the supported game. See [BUILD.md](BUILD.md). The game, MelonLoader binaries, and generated interop assemblies are not included.

The code is available under [MIT](LICENSE-CODE.md); the six image data files under `src/CrucibleUnlock/Assets/SecondBowlBlood/` are outside that code license.

This is an unofficial community project.
