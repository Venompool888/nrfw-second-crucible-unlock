# RaisedMinion teleport / UseNavData: static notes (public 22928553)

Source: GameAssembly.dll disassembled with capstone. RVAs come from the Cpp2IL dummy DLL `AddressAttribute` in MelonLoader `cpp2il_out/moon.quantum.forsaken.dll`. This is a read-only static analysis; nothing was checked at runtime.

## Offsets
- RaisedMinionComponent (unmanaged struct): IsFollowing +0x04, RaiseActionGuid +0x08, Owner +0x10, FollowStartDistance +0x38, FollowStopDistance +0x40, MaxDistanceBeforeTeleport +0x50, StuckDuration +0x70, LastCheckedPosition +0x98.
- RaiseMinionActionData (managed object, includes header): FollowStartDistance 0x1A0, MaxDistanceBeforeTeleport 0x1B8.
- PathFollowingComponent: ControlledEntityRef +0x10, OwnerRef +0x18, CurrentRequest +0x548, request status +0x550.

## RaisedMinionSystem.Update 0x5B497F0 (two RaisedMinionTeleported call sites)
- 0x5B49E43: `sqrt(3D dist(owner, minion)) > comp+0x50`. Jumps to the far teleport at 0x5B4AAB9.
- 0x5B49E5B: `|owner.y - minion.y| > 3.0` AND `horizontal dist > comp+0x38`. The owner must also have a component with byte +0xC bit 0x10 set (type unidentified). Jumps to the far teleport at 0x5B4AAC0.
- 0x5B49F51 to 0x5B49FA2 (stuck timer): the timer runs while `IsFollowing`, or the minion has a live AiController attack target (+0x30), or `horizontal dist > FollowStartDistance`.
  - If the minion is still within 0.5 m (0x8000) of LastCheckedPosition, `StuckDuration += DeltaTime`. Otherwise both reset.
  - At `StuckDuration >= 3.0` (0x30000), it teleports to `ownerPos - ownerForward * FollowStopDistance`, ground-snapped by a VerticalRaycast from 2 m above (10 m length). The event fires at 0x5B4A229.
- The far teleport targets the same point, ground-snapped with Physics.Raycast down. The event fires at 0x5B4AE2D.
- Both paths write Transform3D position directly and do not touch UseNavDataComponent.

## Where the component values come from
RaiseMinionActionData.SpawnMinion 0x5A67800 copies the asset fields into a stack struct at 0x5A67E63 to 0x5A67EE0 (asset 0x1B8 goes to comp+0x50). It adds the component once at 0x5A680AA, only if the entity does not already have one. Update only reads the component copy.

## UseNavDataComponent
- AttachmentAPI.ResolveNavDataAttachment 0x5F3D690 calls `Frame.Get<UseNavDataComponent>` unconditionally, which throws when the component is missing. It then reads InstanceDataStorage at NavDataIndex.
- TryResolveNavDataAttachment 0x5F3D760 returns null instead.
- PathFollowingComponent.SearchPath 0x5BCC730 passes the entity to it at 0x5BCC97F, only if `distSq(entity, target) > request+0x38` (0x5BCC969).
  - A null result goes to 0x90FAE0 (NullReferenceException) at 0x5BCC98A.
  - `!IsValid` sets status=2 (0x5BCCBDA).
- The entity passed is PathFollowingComponent.ControlledEntityRef (PathFollowingSystem.Update 0x5BCDBAA, then SearchPath 0x5BCDFA0, then 0x5BCE0ED).
- Writers of UseNavDataComponent:
  - SpawningAPI.SpawnEntity 0x5B47064: copies the component from the source/scope entity. Otherwise it uses NavMeshAPI.GetActiveNavMeshFor(spawnPos); if that returns null, no component is added.
  - AiControllerData.BindControllerToEntity 0x5B06F40: copies the pawn's component to the controller, or removes the controller's component if the pawn lacks one (0x5B0712E).
  - NavMeshTrackerSystem.Update 0x5B1F950: sets the component when the entity leaves its navmesh and another active one is found.
