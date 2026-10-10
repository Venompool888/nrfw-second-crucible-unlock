# Canonical sword reaction view regression harness

This managed harness compiles the production `EnemyRestorationReactionViews.cs`
with local fake Unity/interop classes. It neither references nor loads game DLLs.
Run from the task worktree using the bundled .NET 6 SDK:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-home'
$env:TEMP = Join-Path (Get-Location) '.tmp'
$env:TMP = $env:TEMP
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationReactionViewsTests/EnemyRestorationReactionViewsTests.csproj -c Release
```

R13 result: **222 checks passed**, including the original 148 parry checks.
The frozen actual R12 helper is `baseline/R12EnemyRestorationReactionViews.cs`
(SHA256 `556625ce30ebc9def3a85ef77228549b24b89454cbdc775f2d761504d5bd8302`).
Run the same tests against it with
`-p:ReactionSource=baseline/R12EnemyRestorationReactionViews.cs`: the existing
148 parry checks pass, then canonical blocked fallback fails because R12 returns
null. This is an actual old-source red result, not an invented no-op helper.

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
publication without rollback or duplicate ownership records. R13 adds canonical
blocked-hit clip preservation, the separate exact 13-track inventory, direct
weapon-recoil/profile scope and current equipped asset identity, atomic readiness
of both sources, simultaneous `(owner pointer, GUID)` records, exact individual
map cleanup and retries, immutable source references, and WwiseWeaponAnimator
companion/host binding after a native callback which clears its caches. The
companion and effective-host resolution failure cases exercise production
validation; fakes do not model the native cached-host value-handle ABI.

`EnemyRestorationOwnerScope` is an injected verified-profile seam here. Its
actual native attachment layout, current frame, storage lookup and manager
identity checks have separate native ABI/schema/source review and production
compile evidence; there is no actual-source fake OwnerScope harness. These tests
check orchestration, not IL2CPP execution or animation
playback. Native registration/context evidence and shipped clip/reference
structure are recorded in
`artifacts/third-launch/r9-animation-view-review/parry-review.md`.

There is no fabricated no-op old-helper baseline. The genuine frozen R9 Runtime
baseline and its 99 checks / 11 expected failures are kept separately in
`artifacts/third-launch/r10-runtime-r9-red.json` and the Runtime test baseline.
