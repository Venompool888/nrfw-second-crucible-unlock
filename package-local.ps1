param(
    [string]$DllPath = (Join-Path $PSScriptRoot 'src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll')
)

$ErrorActionPreference = 'Stop'
$expectedDllHash = 'EB2753BAE81B22BA2F543B992116F0EE36DF0CD69F06374E8010847BC7F033C0'
if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) { throw "Build output missing: $DllPath" }
$actualDllHash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash
if ($actualDllHash -ne $expectedDllHash) { throw "Unexpected DLL hash: $actualDllHash" }

$stage = Join-Path $PSScriptRoot 'artifacts/stage-0.9.5'
$mods = Join-Path $stage 'Mods'
$dist = Join-Path $PSScriptRoot 'download/0.9.5'
$zip = Join-Path $dist 'CrucibleUnlock-0.9.5.zip'
New-Item -ItemType Directory -Path $mods, $dist, (Join-Path $dist 'Mods') -Force | Out-Null
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $mods 'CrucibleUnlock.dll') -Force
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $dist 'Mods/CrucibleUnlock.dll') -Force
Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Value @(
    'Crucible Unlock 0.9.5 - Steam public Build 22928553 only.'
    'Place Mods/CrucibleUnlock.dll in the game installation directory.'
    'Install MelonLoader 0.7.3 separately and set the CrucibleUnlock preferences.'
    'See https://github.com/Venompool888/nrfw-second-crucible-unlock for full instructions.'
) -Encoding utf8
Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Value "$actualDllHash  Mods/CrucibleUnlock.dll" -Encoding ascii
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output "DLL: $actualDllHash"
Write-Output "ZIP: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)"
Write-Output "Path: $zip"
