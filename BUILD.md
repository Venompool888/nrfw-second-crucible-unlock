# Build from source

This is a source snapshot of the tested `0.8.6-boss-order` DLL. Build references are supplied by the local MelonLoader installation for the supported game build.

## Local requirements

- Windows x64, .NET 6 SDK, a locally owned Steam copy of No Rest for the Wicked Build 22928553, and a compatible MelonLoader 0.7.x installation.
- Locally generated IL2CPP assemblies and MelonLoader/0Harmony references under `vendor/generated/` and `vendor/refs/`, with the filenames referenced by `src/CrucibleUnlock/CrucibleUnlock.csproj`.
- The six matching `*.rgba.gz` assets are included under `src/CrucibleUnlock/Assets/SecondBowlBlood/`.

With the game closed, copy the required references from your own installation and build:

```powershell
./setup-local-refs.ps1 -GameRoot 'C:/Games/NoRestForTheWicked'
dotnet build src/CrucibleUnlock/CrucibleUnlock.csproj -c Release
dotnet run --project tests/RuntimePolicyTests/RuntimePolicyTests.csproj -c Release
./package-local.ps1
```

Adjust `-GameRoot` to your actual installation. `setup-local-refs.ps1` only reads the game installation and copies selected references into this repository's ignored `vendor/` directory. Do not substitute arbitrary game versions: the runtime build guard is intentionally exact.

The build output is `src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll`. Build and runtime compatibility are limited to the specified game build.
