param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath,
    [Parameter(Mandatory = $true)]
    [string]$AudioDllPath
)

$ErrorActionPreference = 'Stop'
$expectedDllHash = '5553C36B4FD694484150FE3CEBCB7864FF3C1BC45DF4E6F484873CF9C2EF12AD'
$expectedAudioHash = 'E861D4BABF57DA77259BB2DFFCA50BB51257C7D8C3ADB6D8EB0984D5E333D7C7'
if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) { throw "Build output missing: $DllPath" }
$actualDllHash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash
if ($actualDllHash -ne $expectedDllHash) { throw "Unexpected DLL hash: $actualDllHash" }
if (-not (Test-Path -LiteralPath $AudioDllPath -PathType Leaf)) { throw "Build output missing: $AudioDllPath" }
$actualAudioHash = (Get-FileHash -LiteralPath $AudioDllPath -Algorithm SHA256).Hash
if ($actualAudioHash -ne $expectedAudioHash) { throw "Unexpected audio/input DLL hash: $actualAudioHash" }

$dist = Join-Path $PSScriptRoot 'download/0.9.17'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$publishedDll = Join-Path $dist 'CrucibleUnlock.dll'
Copy-Item -LiteralPath $DllPath -Destination $publishedDll -Force
Copy-Item -LiteralPath $AudioDllPath -Destination (Join-Path $dist 'NRFWBowgunAudioSync.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/CrucibleUnlock/MelonPreferences.example.cfg') -Destination (Join-Path $dist 'MelonPreferences.cfg') -Force
@("$($actualDllHash.ToLowerInvariant())  CrucibleUnlock.dll", "$($actualAudioHash.ToLowerInvariant())  NRFWBowgunAudioSync.dll") | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Output "DLL: $actualDllHash"
Write-Output "Audio/input DLL: $actualAudioHash"
Write-Output "Path: $publishedDll"
