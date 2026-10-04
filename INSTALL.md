# Player guide — 0.8.6-boss-order

1. Confirm that Steam reports No Rest for the Wicked public Build **22928553**. The mod is not verified on other builds.
2. Back up your realm and character saves before adding a loader or mod.
3. Install a compatible MelonLoader 0.7.x from its [official releases](https://github.com/LavaGang/MelonLoader/releases) and let it generate the game's IL2CPP assemblies.
4. With the game closed, place `CrucibleUnlock.dll` in the game's `Mods` directory. In `UserData/MelonPreferences.cfg`, set `[CrucibleUnlock]` `mode = "runtime-unlock"`.
5. Launch through Steam. Confirm the log reports that the build guard passed and both targeted read overrides were installed.

The unlock exists only while the mod runs. Normal play can still save progress, but this mod does not write a permanent quest completion. To uninstall, close the game and remove `Mods/CrucibleUnlock.dll`; remove MelonLoader separately if desired. Never replace the DLL while the game is running.

This mod is unofficial. If the game updates, wait for a compatible release. Do not bypass the build guard.
