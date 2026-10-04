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

Get the player archive from [Nexus Mods](https://www.nexusmods.com/norestforthewicked/mods/108) and follow [INSTALL.md](INSTALL.md). Do not use the old `0.4.0` archive or its persistent-unlock instructions from the research workspace.

## Source and builds

The source under `src/` is the frozen `0.8.6-boss-order` source. Building it requires MelonLoader references and game-specific generated IL2CPP interop assemblies from the supported game. See [BUILD.md](BUILD.md). The game, MelonLoader binaries, and generated interop assemblies are not included.

The code is available under [MIT](LICENSE-CODE.md); the six image data files under `src/CrucibleUnlock/Assets/SecondBowlBlood/` are outside that code license.

This is an unofficial community project.
