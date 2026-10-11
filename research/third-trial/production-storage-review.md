# Production storage bridge review

2026-10-10. Read-only review of `ThirdTrialStorageBackend.cs`; no game/DLL execution. Parent notified of actionable findings immediately. The source was still being edited during review; findings refer to the first 10,223-byte version unless updated below.

## Important: native cache dictionary values are one-based refs

All nine helpers (`Npc`, `Controller`, `Host`, `View`, `Team`, `Interaction`, `Spawner`, `Tree`, `Block`) originally called `m_lookup.TryGetValue(id,out i)` followed by `m_storage[i]`.

This is incorrect. Generic Storage.Add at VA `0x184fa74d0` obtains the old list Count, inserts `Count+1` into the dictionary at `0x184fa753b`/`0x184fa755c`, and appends the object at the original zero-based Count. Strong typed Get(NpcDataRef) at `0x185984625` decrements the ref before indexing the list. Generic TryLookup returns the dictionary value unchanged. Thus direct list indexing must use `i-1` after checking `i>0`; alternatively use `Storage.Get(i)` or typed ref/Get. Uncorrected code reads the next object or goes out of bounds and cannot safely complete preflight or readiness.

Evidence is in `native.txt`, generic Add/TryLookup tail sections and METHOD 44441. This finding was sent to root as Important before further review.

Update: the current production helpers use `m_storage[i-1]`, correcting the observed bug. Bounds checking remains a defensive improvement; a corrupt cache index presently raises an exception and is caught by initialization failure gating.

## Dependency closure and type conclusions

The bridge correctly recognizes every serialized attachment asset field: Controller, EntityData, NpcData, EntityView, InteractionController, TeamData, Spawner. It exports a fresh block, uses the restored NPC GUID as default Id, ensures all nonzero raw dependencies before FromSerializedNpcBlock, and validates resolved pointers. Controller/host/view/NPC GUID checks prevent silently registering a mismatched graph. The catalog enforces a nonzero shared TeamGuid, so the unconditional TeamData.Get in Matches is consistent with these ten profiles.

The additional controller Tree cache is essential and is now represented: CreateBehaviorTree receives the current Frame as IAssetResolutionContext, OwnerGuid is explicitly the private controller GUID, Add(Tree) uses that key, and GetTreeRef(frame).Get(frame) reads back the same Tree pointer. AiControllerData.OnCreateEntityInstance resolves this Tree and allocates instance memory; it does not generate a missing tree.

Private EnemyWeaponData is not a WeaponData subtype and has no typed runtime storage. AI combat-tree generation and humanoid weapon lookup use AssetRefEnemyWeaponData.Get through IAssetResolutionContext. Adding a WeaponData cache entry would be a type error. The bridge currently validates the weapon through Frame.FindAsset; an explicit `ai.Weapon.Get(frame)` pointer readback would further verify the exact resolution path. Existing frame asset registration already validates expected profile bindings, so this is a defensive closure check rather than evidence that a current mismatch exists.

## Failure and lifecycle limitations

Native storage Add has no removal API in the observed interface; earlier successful additions remain after a later failure. The production policy intentionally seals the entrance on failure and retains owned objects. This is safer than disposing backing assets while storage still references them, but must be honored by runtime cleanup: do not remove private Frame assets or view wrappers after the storage has retained their data if that storage can still be used.

The static ownership map keys storage.Pointer rather than Frame.Pointer, matching shared InstanceDataStorage across simulation frames. Retained managed wrappers keep plugin-owned trees/default blocks alive. Native pointer reuse across a genuinely new storage lifetime is still a reason to compare existing cache pointers; present Preflight does so and rejects foreign collisions. This review does not establish actual lifecycle/rollback behavior in a live session.

No live spawn, controller execution, AI combat, clear conditions, rewards, failure/reentry or view rendering was tested. Static verification and compilation must not be reported as game validation.

## Failure after some NPCs have already spawned

The original failure implementation blocked subsequent private-source NPC spawns but kept the official clear-condition updater running. `UpdateCheckFloorClear`[53071] enumerates existing AI entities in the containing integration group, tracks the maximum total observed, and computes progress from remaining living entities. With a partial successful population followed by a blocked spawn, killing the successful population can yield full progress even though some required spawn callbacks failed. An entirely empty room with no observed total returns zero progress, so this is specifically a partial-spawn failure path.

Recommended precise guard: only for the private third source, when ThirdTrialRuntime has latched failure, skip `InfiniteDungeonRoguelitePlaylistRuleDirector.UpdateActiveRun(Frame)`[53091, VA0x185c9a7c0]. Its body checks active director, reads rule state, calls ShouldCheckEnemyKilledCondition, calls UpdateCheckFloorClear at `0x185c9a83b`, and publishes CrucibleFloorProgressUpdate at `0x185c9a877`. It contains no player-death, respawn, exit, or run-finalization calls.

Update: production installs that exact prefix. The readiness condition also includes EnemyRestorationRuntime.CurrentViewsReady, so view failure pauses private-source clear progress. Healthy callbacks return early; unavailable callbacks identify the exact source. GetSourcePlaylist[42660,VA0x185f4c800] is not a TryGet: it accesses serialization's StateEntity and a component Get, throwing when missing. Unknown-source lookup failure should pass to the native active-source guard without permanently disabling third readiness; this last catch-path change was sent to root during review.

Final re-read confirms the catch returns true without latching ThirdTrialRuntime.Fail. The identified indexing, setup ordering, stale-context readiness and partial-spawn clear-progress issues are addressed in the reviewed production source. There is no remaining blocker identified by this static storage/runtime review; native integration behavior remains untested.

Death/exit evidence outside that guarded method:

- `InfiniteDungeonDirector.OnPlayerDied`[53028, VA0x185c95ff0] calls CheckAutoRespawnDeadPlayers at `0x185c960cc`, SetDungeonState(3) at `0x185c9611d`, and FinalizeCerimCryptRun at `0x185c96139`.
- `OnCharacterDisengaged`[53011, VA0x185c93560] calls ClearPlayerData at `0x185c93585` and CheckAutoRespawnDeadPlayers at `0x185c93614`.
- `InfiniteDungeonDirector.Update`[52995] independently detects no remaining player in the dungeon and routes to SetDungeonState(3) at `0x185c90b79` through `0x185c90a46`.
- Native fast-travel permission remains unchanged: AllowTeleportToSacramentFromFastTravelPoint[52994] permits the entrance/outside, not arbitrary normal combat-room fast travel. The proposed guard preserves existing exit/death mechanics; it does not create a new exit action.

## Latest root integration observations

Entry hooks are now installed before enemy/runtime/spawn hooks. Their delegates report unavailable until assets and runtime are ready, retaining targeted false/null behavior after later setup failure. If entry-hook installation itself fails and rolls back, the mod cannot promise to block every vanilla Horde entry; the setup error is explicitly module-unavailable.

The main-thread view callback now compares each cached wrapper's global AssetObject pointer with the same context's cached data before publishing managed readiness. This closes the observed stale-owned-context path. Simulation callbacks consume only the readiness flag and Frame/storage objects.

Npc FromSerializedNpcBlock checks refs with unsigned `cmp value,0`/`jbe`, so negative nonzero dynamic GUIDs are resolved. It copies CharacterType and CharacterVersion directly from the storage-resolved NPC at `0x185beefa5` and `0x185beefd2`; the latest Matches validates both and the profile CharacterType. Tree now also checks RootNode and logs InstanceMemorySize. Creation/size remains unexecuted static evidence.

The native backend agent independently verified `AssetRefNuggetData` and nested AssetGuid are true value types with no reference fields (eight bytes total). Il2CppStructArray setter uses `AsSpan().get_Item` followed by `stobj !0`, so the playlist's explicit index assignment is valid for that array; it does not have the class-projected nested-struct issue seen in weapon action/unlock arrays.
