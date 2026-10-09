# Second Crucible for No Rest for the Wicked

[![阅读简体中文说明](media/language-zh-CN.svg)](README.zh-CN.md)

[![简体中文快速安装](media/install-quick-zh-CN.svg)](INSTALL-QUICK.zh-CN.md)
[![English Quick Install](media/install-quick-en.svg)](INSTALL-QUICK.md)

Unlock the **Second Crucible**, with encounter fixes and multiplayer support.

![Second Crucible: unlock and encounter fixes, Build 22928553](media/header.png)

**[Download 0.9.17 prerelease core](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/CrucibleUnlock.dll)** · **[Download bowgun audio/input 0.3.3](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/NRFWBowgunAudioSync.dll)** · [0.9.17 quick install](INSTALL-QUICK.md) · [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108)

Latest stable release: **`0.9.16`** · Candidate prerelease: **`0.9.17`** (offline-verified; not play-tested) · Supported game: **Steam public Build 22928553** · Unofficial community mod

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
- **Collect Boss traces and choose more echoes.** Eligible Second Crucible Bosses award 300 traces by default, and the mod raises the active hero's echo limit to 2000.
- **See corrected Boss presentation.** The Broken Vow and Wallowing Husk encounters receive scoped HUD fixes, including the Husk name in the game's 12 supported languages.

- **Use an equipped Reinforced Bowgun.** Fire with the game's current block action: Xbox LB or your keyboard/mouse block binding. The extra audio/input DLL supplies keyboard routing and synchronizes crossbow audio with projectile release. This release does not grant the weapon.

## Multiplayer and revival

Supports multiplayer. When the surviving team members defeat a floor's Boss, teammates who died during that Boss fight revive with **50% of their own maximum HP**.

## Gameplay demonstration

See the mod in action:

**[▶ Watch on YouTube](https://www.youtube.com/watch?v=Rw-hCcjvWss)** · **[▶ Watch on Bilibili](https://www.bilibili.com/video/BV1zbHH6EEGx)**

## Download and installation

For the 0.9.17 prerelease, download MelonLoader 0.7.3 and both DLLs, then place them in `Mods`. The core enables the accepted runtime behavior by default; no preference-file setup is needed. To change Boss trace quantity, edit only `boss_trace_drop_amount` in the existing `[CrucibleUnlock]` section. Keep the 0.9.16 stable release if you need a play-tested version.

[![简体中文快速安装](media/install-quick-zh-CN.svg)](INSTALL-QUICK.zh-CN.md)
[![English Quick Install](media/install-quick-en.svg)](INSTALL-QUICK.md)

**[Screenshot guide (0.9.16 stable)](https://venompool888.github.io/nrfw-second-crucible-unlock/en.html)** · [中文截图教程（0.9.16 稳定版）](https://venompool888.github.io/nrfw-second-crucible-unlock/)

## Compatibility and safety

- **Tested scope:** one full nine-floor run on 2026-10-05 on Steam public **Build 22928553** showed the seven expected Boss encounters, checkpoint, reward room, and return to the second bowl. This is one tested run on one build, not a compatibility guarantee for later builds. Other game builds and repeated reload scenarios remain unverified. See the [version history](CHANGELOG.md).
- **0.9.17 prerelease:** offline build and managed entry-point checks only; this candidate has not received in-game testing. Version 0.9.16 remains the latest stable release.
- **0.9.16 validation:** 24 bowgun shots were recorded in a local test (18 during keyboard/mouse input and 6 during Gamepad input), with no input-thread errors. Sixteen shots had independent audio/playback IDs matching the projectile-release frame. PS controller models and audible onset remain unverified. Two return/reuse cleanup paths have incomplete per-shot logging; no restoration errors were recorded. The earlier Husk overhead health-bar change has not received a new focused play test.
- **Save behavior:** the unlock exists only while the mod runs. Normal play can still save progress; the mod does not write permanent quest completion.
- **Updates and removal:** never replace the DLL while the game is running. To uninstall, close the game and remove `Mods/CrucibleUnlock.dll` and `Mods/NRFWBowgunAudioSync.dll`; remove MelonLoader separately if desired.
- **Old builds:** do not use the old `0.4.0` archive or its persistent-unlock instructions from the research workspace.

## Technical details and building

The source under [`src/`](src/) contains the **0.9.17 player-defaults prerelease candidate** and the unchanged **0.3.3** audio/input component. The core behavior changes are not play-tested; 0.9.16 remains the latest stable release.

- The mod checks the **exact supported game build** before installing its runtime hooks.
- Building requires MelonLoader references and game-specific generated IL2CPP interop assemblies from the supported game.
- The game, MelonLoader binaries, and generated interop assemblies are **not included**.

See **[BUILD.md](BUILD.md)** for local requirements, build commands, tests, and packaging.

## License

The code is available under **[MIT](LICENSE-CODE.md)**. The six image data files under `src/CrucibleUnlock/Assets/SecondBowlBlood/` are outside that code license; see the license file for its full scope.

This is an **unofficial community project**.
