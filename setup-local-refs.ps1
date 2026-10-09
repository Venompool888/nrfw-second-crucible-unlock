param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot
)

$ErrorActionPreference = 'Stop'
$gameRootResolved = (Resolve-Path -LiteralPath $GameRoot).Path
$loaderRoot = Join-Path $gameRootResolved 'MelonLoader'
$interopRoot = Join-Path $loaderRoot 'Il2CppAssemblies'
$net6Root = Join-Path $loaderRoot 'net6'
if (-not (Test-Path -LiteralPath $interopRoot -PathType Container)) { throw "Missing generated interop directory: $interopRoot" }
if (-not (Test-Path -LiteralPath $net6Root -PathType Container)) { throw "Missing MelonLoader net6 directory: $net6Root" }

$projectPaths = @('src/CrucibleUnlock/CrucibleUnlock.csproj', 'src/BowgunRepair/BowgunRepair.csproj')
foreach ($projectPath in $projectPaths) {
  $project = [xml](Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot $projectPath))
  foreach ($reference in $project.Project.ItemGroup.Reference) {
    $relative = ([string]$reference.HintPath).Replace('/', '\')
    if (-not $relative.StartsWith('..\..\vendor\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected reference path in project: $relative"
    }
    $name = [IO.Path]::GetFileName($relative)
    $kind = if ($relative.Contains('\refs\')) { 'refs' } else { 'generated' }
    $source = if ($kind -eq 'refs') { Join-Path $net6Root $name } else { Join-Path $interopRoot $name }
    if ($kind -eq 'generated' -and -not (Test-Path -LiteralPath $source -PathType Leaf)) {
        $source = Join-Path $net6Root $name
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required reference missing: $source" }
    $targetDir = Join-Path $PSScriptRoot "vendor/$kind"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $targetDir $name) -Force
    Write-Output "Copied $kind/$name"
  }
}
