# Candidate-only offline verification. Builds our code, executes managed fakes,
# reads PE metadata; never loads game DLLs, installs a plugin or launches a game.
[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
$reportDir = Join-Path $base 'artifacts\third-launch\r15-repair\verification'
$env:DOTNET_CLI_HOME = Join-Path $base '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $base '.nuget'
$env:TEMP = Join-Path $base '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
foreach ($directory in @($reportDir,$env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:TEMP)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$results = [Collections.Generic.List[object]]::new()
function RunOffline([string]$Name, [string[]]$Arguments) {
    $output = & $Dotnet @Arguments 2>&1
    $code = $LASTEXITCODE
    $log = Join-Path $reportDir ($Name + '.txt')
    $output | Set-Content -LiteralPath $log -Encoding utf8
    $results.Add([pscustomobject]@{name=$Name;exit_code=$code;arguments=$Arguments;log=$log})
    $tail = ($output | Select-Object -Last 1) -as [string]
    Write-Output "$Name exit=$code : $tail"
}
Push-Location -LiteralPath $base
try {
    RunOffline 'build-core' @('build','src/CrucibleUnlock/CrucibleUnlock.csproj','-c','Release','-v','minimal')
    RunOffline 'build-audio' @('build','src/BowgunRepair/BowgunRepair.csproj','-c','Release','-v','minimal')
    foreach ($name in @('RuntimePolicyTests','BowgunRepairTests','PlayerConfigurationTests',
        'EnemyRestorationTests','EnemyRestorationViewsTests','EnemyRestorationWeaponViewsTests',
        'EnemyRestorationKeeperWeaponViewsTests','EnemyRestorationActionHooksTests','EnemyRestorationReactionViewsTests',
        'EnemyRestorationAudioDiagnosticsTests','EnemyRestorationRuntimeTests','EnemyRestorationVoiceRepairTests',
        'ThirdTrialEntryTests','ThirdTrialStorageTests','ThirdTrialNativeStorageTests','ThirdTrialRuntimeTests',
        'ThirdTrialSpawningTests','ThirdTrialPresentationTests','RitualTemplateDiscoveryTests','ThirdTrialHudRepairTests')) {
        $arguments = @('run','--project',"tests/$name/$name.csproj",'-c','Release')
        if ($name -eq 'EnemyRestorationRuntimeTests') {
            $arguments += @('--','src/CrucibleUnlock/EnemyRestorationRuntime.cs',(Join-Path $reportDir ($name+'.json')))
        }
        RunOffline $name $arguments
    }
    $core = Join-Path $base 'src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll'
    $audio = Join-Path $base 'src/BowgunRepair/bin/Release/NRFWBowgunAudioSync.dll'
    RunOffline 'bowgun-hooks' @('run','--project','tools/BowgunHookAudit/BowgunHookAudit.csproj','-c','Release','--',(Join-Path $base 'vendor/generated'),$core)
    RunOffline 'bowgun-input-metadata' @('run','--project','tools/BowgunInputMetadataGate/BowgunInputMetadataGate.csproj','-c','Release','--',$core,$audio)
    RunOffline 'third-trial-hooks' @('run','--project','tools/ThirdTrialHookAudit/ThirdTrialHookAudit.csproj','-c','Release','--',(Join-Path $base 'vendor/generated'),$core)
    RunOffline 'ritual-jit-order' @('run','--project','tools/RitualViewJitAudit/RitualViewJitAudit.csproj','-c','Release','--',$core,(Join-Path $reportDir 'ritual-jit-order.json'),'--require-early-preflight')
    $failed = @($results | Where-Object exit_code -ne 0)
    [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');core_sha256=(Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant();checks=$results;passed=($failed.Count -eq 0);game_started=$false;installed=$false;game_dlls_executed=$false} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportDir 'summary.json') -Encoding utf8
    if ($failed.Count -ne 0) { throw "Offline candidate checks failed: $($failed.name -join ', ')" }
    Write-Output "PASS: $($results.Count) candidate build, managed regression and PE metadata tasks; no installation or game launch."
}
finally { Pop-Location }
