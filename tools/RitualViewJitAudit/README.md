# RitualView JIT shape audit

This standalone net6.0 executable reads a candidate plugin using PEReader and System.Reflection.Metadata. It has no game, Unity, IL2CPP, production-project or third-party references; it never loads the inspected DLL or invokes its code. The opcode dictionary reflects only the framework's System.Reflection.Emit.OpCodes. No game is started or controlled.

The gate checks the emitted callback, not source text:

- Exactly one `CrucibleUnlock.RitualViewRepair.AfterGetActionView` with unchanged static, nongeneric `void(EntityView __instance, ActionData __0, ref ActionView __result)` ABI and parameter names.
- Callback IL at most 128 bytes, no native generic collection locals and no exception/repair regions.
- Direct call to the inspected `RepairOffering` method, whose implementation flags include NoInlining and exclude NoOptimization for this experiment.
- No `Il2CppSystem.Collections.Generic.List<T>.Enumerator` in any RitualViewRepair method or descendant type method signature, decoded local, or instruction operand. MemberRef/MethodSpec signatures and nested TypeRef/TypeSpec owners are decoded, including GetEnumerator returns, Current and MoveNext calls. Managed `System.Collections.Generic.List<T>.Enumerator` is allowed.
- No native `Il2CppArray*` method signatures, locals or decoded instruction operands in those methods. R11 extends the earlier enumerator-only gate: indexed access also imports the closed array type during JIT.
- Install calls RuntimeHelpers.PrepareMethod, and all such call offsets precede the first TrialRepairLog.Patch call offset in its emitted IL. This checks lexical IL order; it does not execute PrepareMethod or claim its runtime success.

From the task worktree, use the project SDK at `../crucible-mod/.dotnet/dotnet.exe`. Set TEMP/TMP and DOTNET_CLI_HOME to task-local directories, and set DOTNET_GENERATE_ASPNET_CERTIFICATE=false and DOTNET_CLI_TELEMETRY_OPTOUT=1 before building/running.

```powershell
& ../crucible-mod/.dotnet/dotnet.exe build tools/RitualViewJitAudit/RitualViewJitAudit.csproj -c Release --nologo
& ../crucible-mod/.dotnet/dotnet.exe tools/RitualViewJitAudit/bin/Release/net6.0/RitualViewJitAudit.dll dist/third-trial-20261010-r2/CrucibleUnlock.dll tools/RitualViewJitAudit/r2-red.json
& ../crucible-mod/.dotnet/dotnet.exe tools/RitualViewJitAudit/bin/Release/net6.0/RitualViewJitAudit.dll src/CrucibleUnlock/bin/Release/CrucibleUnlock.dll tools/RitualViewJitAudit/r3-green.json
```

Exit 0 is green, 1 is a failed shape gate and 2 indicates incorrect arguments. Malformed input is rejected by PE/metadata decoding; the gate never substitutes a runtime load.

On 2026-10-10 the expanded tool built with zero warnings/errors, then the frozen R2 was tested before the final revised R3:

| Observation | R2 RED | R3 GREEN |
|---|---:|---:|
| Exit status | 1 | 0 |
| Callback IL bytes | 2434 (`0x982`) | 71 (`0x47`) |
| Callback locals | Includes native enumerator | None |
| Native Enumerator local/call references | 8 | 0 |
| Il2CppArrayBase GetEnumerator calls | 2 | 0 |
| RepairOffering | Missing | NoInlining, 2384 IL bytes |
| Install PrepareMethod/Patch IL offsets | Missing / `005a` | `0044` / `00a2` |
| Gate failures | 17 | 0 |

R2 had four forbidden occurrences in AfterGetActionView for TimelineItemRecord and four in CopyResolver for SerializedDynamicDataLinkItem: each local plus GetEnumerator, Current and MoveNext. Therefore the red run verifies the audit can distinguish the released failing IL shape from the experimental shape; it is not an always-green source assertion.

The expanded red run additionally detects native-array GetEnumerator calls for GeneralInteractableView at IL_011d and InteractionView at IL_018e. R2 has no PrepareMethod call. The final R3 removes both array enumerators and calls PrepareMethod at IL_0044, before Patch at IL_00a2.

R2 input SHA256: `b22e5c99329645952c54b29f10127c7da8189212be764971151ff6dc0c4a4d60`.

Final R3 inspected build SHA256: `120bba2fa5dbb31f03c541d66b5d6812eaaac9decd63f8bf5cdff00664b9d70f`. This supersedes the earlier R3 audit with hash `e6648208d583b197a822708c379a99c8caa431a5bb2388e1c60dbb874e070095`; the JSON files now describe the expanded gate and final build.

The detailed JSON reports record the input, hash, method tokens, implementation flags, local signatures, failures and scope limitation. AfterGetActionView remains token `0x06000245`; the new RepairOffering is `0x06000246` in this R3 build.

**A green result does not establish that the CoreCLR JIT crash is fixed.** The offering helper remains a large body and still uses native collection types through indexed access. This tool does not execute the callback or helper, exercise Harmony, check native ABI, or validate offering/rollback behavior in a game session. Live crash confirmation requires the user to run the candidate and provide fresh evidence.

## R11 extension after the R10 startup crash

The R10 dump names `RuntimeHelpers.PrepareMethod -> RitualViewRepair.Install`, while its RepairOffering canonical IL matches R9. The dump does not identify the faulting MethodTable. The two closed native-array types are a candidate type-load trigger, even though the R3 enumerator gate passed. The extended gate rejects frozen R10 with eight references: two locals, two component queries, two item reads and two Length reads. R11 traverses the same shipped statue hierarchy with Transform childCount/GetChild and non-generic GetComponent(Type), including self and inactive children. The shipped layout must separately establish at most one target component per GameObject; this is not a general equivalence proof for arbitrary Unity hierarchies.

All preparation remains before Patch. Startup logging is immediate because queued OnUpdate diagnostics can be lost if the CLR aborts first. Exact live compilation and actual offering/animation playback require a fresh candidate session.
