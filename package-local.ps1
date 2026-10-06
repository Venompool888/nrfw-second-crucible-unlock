param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath
)

$ErrorActionPreference = 'Stop'
$expectedDllHash = 'EB2753BAE81B22BA2F543B992116F0EE36DF0CD69F06374E8010847BC7F033C0'
if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) { throw "Build output missing: $DllPath" }
$actualDllHash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash
if ($actualDllHash -ne $expectedDllHash) { throw "Unexpected DLL hash: $actualDllHash" }

$dist = Join-Path $PSScriptRoot 'download/0.9.5'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$publishedDll = Join-Path $dist 'CrucibleUnlock.dll'
Copy-Item -LiteralPath $DllPath -Destination $publishedDll -Force
Write-Output "DLL: $actualDllHash"
Write-Output "Path: $publishedDll"
