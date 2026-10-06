# Second Crucible Unlock and Encounter Fixes

An unofficial MelonLoader mod for No Rest for the Wicked **Steam public Build 22928553**.

Gameplay demonstration: [Bilibili](https://www.bilibili.com/video/BV1zbHH6EEGx) · [YouTube](https://www.youtube.com/watch?v=Rw-hCcjvWss)

## What it does

- Temporarily unlocks the second Crucible while the mod is loaded. It does not permanently complete a quest or directly edit a save.
- Repairs the second offering bowl sequence and floor blood route.
- Repairs room progression and Boss content selection, plus Warrick's phase-two target.
- Adds configurable Boss trace rewards (default 300), raises the Second Crucible echo limit to 2000, and corrects the Broken Vow and Wallowing Husk Boss displays.

## Installation

Detailed Chinese installation guide (ZIP placement, configuration, verification, troubleshooting): [安装教程（简体中文）](https://github.com/Venompool888/nrfw-second-crucible-unlock/blob/v0.9.5/INSTALL.zh-CN.md)

1. Back up your realm and character saves.
2. Install compatible [MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases) and launch the game once to generate IL2CPP assemblies. Close the game.
3. Extract the ZIP, then copy its `Mods/CrucibleUnlock.dll` into the game's `Mods` folder. The final path is `<game directory>/Mods/CrucibleUnlock.dll`.
4. In `UserData/MelonPreferences.cfg`, under `[CrucibleUnlock]`, set `mode = "runtime-unlock"`, `guard_broken_warrick_music = true`, and `repair_warrick_phase2_target = true`. Enter both Boss-related options explicitly; their defaults are false.
5. Launch through Steam. Check `MelonLoader/Latest.log` for a passed build guard, both targeted read overrides, and both Boss-related options showing `True`.

The unlock is active only while the mod runs. To remove it, close the game and remove `Mods/CrucibleUnlock.dll`. Do not replace the DLL while the game is running.

## Tested scope

One complete nine-floor run on 2026-10-05 matched the seven configured Boss encounters, checkpoint, and reward room. The second-bowl cut, blood drop, and floor blood route were visually checked in an earlier run. Other game builds and repeated reload scenarios remain unverified. The build guard is intentionally exact; wait for an updated mod after a game update.

The 0.9.5 update passed offline checks. Its final Wallowing Husk overhead health-bar change remains unconfirmed in play. Direct [ZIP](https://github.com/Venompool888/nrfw-second-crucible-unlock/raw/refs/tags/v0.9.5/download/0.9.5/CrucibleUnlock-0.9.5.zip) and [DLL](https://github.com/Venompool888/nrfw-second-crucible-unlock/raw/refs/tags/v0.9.5/download/0.9.5/Mods/CrucibleUnlock.dll) downloads are also available on GitHub.

## Source

[GitHub repository](https://github.com/Venompool888/nrfw-second-crucible-unlock) — source, build instructions, version history, and code license.
