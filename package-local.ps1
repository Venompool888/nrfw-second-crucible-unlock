param(
    [string]$DllPath = (Join-Path $PSScriptRoot 'src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll')
)

$ErrorActionPreference = 'Stop'
$expectedDllHash = 'F456A46C79A8F9C272946BB00BD555D127FFE770E1EDD7D757B44F6EDA5472CC'
if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) { throw "Build output missing: $DllPath" }
$actualDllHash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash
if ($actualDllHash -ne $expectedDllHash) { throw "Unexpected DLL hash: $actualDllHash" }

$stage = Join-Path $PSScriptRoot 'artifacts/nexus-stage'
$mods = Join-Path $stage 'Mods'
$dist = Join-Path $PSScriptRoot 'dist'
$zip = Join-Path $dist 'CrucibleUnlock-0.8.6-boss-order.zip'
New-Item -ItemType Directory -Path $mods, $dist -Force | Out-Null
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $mods 'CrucibleUnlock.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'INSTALL.md') -Destination (Join-Path $stage 'INSTALL.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination (Join-Path $stage 'CHANGELOG.md') -Force
Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Value "$actualDllHash  Mods/CrucibleUnlock.dll" -Encoding ascii
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output "DLL: $actualDllHash"
Write-Output "ZIP: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)"
Write-Output "Path: $zip"
