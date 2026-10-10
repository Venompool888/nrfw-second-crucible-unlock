# Private enemy weapon Unity bridge regression harness

This project compiles the actual `EnemyRestorationWeaponViews.cs` and catalog against managed fakes. It neither references nor loads a game/Unity DLL. The ten-profile fixture models the three pruned canonical weapons at indices 1, 8 and 9 and requires null entries for the seven shipped weapons.

The real R9 regression baseline belongs to the Runtime harness: `../EnemyRestorationRuntimeTests/baseline/R9EnemyRestorationRuntime.cs`, with the frozen RED result at `../../artifacts/third-launch/r10-runtime-r9-red.json` (99 checks, 11 failures). This new helper had no R9 implementation; no fabricated no-op helper is described as a frozen baseline.

Run with the bundled SDK, setting `DOTNET_CLI_HOME`, `TEMP`, and `TMP` to the task workspace directories:

```
../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationWeaponViewsTests -c Release
```

GREEN: 101 checks, recorded at `../../artifacts/third-launch/r10-weapon-views-green.txt`. Coverage includes canonical pointer identity with zero simulation data creation and no GUID/path writes, exact source prefab reference preservation, both wrapper resolver paths, absent/incorrect snapshots, source type/GUID mismatch, unpooled prefabs, root-only `EnemyWeaponView` checks, asynchronous wait and load failure, foreign registrations/list paths, post-insertion failures in every cache, rollback, and repeated readiness checks after ownership/source/pool/root/resolver mutation.

These fakes prove orchestration and fail-closed behavior. They do not prove native method compatibility, Addressables completion in game, exact appearance, animation timing, or absence of other missing views. Those require independent native evidence and the user's later gameplay.
