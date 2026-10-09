# Actual ThirdTrialRuntime source, managed test harness

The project compiles the actual production Runtime, StorageBackend, StoragePolicy, FailurePolicy and both catalogs. It embeds both task manifests and a SecondPlaylist fixture copied from the read-only static extraction in `artifacts/third-entry/configuration.json`, record8619/source1509816363606571268. Its eight original entries, BossRush type, second SpawnPoint, fixed-point loot modifier and flags come from that data.

Only the EnemyRestorationRuntime asset preparation callback and native/Frame/Harmony APIs are faked. `AfterInit` and `BeforeRuleUpdate` are called through reflection; `Install`, `IsFrameReady`, `ResolveReadyPlaylist`, `TryGetNpc` and `Fail` use real Runtime source. There is no production test hook and no game/IL2CPP/Unity DLL reference. Fake source assets, cloned wrappers and caches have unique pointer identities; fake serializer performs an independent copy. The final check verifies no Il2Cpp/Unity assembly has loaded.

Run with the task SDK and local TEMP/TMP/DOTNET_CLI_HOME:

`dotnet run --project tests/ThirdTrialRuntimeTests/ThirdTrialRuntimeTests.csproj -c Release`

365 checks pass with zero build warnings/errors:

- Clones precisely the original second resource-manager source. Original source has an unchanged complete snapshot of Entries/Type/SpawnPoint/flags before and after success and each initialization failure.
- Ten fixed Arena entries contain each stage's full ordered EntryNuggetGuids; entry11 preserves independent native randomized Reward. Private source is Roguelite1 and third SpawnPoint. Second respite5/5, loot/healing/experience flags and 65536 loot modifier remain copied.
- Asset preparation happens first; all forty private dependencies and the shared Team precede attachment creation; all ten Trees precede all ten prepared Blocks; playlist registration happens last. Repeated OnInit makes no extra cache writes or Tree/Block/Playlist creations.
- Readiness requires the current-view flag, initialized storage identity, exact owned private source, all ten dynamic NPC/controller assets, all ten NPC cache identities, attachment blocks and private Tree OwnerGuid. Another Frame with uninitialized storage is rejected; another Frame sharing the initialized storage and same graph is accepted.
- Profile order mismatch rejects before initialization. Foreign source GUID/Path mismatch, a completely matching foreign deserialized source with a different identity, and native Add readback returning a new pointer are rejected. A previously owned source remains valid on repeat.
- Thirty-five injected initialization failures cover graph preparation, missing/mismatched dependencies, export, behavior-tree root/memory/Add, attachment shape/type/version/default refs/Add, template/serializer/source reward, static floor/full nugget/spawn, ownership collision, playlist Add/readback and bad source flags/respite. These disable entry readiness and NPC lookup. The failure rule prefix stops only this private source; first, original second, original third and unrelated/no source retain native rule execution.

An Add that inserts and then throws may leave unused private objects cached; tests require entry readiness to remain false. This matches the production lifetime rule that cache-backed assets cannot be discarded by an unsupported removal path.

These tests validate real managed orchestration against modeled native contracts. They do not validate the game's actual serializer, simulation, streaming, animations or rewards through engine execution. Serialized rehydration yielding a new pointer is deliberately rejected by strict ownership, so native rehydration behavior remains a gameplay candidate boundary.
