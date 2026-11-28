# Build from source

Current main retains the `0.9.17` player defaults and includes the user-approved R17 Third Crucible source (internal core label `0.9.17-third-trial-candidate-r17`), the unchanged `0.3.3` bowgun audio/input component (`NRFWBowgunAudioSync`), and the standalone single-player necromancy plugin (`NRFWNecromancy`, currently `0.18.4`). The latest published stable download remains `0.9.17`; its hashes under `download/` describe that earlier release, not the current main build. See [R17 integration and known limits](research/third-trial/r17-main-integration.md).

## Local requirements

- Windows x64, .NET 6 SDK, a locally owned Steam copy of No Rest for the Wicked Build 22928553, and MelonLoader 0.7.3.
- Generated IL2CPP assemblies under `vendor/generated/` and MelonLoader/0Harmony references under `vendor/refs/`. `setup-local-refs.ps1` copies references for all three plugin projects (`CrucibleUnlock`, `BowgunRepair`, and `Necromancy`), including the generated Wwise API reference and the optional `UnityEngine.ImageConversionModule.dll` used by `NRFWNecromancy` for Boss rune PNG icons (`BOSS_RUNE_PNG`).
- Matching `*.rgba.gz` assets for both bowls are included under `src/CrucibleUnlock/Assets/SecondBowlBlood/` and `src/CrucibleUnlock/Assets/ThirdBowlBlood/`.

## Solution and shared build settings

- `Directory.Build.props` centralizes common MSBuild settings across `src/`, `tests/`, and `tools/` (`TargetFramework=net6.0`, `PlatformTarget=x64`, `LangVersion=latest`, `AppendTargetFrameworkToOutputPath=false`, `DebugType=none`, and `VendorDir`/`VendorGeneratedDir`/`VendorRefsDir`), while preserving per-project settings such as `BOSS_RUNE_PNG` in `Necromancy.csproj` and `ImplicitUsings` / `Nullable` options.
- `NRFWMods.sln` includes all 30 projects in the repository (3 plugins under `src/`, 22 pure-managed test suites under `tests/`, and 5 offline audit tools under `tools/`).

With the game closed, copy the required references from your own installation and build:

```powershell
./setup-local-refs.ps1 -GameRoot 'C:/Games/NoRestForTheWicked'
./verify-local.ps1
```

Adjust `-GameRoot` to your actual installation. `setup-local-refs.ps1` only reads the game installation and copies selected references into this repository's ignored `vendor/` directory. Do not substitute arbitrary game versions: the runtime build guard is intentionally exact.

`verify-local.ps1` auto-detects `./.dotnet/dotnet.exe` when `-Dotnet` is omitted (falling back to `dotnet` on `PATH`). It builds all three plugins (`CrucibleUnlock`, `BowgunRepair`, `Necromancy`), runs five fast managed policy/unit test suites (`RuntimePolicyTests`, `BowgunRepairTests`, `PlayerConfigurationTests`, `NecromancyReactionTests`, `NecromancyBossRuneTests`), and runs all three PE metadata audits (`BowgunHookAudit`, `BowgunInputMetadataGate`, `NecromancyHookAudit`). Pure tests use managed fixtures; metadata tools inspect PE/IL without loading game assemblies. Pass `-Dotnet 'C:/path/to/dotnet.exe'` to select a specific .NET 6 executable.

For CI without game files, `.github/workflows/ci.yml` runs all 22 pure-managed test projects under `tests/` (none of which reference `vendor/`) on push to `main` and pull requests.

For the complete R17 offline gate, use:

```powershell
./verify-third-trial.ps1 -Dotnet 'C:/path/to/dotnet.exe' -Python '../item-db/.venv/Scripts/python.exe' -ReportDirectory 'artifacts/r17-verification'
```

This runs 28 build, managed regression, PE/JIT metadata and source-resource tasks. The two source audits require the local NRFW research workspace: `research/crucible-deep/combat/bundle_select.py`, `research/quantum-runtime/quantum-full.sqlite`, `research/item-db/output/items.sqlite`, Python/UnityPy dependencies and the owned Build 22928553 bundles. These are read-only inputs, not shipped dependencies. The curved-sword auditor is included in this checkout and does not depend on another unmerged feature checkout. Missing local source inputs are a validation limitation, not a reason to launch the game.

`package-local.ps1` remains pinned to the published 0.9.17 core hash and unchanged 0.3.3 audio/input hash and is no longer invoked by `verify-local.ps1`; it rejects the current R17 core. `package_r17.py` records the historical frozen R17 candidate and its installation baseline. This main-source integration does not regenerate those historical packages or publish a new release.

For solution-wide or separate builds:

```powershell
dotnet build NRFWMods.sln -c Release
dotnet build src/CrucibleUnlock/CrucibleUnlock.csproj -c Release
dotnet build src/BowgunRepair/BowgunRepair.csproj -c Release
dotnet build src/Necromancy/Necromancy.csproj -c Release
```

These scripts do not install plugins or launch the game. Runtime coverage and limits are recorded in `CHANGELOG.md` and the version-specific `release-manifest.json`; static checks do not establish PS hardware support or audible onset.
