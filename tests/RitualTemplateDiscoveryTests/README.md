# Ritual template discovery regression

`Program.cs` compiles the production `RitualViewRepair.Template.cs` and the actual ritual policies against small managed stand-ins. Run from the task branch root:

```powershell
$env:TEMP=(Join-Path (Get-Location) '.tmp')
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME=(Join-Path (Get-Location) '.dotnet-home')
New-Item -ItemType Directory -Force $env:TEMP,$env:DOTNET_CLI_HOME | Out-Null
..\crucible-mod\.dotnet\dotnet.exe run --project tests/RitualTemplateDiscoveryTests/RitualTemplateDiscoveryTests.csproj
```

The shipped bowl layout has at most one `GeneralInteractableView` and one `InteractionView` of the requested type per GameObject. Under that premise, `GetComponent` on each visited Transform finds the relevant component, including on inactive nodes. This test does **not** claim equivalence to arbitrary `GetComponentsInChildren` results when a GameObject has multiple components of one requested type. It also cannot verify IL2CPP runtime behavior, scene contents, or game play.
