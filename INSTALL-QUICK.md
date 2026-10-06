# Second Crucible Mod | Four-step installation

[简体中文](INSTALL-QUICK.zh-CN.md)

For Windows Steam players. Have the official game, a browser, and File Explorer ready.

For: **Steam public Build 22928553 · Mod 0.9.5**

## 1 · Download MelonLoader

Download the [official MelonLoader 0.7.3 installer](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.Installer.exe).

## 2 · Install the loader

Open the installer you just downloaded. It usually lists the Steam games detected on your computer: find **No Rest for the Wicked**, select **0.7.3**, and click **Install**.

If the game is missing from the list, click **Add Game Manually**, select `NoRestForTheWicked.exe` in the game installation directory, and install.

## 3 · Download the mod

Download [CrucibleUnlock.dll 0.9.5 directly](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/CrucibleUnlock.dll) and place it at `<game directory>\Mods\CrucibleUnlock.dll`. No extraction is needed.

## 4 · Place the DLL and set the configuration

1. Download `CrucibleUnlock.dll` directly; no extraction is needed
2. In your Steam library, right-click the game → **Manage** → **Browse local files**. Open the `Mods` folder in the game installation directory, or create it if missing. Copy `CrucibleUnlock.dll` into it. The final location must be `game installation directory\Mods\CrucibleUnlock.dll`
3. Open `game installation directory\UserData\MelonPreferences.cfg` in Notepad. If the file or folder is missing, create it in the game installation directory. Turn on Windows **File name extensions** and make sure the file is not named `MelonPreferences.cfg.txt`
4. Search for `[CrucibleUnlock]`. If that section exists, change its values; otherwise, add the block below to the end of the file. Keep other mods' settings and do not create a second `[CrucibleUnlock]` section

```ini
[CrucibleUnlock]
mode = "runtime-unlock"
guard_broken_warrick_music = true
repair_warrick_phase2_target = true
```

Save and close the configuration file.

**Or use the ready-made configuration file.** Which case are you in?

- **Fresh install** — you do not have `UserData\MelonPreferences.cfg` yet: download [`MelonPreferences.cfg`](https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/MelonPreferences.cfg) and drop it into `game installation directory\UserData\`. You can skip the manual editing above; MelonLoader adds the remaining options by itself on first launch.
- **You already use other mods** — the file exists: **do not overwrite it.** Every mod keeps its settings in that same file, so replacing it also discards theirs. Merge only the `[CrucibleUnlock]` block above into your own file, and back it up first if you want a safety net.

**Done. Launch the game yourself through Steam, then go to the second blood bowl in the Crucible hub to play the Second Crucible.**

If your game is not Build 22928553, or the mod does not work after installation, contact the author.

[Back to the project](README.md)
