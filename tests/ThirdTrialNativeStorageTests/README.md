# Real storage backend source, managed tests

This project directly links the production `ThirdTrialStorageBackend.cs`, `ThirdTrialStoragePolicy.cs` and `EnemyRestorationCatalog.cs`. It embeds the actual ten-profile manifest. It has no game, IL2CPP or Unity DLL reference. The namespace-compatible fake APIs are defined within this test executable; the final check also verifies no Il2Cpp/Unity assembly has loaded.

The fake storage uses native-style 1-based `m_lookup` values and zero-based lists. Each Add rejects an already occupied GUID. Raw nonzero refs must find the exact typed dependency in runtime storage before attachment creation. Fake assets are distinct pointer-bearing objects, so source DB identity and foreign cache collisions remain meaningful. Each fake Frame/storage gets a unique pointer, exercising production static ownership without sharing fixtures.

Run `dotnet run --project tests/ThirdTrialNativeStorageTests/ThirdTrialNativeStorageTests.csproj -c Release` with the project SDK and task-local TEMP/TMP/DOTNET_CLI_HOME.

Covered by 150 checks:

- 40 private dependencies, one shared Team, ten independent nonempty rooted trees with exact private Controller OwnerGuid, ten independent blocks and per-profile readiness readback.
- Typed closure of all raw nonzero refs, including optional interaction/spawner, exact NPC/CharacterType/CharacterVersion and default placement/raw-zero refs.
- Late static dependency absence, malformed exported graph, mismatched weapon Get-vs-FindAsset and nine foreign cache collision categories reject before any plugin cache write.
- Repeated whole-batch Ensure appends nothing and recreates no Tree/Block, while duplicate native Add is rejected.
- NPC export, tree construction/root/memory/Add, attachment export/type/default-ref/match and block Add failures keep the test entrance gate unavailable. A block Add that inserts and then throws remains unavailable even if all ten caches became populated.

The test entrance gate is the caller contract: it starts false and is set true only after the real storage policy returns successfully. These tests exercise the production storage backend, rather than the actual loader/Harmony entry lifecycle or native engine.

The offline mutation script at `artifacts/third-entry/check_storage_lookup_mutation.py` writes a source copy only to artifacts and changes the first NPC lookup from `[i-1]` to `[i]`. Running the same real-source tests against that copy fails with `Index was out of range`; the script checks the production source bytes stayed unchanged. `BackendSource` is solely a test-project Compile input override, not a production hook. The default project always compiles production source. Mutation results are recorded in `storage-index-mutation-result.json`; normal tests were rerun successfully afterward.
