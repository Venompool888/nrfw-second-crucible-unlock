# Second Crucible Unlock and Encounter Fixes

An unofficial MelonLoader mod for No Rest for the Wicked **Steam public Build 22928553**.

## Download

[Download the standalone CrucibleUnlock.dll (0.9.5)](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/CrucibleUnlock.dll). No extraction is needed.

## Install

1. Install MelonLoader 0.7.3.
2. Copy `CrucibleUnlock.dll` to `<game installation directory>/Mods/CrucibleUnlock.dll`.
3. In `UserData/MelonPreferences.cfg`, under `[CrucibleUnlock]`, set:

   ```ini
   mode = "runtime-unlock"
   guard_broken_warrick_music = true
   repair_warrick_phase2_target = true
   ```

The unlock is active only while the mod runs. Do not replace the DLL while the game is running. For the full guide, see the [English instructions](INSTALL.md) or [简体中文安装说明](INSTALL.zh-CN.md).

## Features

- Temporarily unlocks the second Crucible while the mod is loaded. It does not permanently complete a quest or directly edit a save.
- Repairs the second offering bowl sequence, floor blood route, room progression, Boss content selection, and Warrick's phase-two target.
- Adds configurable Boss trace rewards (default 300), raises the Second Crucible echo limit to 2000, and corrects the Broken Vow and Wallowing Husk Boss displays.

## Tested scope

One complete nine-floor run on 2026-10-05 matched the seven configured Boss encounters, checkpoint, and reward room. The second-bowl cut, blood drop, and floor blood route were visually checked in an earlier run. The 0.9.5 update passed offline checks; its final Wallowing Husk overhead health-bar change remains unconfirmed in play. Other game builds and repeated reload scenarios remain unverified. The build guard is intentionally exact; wait for an updated mod after a game update.

## Source

[GitHub repository](https://github.com/Venompool888/nrfw-second-crucible-unlock) — source, build instructions, version history, and code license.
