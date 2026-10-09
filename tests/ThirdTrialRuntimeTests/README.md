# Actual ThirdTrialRuntime source, managed test harness

R5 expanded regression on 2026-10-10: the frozen `R4ThirdTrialRuntime.snapshot` returns RED (exit1 at check12) because actual AfterInit never publishes simulation views after bootstrap. Current production source returns GREEN: **423 checks, exit0**. `r4-r5-red.json/.txt` and `r5-green.json/.txt` contain hashes and outcomes. The production ThirdTrialRuntime, StorageBackend/Policy and catalogs are compiled directly; the EnemyRestorationRuntime publisher is a managed fake here, while its actual implementation is tested separately in EnemyRestorationRuntimeTests.

The fake publisher rejects calls before all ten Trees, all ten Blocks and the private playlist are present, requires `requireStorage:true`, verifies exact storage/dynamic view pointers, and records publication after the playlist event. Tests require one publication on success, no additional publication on repeat, and zero publication for every initial bootstrap failure, including publisher failure. A failure on repeated bootstrap cannot gain readiness from a prior snapshot. A frame with initialized storage but a different resource manager is rejected; another frame with the same initialized storage, manager and graph is accepted.

Use the task SDK, local TEMP/TMP/DOTNET_CLI_HOME and these exact R5 commands from the worktree:

```powershell
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialRuntimeTests -c Release -p:RuntimeSource=R4ThirdTrialRuntime.snapshot -- tests/ThirdTrialRuntimeTests/R4ThirdTrialRuntime.snapshot tests/ThirdTrialRuntimeTests/r4-r5-red.json
& ../crucible-mod/.dotnet/dotnet.exe run --project tests/ThirdTrialRuntimeTests -c Release -- src/CrucibleUnlock/ThirdTrialRuntime.cs tests/ThirdTrialRuntimeTests/r5-green.json
```

Frozen R4 ThirdRuntime SHA256: `4aea5041b744aaf53f88bee02d0083b7ac10d088d9f9a0b6fc4d9dd8c6729fab`. Tested R5 ThirdRuntime SHA256: `fe904ceeea35947018d1e18ccca50794a0ee99901a1f03cf2628ce95a638ded0`. The earlier results described below are historical; the current totals above supersede them.

The project compiles the actual production Runtime, StorageBackend, StoragePolicy, FailurePolicy and both catalogs. It embeds both task manifests and a SecondPlaylist fixture copied from the read-only static extraction in `artifacts/third-entry/configuration.json`, record8619/source1509816363606571268. Its eight original entries, BossRush type, second SpawnPoint, fixed-point loot modifier and flags come from that data.

Only the EnemyRestorationRuntime asset preparation callback and native/Frame/Harmony APIs are faked. `AfterInit` and `BeforeRuleUpdate` are called through reflection; `Install`, `IsFrameReady`, `ResolveReadyPlaylist`, `TryGetNpc` and `Fail` use real Runtime source. There is no production test hook and no game/IL2CPP/Unity DLL reference. Fake source assets, cloned wrappers and caches have unique pointer identities; fake serializer performs an independent copy. The final check verifies no Il2Cpp/Unity assembly has loaded.

Run with the task SDK and local TEMP/TMP/DOTNET_CLI_HOME:

`dotnet run --project tests/ThirdTrialRuntimeTests/ThirdTrialRuntimeTests.csproj -c Release`

The earlier harness passed 375 checks with zero build warnings/errors:

- Clones precisely the original second resource-manager source. Original source has an unchanged complete snapshot of Entries/Type/SpawnPoint/flags before and after success and each initialization failure.
- Fake Harmony records production target type, method and postfix and dispatches the native initialization sequence. StartOfFrame runs first, then InitializeContentSystem performs non-idempotent static Team export and an unrelated pooled spawner export, then the registered postfix runs. All forty-three private graph assets, blocks and playlist remain absent before native content finishes. The exact official Team pointer is reused without another Add, and pooled initialization can continue.
- Ten fixed Arena entries contain each stage's full ordered EntryNuggetGuids; entry11 preserves independent native randomized Reward. Private source is Roguelite1 and third SpawnPoint. Second respite5/5, loot/healing/experience flags and 65536 loot modifier remain copied.
- Asset preparation happens first; all forty private dependencies and the shared Team precede attachment creation; all ten Trees precede all ten prepared Blocks; playlist registration happens last. Repeated OnInit makes no extra cache writes or Tree/Block/Playlist creations.
- Readiness requires the current-view flag, initialized storage identity, exact owned private source, all ten dynamic NPC/controller assets, all ten NPC cache identities, attachment blocks and private Tree OwnerGuid. Another Frame with uninitialized storage is rejected; another Frame sharing the initialized storage and same graph is accepted.
- Profile order mismatch rejects before initialization. Foreign source GUID/Path mismatch, a completely matching foreign deserialized source with a different identity, and native Add readback returning a new pointer are rejected. A previously owned source remains valid on repeat.
- Thirty-five injected initialization failures cover graph preparation, missing/mismatched dependencies, export, behavior-tree root/memory/Add, attachment shape/type/version/default refs/Add, template/serializer/source reward, static floor/full nugget/spawn, ownership collision, playlist Add/readback and bad source flags/respite. These disable entry readiness and NPC lookup. The failure rule prefix stops only this private source; first, original second, original third and unrelated/no source retain native rule execution.

An Add that inserts and then throws may leave unused private objects cached; tests require entry readiness to remain false. This matches the production lifetime rule that cache-backed assets cannot be discarded by an unsupported removal path.

These tests validate real managed orchestration against modeled native contracts. They do not validate the game's actual serializer, simulation, streaming, animations or rewards through engine execution. Serialized rehydration yielding a new pointer is deliberately rejected by strict ownership, so native rehydration behavior remains a gameplay candidate boundary.

Startup ordering regression: with the actual old StartOfFrameSystem.OnInit hook, this harness produced `native duplicate Add rejected: 370725232116713263`, `official content exported=False`, `pooled=0`. After the production hook moved to InitializeContentSystem.OnInit postfix, the same non-idempotent native model reports `official content exported=True`, `pooled=1`. Red/green outputs are stored in `artifacts/third-entry/runtime-init-order-red.txt` and `runtime-init-order-green.txt`. Repeated tests dispatch only the mod postfix because native content export itself is intentionally non-idempotent and normally runs once per storage.
