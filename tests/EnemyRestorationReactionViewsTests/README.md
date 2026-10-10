# Sword parry reaction view regression harness

This managed harness compiles the production `EnemyRestorationReactionViews.cs`
with local fake Unity/interop classes. It neither references nor loads game DLLs.
Run from the task worktree using the bundled .NET 6 SDK:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-home'
$env:TEMP = Join-Path (Get-Location) '.tmp'
$env:TMP = $env:TEMP
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationReactionViewsTests/EnemyRestorationReactionViewsTests.csproj -c Release --nologo
```

Final result: **148 checks passed**. The captured result is
`artifacts/third-launch/r10-reaction-views-green.txt`.

Coverage includes all three eligible profiles; independent timeline and typed
reference copies; correct owner animator, weapon preview, and sound bindings;
preserved source references; equipment-loading and current-context gates;
exact action-map ownership before and after registration and on repeated
lookups; source/owner/equipment mutation rejection; incompatible rig, shared
timeline, foreign track, invalid reference copy, unresolved reference,
initialization and activation failures; preservation of foreign map entries;
GUID-unregister fallback only for the exact clone; separate cleanup error
boundaries; retained inactive cleanup records when map removal or Destroy is
unproven; cleanup retry after creation and release failures; readiness refusal
for releasing copies; bounded diagnostics; and READY-log failure after successful
publication without rollback or duplicate ownership records.

`EnemyRestorationOwnerScope` is an injected verified-profile seam here. Its
actual native attachment layout, current frame, storage lookup and manager
identity checks are covered by the separate OwnerScope harness and native
review. These tests check orchestration, not IL2CPP execution or animation
playback. Native registration/context evidence and shipped clip/reference
structure are recorded in
`artifacts/third-launch/r9-animation-view-review/parry-review.md`.

There is no fabricated no-op old-helper baseline. The genuine frozen R9 Runtime
baseline and its 99 checks / 11 expected failures are kept separately in
`artifacts/third-launch/r10-runtime-r9-red.json` and the Runtime test baseline.
