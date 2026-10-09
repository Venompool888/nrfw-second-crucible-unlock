# Version history

## 0.9.17 prerelease candidate — 2026-10-10

- Simplified first-run setup: the core runtime mode and accepted repairs are enabled by default; players do not need to create a preferences file or set repair switches.
- Exposes only the optional `boss_trace_drop_amount` setting (0–1000, default 300; 0 disables the target Boss trace quantity). Legacy preference keys remain in existing shared files but are ignored.
- Sets bowgun weapon class 30 internally. The separate bowgun audio/input dependency remains 0.3.3.
- Offline verification only: managed player-entry tests and release build/hash checks. This candidate has **not** been tested in-game. `0.9.16` remains the latest stable release and GitHub latest.
- This is a GitHub prerelease candidate, not a replacement for the tested 0.9.16 release.

## 0.9.16 + Bowgun Audio Sync 0.3.3 — 2026-10-10

- Restored the equipped Reinforced Bowgun's firing action, animation dependencies and scoped block-action input.
- Added the separate 0.3.3 audio/input component: keyboard/mouse uses the game's current block mapping, existing Gamepad routing is retained, and native crossbow fire audio follows projectile release.
- Reused the core's main-thread hero snapshot in input callbacks; equipment identity and UI/cinematic gates remain scoped.
- Added view/equipment rebind, pool-return and field-restoration safeguards to the audio component.
- Local test recorded 24 releases, including keyboard/mouse and Gamepad periods, with no input-extension errors. Sixteen had independent same-frame audio-ID evidence. PS hardware, audible onset and explicit pre-release interruption remain unverified; two cleanup paths lack individual CLEANED events.
- Both public builds reproduce the tested DLL hashes. Retains all previously merged Second Crucible functionality and the exact Build 22928553 guard.
- Download both CrucibleUnlock.dll and NRFWBowgunAudioSync.dll. The core retains internal label 0.9.16-bowgun-view-repair-r2; the companion is 0.3.3.

## 0.9.5 — 2026-10-06

- Added configurable Second Crucible Boss trace rewards, defaulting to 300 for eligible Bosses.
- Raised the active hero's echo limit to 2000 in the Second Crucible shop flow.
- Corrected Broken Vow Boss HUD and health handling, and Wallowing Husk name, trace identity, and overhead health-bar handling.
- Included 12 game-language mappings for the Wallowing Husk name.
- Offline verification passed on the development source. The final overhead health-bar change remains unconfirmed in play.
- Supported game version: Steam public Build 22928553 only. The DLL's internal MelonLoader label retains `0.9.5-husk-hud-candidate` from the verified build.

## 0.8.6-boss-order — 2026-10-05

- Installation guide correction: explicitly enable the two Warrick repair options, which otherwise default to off. The DLL is unchanged.
- Corrected IL2CPP nullable payload marshalling and verified the selected Boss content after room reintegration.
- Preserved the already tested second-bowl blood route and Warrick phase-two repair.
- One complete local run matched the original encounter order: Warrick, bloated Warrick, Darak, plagued Darak, checkpoint, Riven Twins, Spider Horse, Executioner, reward room.
- Supported game version: Steam public Build 22928553 only.

## 0.8.5-warrick-test — 2026-10-05

- Repaired Warrick's phase-two target across four observed instances.
- Superseded: a later test exposed an empty fourth floor.

## 0.8.4-blood-r2 — 2026-10-05

- Adapted the offering blood route to the second bowl. The player confirmed the cut, blood drop, and floor route alignment in one run.
- Superseded by the combined 0.8.6 build.

Earlier development builds were test candidates and are not distribution releases.
