# Second Crucible Mod | Four-step installation

[简体中文](INSTALL-QUICK.zh-CN.md)

For Windows Steam players. Have the official game, a browser, and File Explorer ready.

For: **Steam public Build 22928553 · Mod 0.9.17 stable release**. Offline checks passed, and the user reported no issues after trying 0.9.17 in-game on 2026-10-10.

## 1 · Download MelonLoader

Download the [official MelonLoader 0.7.3 installer](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.Installer.exe).

## 2 · Install the loader

Open the installer. Find **No Rest for the Wicked**, select **0.7.3**, and click **Install**. If the game is missing, use **Add Game Manually** and select `NoRestForTheWicked.exe` in the game installation directory.

## 3 · Download the two mod DLLs

Download [CrucibleUnlock.dll 0.9.17](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/CrucibleUnlock.dll) and [NRFWBowgunAudioSync.dll 0.3.3](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/NRFWBowgunAudioSync.dll). No extraction is needed. The audio/input component remains a separate dependency for the full bowgun input and sound behavior.

## 4 · Place both DLLs in Mods

In Steam, right-click the game → **Manage** → **Browse local files**. Open the `Mods` folder or create it if missing, then copy both DLLs into it. Their final locations must be `game installation directory\Mods\CrucibleUnlock.dll` and `game installation directory\Mods\NRFWBowgunAudioSync.dll`.

The loader creates `UserData\MelonPreferences.cfg` on first launch. The mod enables its accepted runtime behavior and repairs by default, so a fresh install needs no configuration edits. To change Boss trace quantity, edit only `boss_trace_drop_amount` in the existing `[CrucibleUnlock]` section. It accepts 0–1000, defaults to 300, and 0 means no target Boss traces drop. Edit while the game is closed. Preserve other mods' sections. Legacy `mode`, repair toggles, and `bowgun_weapon_class` are ignored; bowgun class 30 is built in. See [the full setting notes](src/CrucibleUnlock/PLAYER-CONFIG-0.9.17.md).

An optional [`MelonPreferences.cfg` example](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.17/MelonPreferences.cfg) contains only the Crucible Unlock setting. It is not required; never replace an existing shared preferences file with it.

**Done. Launch the game yourself through Steam, then go to the second blood bowl in the Crucible hub to play the Second Crucible.** The user reported no issues in an in-game trial of 0.9.17 on 2026-10-10; other game builds and untested paths are not covered by that feedback.

If your game is not Build 22928553, or the mod does not work after installation, contact the author.

[Back to the project](README.md)
