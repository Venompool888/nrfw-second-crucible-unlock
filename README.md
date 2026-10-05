# Crucible Unlock for No Rest for the Wicked

[![阅读简体中文说明](media/language-zh-CN.svg)](README.zh-CN.md)

Unlock the **second Echo / second Crucible**, with encounter fixes and multiplayer support.

![Second Crucible: unlock and encounter fixes, Build 22928553](media/header.png)

**[Download on Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108)** · [Installation guide](INSTALL.md) · [Watch gameplay](#gameplay-demonstration)

Version **`0.8.6-boss-order`** · Tested on **Steam public Build 22928553** · Unofficial community mod

## Contents

- [Features](#features)
- [Multiplayer and revival](#multiplayer-and-revival)
- [Gameplay demonstration](#gameplay-demonstration)
- [Download and installation](#download-and-installation)
- [Compatibility and safety](#compatibility-and-safety)
- [Technical details and building](#technical-details-and-building)
- [License](#license)

## Features

- **Access the second Crucible.** The unlock is active only while the mod is loaded. It does not permanently complete a quest or directly edit a save.
- **Play the existing encounter sequence.** The mod reuses the game's encounters and repairs the second offering bowl, floor blood route, room progression, Boss content selection, and Warrick's second phase.
- **Play in multiplayer.** When the surviving team members defeat a floor's Boss, teammates who died during that Boss fight can revive and rejoin.

## Multiplayer and revival

Supports multiplayer. When the surviving team members defeat a floor's Boss, teammates who died during that Boss fight revive with **50% of their own maximum HP**.

## Gameplay demonstration

See the mod in action:

**[▶ Watch on YouTube](https://www.youtube.com/watch?v=Rw-hCcjvWss)** · **[▶ Watch on Bilibili](https://www.bilibili.com/video/BV1zbHH6EEGx)**

## Download and installation

Get the player archive from **[Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108)**. Use the released Mod ZIP, not GitHub's source-code ZIP.

> [!IMPORTANT]
> Back up your realm and character saves before installing a loader or mod. This version is verified only on **Steam public Build 22928553**. If the game updates, wait for a compatible release; do not bypass the build guard.

1. Install compatible **[MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases)** from its official releases, then let it generate the game's IL2CPP assemblies.
2. Close the game. Extract the Mod ZIP and copy `CrucibleUnlock.dll` to `<game directory>/Mods/CrucibleUnlock.dll`.
3. In `UserData/MelonPreferences.cfg`, set the following values in a single `[CrucibleUnlock]` section. Keep the other settings already in the file.

   ```ini
   [CrucibleUnlock]
   mode = "runtime-unlock"
   guard_broken_warrick_music = true
   repair_warrick_phase2_target = true
   ```

   Both repair options default to `false` when absent. Enable them explicitly for the tested encounter behavior.
4. Launch through Steam. Check `MelonLoader/Latest.log` for a passed build guard, both targeted read overrides, and both repair options showing `True`.

### Installation guides

- **[English player guide](INSTALL.md):** installation, configuration, verification, and removal
- **[Detailed Simplified Chinese guide](INSTALL.zh-CN.md):** Windows and Steam walkthrough, ZIP placement, checks, and troubleshooting
- **[Standalone Chinese HTML guide](INSTALL.zh-CN.html):** save it and open it locally in a browser

### Install with an AI agent

On a Windows PC with Steam, the official game, and an AI agent client, you can use the **[AI agent installation brief (Simplified Chinese)](AGENT-INSTALL.zh-CN.md)** with DSH, Codex, WorkBuddy, or Doubao desktop. No preinstalled Git, Python, .NET SDK, or Mod tools are required. The agent handles installation and offline checks; you launch and verify the game yourself.

<details>
<summary><strong>Copy a ready-to-use installation prompt</strong></summary>

```text
Please help me install the second Echo Mod for No Rest for the Wicked on this Windows PC. First open and read the full GitHub installation brief: https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/main/AGENT-INSTALL.zh-CN.md . Then actually carry out the download, version check, save backup, installation, and offline verification described there; do not just summarize the guide. This PC currently has only Steam, the official game, your AI agent client, and possibly Chrome; Git, Python, the .NET SDK, and Mod tools are not preinstalled. Obtain files only from the official sources specified in the brief. If a page requires me to sign in, solve a CAPTCHA, or download manually, tell me exactly what to do and continue afterward. Do not launch or operate the game for me; I will perform in-game verification myself. Stop and report if the game version or file hashes do not match, or if existing files would be overwritten. Finish by listing the actual installation paths, verification results, and steps I still need to complete.
```

</details>

## Compatibility and safety

- **Tested scope:** one full nine-floor run on 2026-10-05 on Steam public **Build 22928553** showed the seven expected Boss encounters, checkpoint, reward room, and return to the second bowl. This is one tested run on one build, not a compatibility guarantee for later builds. Other game builds and repeated reload scenarios remain unverified. See the [version history](CHANGELOG.md).
- **Save behavior:** the unlock exists only while the mod runs. Normal play can still save progress; the mod does not write permanent quest completion.
- **Updates and removal:** never replace the DLL while the game is running. To uninstall, close the game and remove `Mods/CrucibleUnlock.dll`; remove MelonLoader separately if desired. See the [player guide](INSTALL.md).
- **Old builds:** do not use the old `0.4.0` archive or its persistent-unlock instructions from the research workspace.

## Technical details and building

The source under [`src/`](src/) is the frozen **`0.8.6-boss-order`** source snapshot.

- The mod checks the **exact supported game build** before installing its runtime hooks.
- Building requires MelonLoader references and game-specific generated IL2CPP interop assemblies from the supported game.
- The game, MelonLoader binaries, and generated interop assemblies are **not included**.

See **[BUILD.md](BUILD.md)** for local requirements, build commands, tests, and packaging.

## License

The code is available under **[MIT](LICENSE-CODE.md)**. The six image data files under `src/CrucibleUnlock/Assets/SecondBowlBlood/` are outside that code license; see the license file for its full scope.

This is an **unofficial community project**.
