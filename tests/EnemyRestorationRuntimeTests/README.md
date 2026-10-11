# Actual runtime source regression

R5 final same-harness regression on 2026-10-10: frozen R4 source is **RED, 87 checks / 22 failures, exit1**; current R5 source is **GREEN, 87 checks / zero failures, exit0**. Both perform 30 host updates with a real pump entry; R4 calls Ensure23 times, R5 calls it18 times. Reports/transcripts are `r4-r5-red.json/.txt` and `r5-green.json/.txt`. The R3/R4 results below are historical, not totals for the expanded R5 harness.

R5 actual `PublishSimulationViews(Frame,bool)` validates ten DynamicDB view identities and optionally exact 1-based storage slots before publishing. No simulation snapshot means no Unity Ensure call or cache write. Ensure receives the exact ten simulation objects. The suite rejects changed pointers for the same manager, incomplete/wrong-path assets, storage pointer mismatch and index0/Count+1. A missing publisher is not treated as a successful rejection. Actual standalone AfterSimulationInit is also tested: it registers and publishes views without requiring third-trial storage.

The Core fake now includes ResourceManager as well as AssetCtx. Scripted getters verify resolver/manager ownership before Ensure and manager ownership after Ensure; races remain retryable. Readiness cannot be borrowed by a frame using another manager. A WeakReference/GC assertion proves the real snapshot strongly retains its manager after callback Frame scope ends; reflection verifies snapshot fields retain no Frame or storage. Strict failure, reset, global tenth-wrapper validation and forbidden Unity reads from disabled/stopped state remain covered.

R5 exact commands, using the same task SDK/environment described below:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationRuntimeTests -c Release -p:RuntimeSource=baseline/R4EnemyRestorationRuntime.cs -- tests/EnemyRestorationRuntimeTests/baseline/R4EnemyRestorationRuntime.cs tests/EnemyRestorationRuntimeTests/r4-r5-red.json
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationRuntimeTests -c Release -- src/CrucibleUnlock/EnemyRestorationRuntime.cs tests/EnemyRestorationRuntimeTests/r5-green.json
```

Frozen R4 source SHA256: `356d2f15946a19257006611227221939d62b5502a7e25553289c0a26f3a898f2`. Tested R5 source SHA256: `b7d190991cc02c319bf03484c90d1f186db9846932d72ec03ffae574f3594363`.

R5 GREEN validates actual managed orchestration against fakes. It does not establish the game's actual Core.ResourceManager/Frame.Context.ResourceManager identity relation, native view rendering, gameplay or JIT behavior. Strict manager equality remains in production; WAIT/publication logs include current/published manager pointers to collect evidence during the user's test. Actual Ensure is covered independently by EnemyRestorationViewsTests.

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

Historical R4 same-harness results on 2026-10-10:

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
