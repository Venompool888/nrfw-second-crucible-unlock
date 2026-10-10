# Third-trial HUD source regression (R15)

This console suite links the **real production `ThirdTrialHudRepair.cs`** into an isolated executable with controlled native API/Unity fakes. It never loads or executes generated/game DLLs, starts the game, or edits game files, saves, configuration or logs. The fakes represent the read-only API signatures confirmed by PE metadata and implement a native HUD control with the established ordering: cached-index check, `floorMessages[completed + skipped]`, shadow/text updates, and only then the reward branch.

`ShippedHudMessages.json` is a fixture extracted from `artifacts/third-launch/r14-hud-audit/hud-asset-evidence.json`. It contains the **actual 11 serialized original messages I–XI**, IDs, all 12 language fields, object path IDs and the source bundle SHA-256. The HUD starts with those 11 entries, not a guessed ten-entry array.

Run from the repository with the bundled SDK:

```powershell
$env:TEMP = Join-Path $PWD '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialHudRepairTests
```

For the absent R14 repair control:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialHudRepairTests -p:HudSource=OldHudBehavior.cs.txt
```

`OldHudBehavior.cs.txt` supplies no-op install/lifecycle methods and passes through the prefix, matching R14's absence of a HUD repair. It does not pretend to be the game DLL. The original regression was observed RED before writing the repair: indices 11 and 12 hit the native-control message array's actual bounds exception before the reward branch. Additional RED tests caught retention of owned UI when a source read fails, an overly large detour callback, and re-exposure of the original native array bounds after a stop restored the eleven-entry array. Final real-source suite: **33 passed, 0 failed**.

Coverage includes exact private source identity and bound RuntimeCharacterReference; current-frame binding and no retained frame; index 11/12 including reward; unchanged original array/assets; all-language added Roman labels; generated count and skipped offset instead of source count; checked overflow, negative/corrupt state and ceiling 64; null arrays/messages; first/second trials; scope failure; per-instance/run ownership; return to ordinary trials with native cache invalidation; disable/reset cleanup; foreign arrays and borrowed clones; owned/foreign label/icon state; safe deferred stop that continues guarding only proven private overflow without allocation; and bounded diagnostics/short NoInlining callback.

Native and PE evidence is in `artifacts/third-launch/r15-hud`. `GetLocalizedRawText #16934` reads the twelve serialized language fields directly. Normal localization Mode 0 therefore needs no registration in a global localization cache; debug Mode 1 shows the clone's unique Id and Mode 2 prefixes Id as the game's existing debug semantics prescribe. The mod does not change static localization settings or caches.

Unity cloning, generated native setters/GC barriers, runtime GetQtn/player binding and actual display cannot be executed offline under this project's constraints. The suite validates production control flow against supported ABI-shaped fakes and shipped asset evidence, and **is not live-game validation**. The exact R14 failing index and runtime-generated count remain unknown until user play supplies fresh evidence. The icon past XI is deliberately hidden because no correct official XII/XIII sprite is supplied.
