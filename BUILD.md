# Build from source

This repository contains the accepted `0.9.16-bowgun-view-repair-r2` core and `0.3.3` bowgun audio/input source. Both distributed DLLs reproduce the user-tested development main `94aa298` byte for byte. SHA256 values are in `download/0.9.16/SHA256SUMS.txt`.

## Local requirements

- Windows x64, .NET 6 SDK, a locally owned Steam copy of No Rest for the Wicked Build 22928553, and MelonLoader 0.7.3.
- Generated IL2CPP assemblies under `vendor/generated/` and MelonLoader/0Harmony references under `vendor/refs/`. The setup script copies references for both projects, including the generated Wwise API reference.
- The six matching `*.rgba.gz` assets are included under `src/CrucibleUnlock/Assets/SecondBowlBlood/`.

With the game closed, copy the required references from your own installation and build:

```powershell
./setup-local-refs.ps1 -GameRoot 'C:/Games/NoRestForTheWicked'
./verify-local.ps1
```

Adjust `-GameRoot` to your actual installation. `setup-local-refs.ps1` only reads the game installation and copies selected references into this repository's ignored `vendor/` directory. Do not substitute arbitrary game versions: the runtime build guard is intentionally exact.

`verify-local.ps1` builds both plugins, runs the runtime policy suite and 77 bowgun policy checks, checks 15 core hook registrations and compiled callbacks, and checks 12 input metadata/thread contracts. Pure tests use managed fixtures; metadata tools read PE/IL without loading game assemblies. Pass `-Dotnet 'C:/path/to/dotnet.exe'` to select a specific .NET 6 executable.

Packaging accepts only the two verified release hashes. A compiler/source/build-setting change may produce a different hash; that output is a new candidate and is rejected. Broader unsafe-wrapper/native-asset audits passed on the accepted development build. The public repository contains authored plugin code and reproducible policy/metadata checks; raw research exports and game-specific dependencies remain locally supplied.

For separate builds and packaging:

```powershell
dotnet build src/CrucibleUnlock/CrucibleUnlock.csproj -c Release
dotnet build src/BowgunRepair/BowgunRepair.csproj -c Release
./package-local.ps1 -DllPath 'src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll' -AudioDllPath 'src/BowgunRepair/bin/Release/NRFWBowgunAudioSync.dll'
```

These scripts do not install plugins or launch the game. Runtime coverage and limits are recorded in `CHANGELOG.md` and `download/0.9.16/release-manifest.json`; static checks do not establish PS hardware support or audible onset.
