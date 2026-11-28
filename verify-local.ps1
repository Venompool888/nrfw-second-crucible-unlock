# Build local plugins and run managed policy/PE-metadata checks. No installation, hash enforcement, or game launch.
[CmdletBinding()]
param([string]$Dotnet = '')
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Dotnet)) {
    $localDotnet = Join-Path $base '.dotnet/dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localDotnet -PathType Leaf) { $localDotnet } else { 'dotnet' }
}
$env:DOTNET_CLI_HOME = Join-Path $base '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $base '.nuget'
$env:TEMP = Join-Path $base '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
foreach ($directory in @($env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:TEMP)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
function RunSdk([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Offline verification failed: $($Arguments -join ' ')" }
}
RunSdk @('build',(Join-Path $base 'src/CrucibleUnlock/CrucibleUnlock.csproj'),'-c','Release','-v','minimal')
RunSdk @('build',(Join-Path $base 'src/BowgunRepair/BowgunRepair.csproj'),'-c','Release','-v','minimal')
RunSdk @('build',(Join-Path $base 'src/Necromancy/Necromancy.csproj'),'-c','Release','-v','minimal')
RunSdk @('run','--project',(Join-Path $base 'tests/RuntimePolicyTests/RuntimePolicyTests.csproj'),'-c','Release')
RunSdk @('run','--project',(Join-Path $base 'tests/BowgunRepairTests/BowgunRepairTests.csproj'),'-c','Release')
RunSdk @('run','--project',(Join-Path $base 'tests/PlayerConfigurationTests/PlayerConfigurationTests.csproj'),'-c','Release')
RunSdk @('run','--project',(Join-Path $base 'tests/NecromancyReactionTests/NecromancyReactionTests.csproj'),'-c','Release')
RunSdk @('run','--project',(Join-Path $base 'tests/NecromancyBossRuneTests/NecromancyBossRuneTests.csproj'),'-c','Release')
$core = Join-Path $base 'src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll'
$audio = Join-Path $base 'src/BowgunRepair/bin/Release/NRFWBowgunAudioSync.dll'
$necromancy = Join-Path $base 'src/Necromancy/bin/Release/NRFWNecromancy.dll'
RunSdk @('run','--project',(Join-Path $base 'tools/BowgunHookAudit/BowgunHookAudit.csproj'),'-c','Release','--',(Join-Path $base 'vendor/generated'),$core)
RunSdk @('run','--project',(Join-Path $base 'tools/BowgunInputMetadataGate/BowgunInputMetadataGate.csproj'),'-c','Release','--',$core,$audio)
RunSdk @('run','--project',(Join-Path $base 'tools/NecromancyHookAudit/NecromancyHookAudit.csproj'),'-c','Release','--',(Join-Path $base 'vendor/generated'),$necromancy)
'PASS: 3 plugin builds, 5 managed policy/unit test suites, and 3 PE metadata audits.'
