# Actual ThirdTrialSpawning regression

R8 final results: **frozen R7 RED66 checks/33 failures, exit1; R8 GREEN66 checks/zero failures, exit0**. Reports/transcripts are `r7-equality-red.json/.txt` and `r8-equality-green.json/.txt`; preceding reports below remain historical. Root supplied a complete R7 stack locating NRE at GeneratedEntryMatches `QListPtr<T>.op_Equality` invoked by `pointer == null`.

The fake NuggetPointer now faithfully rejects null operands in overloaded ==/!= with an Il2CppObjectBaseToPtrNotNull-shaped NRE. Even a nonnull pointer compared to CLR null must throw, so the frozen source fails all valid combat paths. The R8 production change is a single `ReferenceEquals(pointer,null)` comparison; native Ptr validity and the full selected Nugget multiset remain required. New assertions distinguish null wrapper, nonnull/invalid Ptr and valid/full content, and require zero overloaded equality calls. Prior raw component-buffer, scope, readiness and diagnostics checks remain.

R8 exact commands:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialSpawningTests -c Release -p:SpawnSource=R7ThirdTrialSpawning.snapshot -- tests/ThirdTrialSpawningTests/R7ThirdTrialSpawning.snapshot tests/ThirdTrialSpawningTests/r7-equality-red.json
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialSpawningTests -c Release -- src/CrucibleUnlock/ThirdTrialSpawning.cs tests/ThirdTrialSpawningTests/r8-equality-green.json
```

Frozen R7 source SHA256: `fb7d0f81ac16814d37c0366c7909515b3d4fcf84b2b9f7727396023e95f568c5`. R8 source SHA256: `196059ba1fb7752582500428a33b999cf191619ba4202fc314b19a8dc5be2196`. Independent generated-wrapper IL evidence is in `artifacts/third-launch/r8-qlistptr-null-review.md`: == null invokes native equality, while ReferenceEquals/is null emit managed reference comparison. This closes the specific observed R7 exception; it does not establish successful live NPC argument writeback, combat or room progression. No game DLL is loaded or executed.

R7 final results supersede the older fixture described below: **frozen R6 RED62 checks/38 failures, exit1; R7 GREEN62 checks/zero failures, exit0**. `r6-aot-red.json/.txt` and `r7-pointer-green.json/.txt` preserve same-harness outcomes. The old R5/R6 reports remain historical. Their fake Frame.Get returned managed component objects directly and could not establish actual interop boxing or AOT availability; the R6 live NRE exposed that limitation.

The current harness models Identity as an explicit24-byte struct, IntegrationGroup as an8-byte sequential struct (integration ID+0, node index+4), and source data refs as4-byte structs. Fake EntityRefExtensions.Get returns raw HGlobal buffers of native component sizes:24/8/32/80. Source ref is written at+0 and NPC ref at+16. No managed reference is stored inside copied blittable values; node/data indexes resolve through separate managed tables. Buffers are freed after the run. Frame.Get for the three missing-AOT types throws, and the final assertion requires **zero Frame.Get calls**.

Actual production helpers read official pointer calls without constructing class wrappers or retaining pointers. Tests reject layout mismatch before any pointer read; null Identity, both group, source-spawner and diagnostic NPC pointers; and null runtime playlist-entry projection with phase/full stack. Result read failure leaves the success bit clear, so a subsequent valid read can emit RESULT. Disabled callbacks still make no component reads. Every prior ownership, whitelist, generated-content and predicted/verified check remains.

Current commands:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialSpawningTests -c Release -p:SpawnSource=R6ThirdTrialSpawning.snapshot -- tests/ThirdTrialSpawningTests/R6ThirdTrialSpawning.snapshot tests/ThirdTrialSpawningTests/r6-aot-red.json
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialSpawningTests -c Release -- src/CrucibleUnlock/ThirdTrialSpawning.cs tests/ThirdTrialSpawningTests/r7-pointer-green.json
```

Frozen R6 source SHA256: `b96a56b88f234e2c42aa9873446029fff48afe3ff47c88b49392fd885ccc6f00`; final R7 source SHA256: `fb7d0f81ac16814d37c0366c7909515b3d4fcf84b2b9f7727396023e95f568c5`. Official AOT/pointer/unboxed layout evidence is recorded in `artifacts/third-launch/r6-native-component-get-review.md`. The fake's missing-AOT exception models the static risk; the R6 live exception lacked a full stack, so these tests do not independently locate its exact throwing call. R7 preserves full phase and exception stack for the next user-run session. GREEN proves byte-offset orchestration with modeled buffers, not game execution.

The net6.0 project compiles actual ThirdTrialSpawning, SpawningPolicy and both catalogs against managed Frame/Harmony/API doubles. It embeds the actual room manifest and restored bindings. No game, Unity, IL2CPP or Harmony DLL is referenced or executed. Install resolves the exact eight native Spawn parameter types and EntityRef return; the harness invokes registered production callbacks by reflection, including byref NPC/result arguments.

Fixtures model the statically verified native sequence, documented in `artifacts/third-launch/r5-empty-room-native-review.md`: the content source has DynamicSpawnerComponent and IntegrationGroupComponent; the new NPC spawner has DynamicNpcSpawnerComponent, a copied IntegrationGroupComponent, and IdentityComponent.Scope pointing to the source. New component SpawnerData remains uninitialized because native code assigns it after Spawn returns. Instance asset Guid is deliberately unrelated to the manifest's source DynamicSpawnerData.Guid. Tests never manufacture a matching new-entity generic component except the explicit legacy synthetic control.

Frozen R5 source `R5ThirdTrialSpawning.snapshot` was copied before production editing. Final same-harness results: **R5 RED: 50 checks /37 failures, exit1**; **R6 GREEN: 50 checks /zero failures, exit0**. Reports/transcripts are `r5-red.json/.txt` and `r6-green.json/.txt`. The RED first proves the old hook blocks all ten valid new NPC spawners, while accepting an unrealistic all-components control. Further failures expose missing origin/group validation and diagnostics. Historical preliminary results were33/22 and33/0 before result diagnostics were added; the50-check reports supersede them.

Coverage includes ten real profile mappings; predicted/preloaded ownership with a different player-current room; original second/unknown source pass-through; absent NPC/identity/integration components; zero/self source scope; source components; group ID, owner-node ID and entity mismatch; native owner map; whitelist/null source data; full generated entry; readiness; and disabled callbacks. Prefix failures do not invoke the modeled native completion.

On successful native completion the fake writes component.NpcData before invoking actual AfterSpawn, which must only read it and leave result unchanged. A deliberately mismatched stored value proves the diagnostic reads the stored field rather than echoing its expected parameter. Predicted/verified phases have separate bounded replacement/result records; diagnostic read exceptions produce bounded LOGERROR without changing result or runtime control. Simulation diagnostics use fake TrialRepairLog.Write, representing the production managed queue, and do not call a simulation-thread logger delegate.

From the worktree use `../crucible-mod/.dotnet/dotnet.exe`, TEMP/TMP and DOTNET_CLI_HOME inside ignored local test directories, telemetry disabled:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialSpawningTests -c Release -p:SpawnSource=R5ThirdTrialSpawning.snapshot -- tests/ThirdTrialSpawningTests/R5ThirdTrialSpawning.snapshot tests/ThirdTrialSpawningTests/r5-red.json
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialSpawningTests -c Release -- src/CrucibleUnlock/ThirdTrialSpawning.cs tests/ThirdTrialSpawningTests/r6-green.json
```

Frozen R5 source SHA256: `1922c42ed4ad187d6a6755aea4f1daddfa02bf1e9d0e8151be592ce8110098b4`. Tested R6 source SHA256: `b96a56b88f234e2c42aa9873446029fff48afe3ff47c88b49392fd885ccc6f00`.

The modeled native completion does not execute Spawn, instantiate game entities, verify IL2CPP detour argument writeback, or prove that this candidate fixes gameplay. Root separately audits emitted callback ABI. New user-run predicted/verified RESULT records are needed to confirm native stored NPC and nonzero entity result; room combat, unload/reentry and native clear/reward behavior remain live-validation boundaries. No game is launched or controlled.
