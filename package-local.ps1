param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath,
    [Parameter(Mandatory = $true)]
    [string]$AudioDllPath
)

$ErrorActionPreference = 'Stop'
$expectedDllHash = 'A7BDE226634CCEA7A88F88B8DEF97200C77C4486FB1FA8C3D9739FF83737AD50'
$expectedAudioHash = 'E861D4BABF57DA77259BB2DFFCA50BB51257C7D8C3ADB6D8EB0984D5E333D7C7'
if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) { throw "Build output missing: $DllPath" }
$actualDllHash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash
if ($actualDllHash -ne $expectedDllHash) { throw "Unexpected DLL hash: $actualDllHash" }
if (-not (Test-Path -LiteralPath $AudioDllPath -PathType Leaf)) { throw "Build output missing: $AudioDllPath" }
$actualAudioHash = (Get-FileHash -LiteralPath $AudioDllPath -Algorithm SHA256).Hash
if ($actualAudioHash -ne $expectedAudioHash) { throw "Unexpected audio/input DLL hash: $actualAudioHash" }

$dist = Join-Path $PSScriptRoot 'download/0.9.16'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$publishedDll = Join-Path $dist 'CrucibleUnlock.dll'
Copy-Item -LiteralPath $DllPath -Destination $publishedDll -Force
Copy-Item -LiteralPath $AudioDllPath -Destination (Join-Path $dist 'NRFWBowgunAudioSync.dll') -Force
@("$($actualDllHash.ToLowerInvariant())  CrucibleUnlock.dll", "$($actualAudioHash.ToLowerInvariant())  NRFWBowgunAudioSync.dll") | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Output "DLL: $actualDllHash"
Write-Output "Audio/input DLL: $actualAudioHash"
Write-Output "Path: $publishedDll"
