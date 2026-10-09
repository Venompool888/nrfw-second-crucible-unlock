# Actual runtime source regression

This net6.0 executable directly compiles production `EnemyRestorationRuntime.cs` and `EnemyRestorationCatalog.cs`, embedding the actual ten-profile bindings. BuildGuard, Core, resolver caches, wrappers, EnemyRestorationViews.Ensure and Harmony registration are namespace-compatible managed doubles. No game, Unity, IL2CPP or Harmony DLL is referenced or loaded. The final assertion checks no game/Unity/IL2CPP assemblies loaded.

The host driver invokes the exact static, no-argument `PumpCurrentViews()` by reflection, representing the new ModMain.OnUpdate dispatch. It never calls the legacy resolver Update callback or sets runtime readiness itself. This allows the frozen R3 source to compile and fail meaningfully when the pump does not exist. The real ModMain IL call is covered separately by root's PE hook audit; this suite does not compile or execute ModMain.

The task originally considered Tick(ViewFrame) dispatch. The implemented fix instead uses the existing loader OnUpdate, removing dependence on another native detour being called. Core's exact fake type is `Il2CppMoon.Forsaken.Core.AssetCtx : Il2Cpp.IAssetWrapperResolver`; its TryCast supplies the current RuntimeAssetResolutionContext. The official Tick evidence is not treated as this suite's liveness mechanism.

Coverage includes absent/wrong-type resolver; delayed LUT and retry; all ten cached assets/wrappers before ready; same-context revalidation; a mismatch at the tenth global wrapper; retry after global lookup exception; resource-manager reset; new resolver waiting and then ready; context loss and restoration; strict Ensure failure; disabled and explicit shutdown with forbidden Unity reads. A scripted Core getter also changes resolver between the first and second read: retired context receives no Ensure or writes, and the new resolver becomes ready on the next update. Shutdown is independently tested from a freshly enabled, ready runtime.

The frozen baseline was copied before production editing, without hand changes, into `baseline/R3EnemyRestorationRuntime.cs`. `RuntimeSource` only overrides the test Compile input for the red run. The default project compiles the current production file. Only this test directory was modified.

Use the project SDK `../crucible-mod/.dotnet/dotnet.exe` from the task worktree. Set TEMP/TMP and DOTNET_CLI_HOME to this directory's `.tmp`/`.cli`, and disable telemetry/certificate generation as in the task build environment. Run:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationRuntimeTests/EnemyRestorationRuntimeTests.csproj -c Release -p:RuntimeSource=baseline/R3EnemyRestorationRuntime.cs -- tests/EnemyRestorationRuntimeTests/baseline/R3EnemyRestorationRuntime.cs tests/EnemyRestorationRuntimeTests/r3-red.json
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationRuntimeTests/EnemyRestorationRuntimeTests.csproj -c Release -- src/CrucibleUnlock/EnemyRestorationRuntime.cs tests/EnemyRestorationRuntimeTests/r4-green.json
```

Final same-harness results on 2026-10-10:

| Observation | R3 RED | R4 GREEN |
|---|---:|---:|
| Exit status | 1 | 0 |
| Checks / failures | 31 / 15 | 51 / 0 |
| Host updates | 20 | 20 |
| Missing pump dispatches | 20 | 0 |
| Ensure calls | 0 | 13 |
| Ready records | 0 | 5 |

The extra twenty green checks validate the ABI of each existing pump invocation. R3 cannot run those checks because its pump is absent. Five green ready records include successful context transitions and the independently reinstalled shutdown fixture; healthy repeated updates do not produce extra ready records.

RED root cause: Install registered RuntimeAssetResolutionContext.Update, while the chosen live dispatch has no PumpCurrentViews. Every host update misses dispatch, so Ensure is never called and CurrentViewsReady remains false. GREEN proves the actual revised runtime performs the expected orchestration against managed fakes; it does not prove game-context liveness or native view rendering.

Frozen R3 source SHA256: `38a2b84c8f66cd335766351a88b3ab769466d455324e90ebcb6b63550121537e`.

Tested R4 production source SHA256: `356d2f15946a19257006611227221939d62b5502a7e25553289c0a26f3a898f2`.

Detailed outcomes are in `r3-red.json` / `r3-red.txt` and `r4-green.json` / `r4-green.txt`. Existing EnemyRestorationViewsTests separately exercise Ensure with 52 assertions. This suite does not test that implementation, native ABI, simulation registration, gameplay, actual loader scheduling or the R3/R4 JIT. No game is started or operated.

