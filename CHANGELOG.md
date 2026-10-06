# Version history

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
