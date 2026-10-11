# Build from source

Current main retains the `0.9.17` player defaults and includes the user-approved R17 Third Crucible source, with internal core label `0.9.17-third-trial-candidate-r17`. The `0.3.3` bowgun audio/input component is unchanged. The latest published stable download remains `0.9.17`; its hashes under `download/` describe that earlier release, not the current main build. See [R17 integration and known limits](research/third-trial/r17-main-integration.md).

## Local requirements

- Windows x64, .NET 6 SDK, a locally owned Steam copy of No Rest for the Wicked Build 22928553, and MelonLoader 0.7.3.
- Generated IL2CPP assemblies under `vendor/generated/` and MelonLoader/0Harmony references under `vendor/refs/`. The setup script copies references for both projects, including the generated Wwise API reference.
- Matching `*.rgba.gz` assets for both bowls are included under `src/CrucibleUnlock/Assets/SecondBowlBlood/` and `src/CrucibleUnlock/Assets/ThirdBowlBlood/`.

With the game closed, copy the required references from your own installation and build:

```powershell
./setup-local-refs.ps1 -GameRoot 'C:/Games/NoRestForTheWicked'
./verify-local.ps1
```

Adjust `-GameRoot` to your actual installation. `setup-local-refs.ps1` only reads the game installation and copies selected references into this repository's ignored `vendor/` directory. Do not substitute arbitrary game versions: the runtime build guard is intentionally exact.

`verify-local.ps1` builds both plugins, runs the runtime policy suite and 77 bowgun policy checks, the player entry-point configuration suite, 15 core hook registrations and compiled callbacks, and 12 input metadata/thread contracts. Pure tests use managed fixtures; metadata tools read PE/IL without loading game assemblies. Pass `-Dotnet 'C:/path/to/dotnet.exe'` to select a specific .NET 6 executable.

For the complete R17 offline gate, use:

```powershell
./verify-third-trial.ps1 -Dotnet 'C:/path/to/dotnet.exe' -Python '../item-db/.venv/Scripts/python.exe' -ReportDirectory 'artifacts/r17-verification'
```

This runs 28 build, managed regression, PE/JIT metadata and source-resource tasks. The two source audits require the local NRFW research workspace: `research/crucible-deep/combat/bundle_select.py`, `research/quantum-runtime/quantum-full.sqlite`, `research/item-db/output/items.sqlite`, Python/UnityPy dependencies and the owned Build 22928553 bundles. These are read-only inputs, not shipped dependencies. The curved-sword auditor is included in this checkout and does not depend on another unmerged feature checkout. Missing local source inputs are a validation limitation, not a reason to launch the game.

`package-local.ps1` remains pinned to the published 0.9.17 core hash and unchanged 0.3.3 audio/input hash; it rejects the current R17 core. `package_r17.py` records the historical frozen R17 candidate and its installation baseline. This main-source integration does not regenerate those historical packages or publish a new release. A compiler/source/build-setting change may produce a different hash and must be treated as a different build.

For separate builds:

```powershell
dotnet build src/CrucibleUnlock/CrucibleUnlock.csproj -c Release
dotnet build src/BowgunRepair/BowgunRepair.csproj -c Release
```

These scripts do not install plugins or launch the game. Runtime coverage and limits are recorded in `CHANGELOG.md` and the version-specific `release-manifest.json`; static checks do not establish PS hardware support or audible onset.
