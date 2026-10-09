# Actual ThirdTrialSpawning regression

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
