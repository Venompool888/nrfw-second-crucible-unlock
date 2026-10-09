using BowgunRepair;

int checks = 0, failures = 0;
void Check(bool value, string label) { ++checks; if (!value) { ++failures; Console.WriteLine("FAIL " + label); } }

// Reproduce the recorded startup: unrelated pending work never clears, but the
// missing bowgun action itself has no request. The old global guard blocks forever.
var startup = new StartupPolicy();
Check(!startup.Decide(1000, 11, 22, 33, false, false, true, false), "allow initial equipment to settle");
Check(startup.Decide(2100, 11, 22, 33, false, false, true, false), "unrelated pending request must not starve missing action");
startup.Requested(2100);
Check(!startup.Decide(2200, 11, 22, 33, false, false, true, false), "do not spam native requests");
Check(!startup.Decide(7200, 11, 22, 33, false, true, true, false), "wait for target action's real pending request");
Check(!startup.Decide(7200, 11, 22, 33, true, false, true, false), "do not recreate a loaded action");
Check(!startup.Decide(7200, 11, 22, 33, false, false, true, true), "do not reload while the bowgun action is playing");
Check(startup.Decide(7200, 11, 22, 33, false, false, true, false), "bounded retry after failed request");
startup.Requested(7200);
Check(startup.Decide(12300, 11, 22, 33, false, false, true, false), "third bounded attempt");
startup.Requested(12300);
Check(!startup.Decide(18000, 11, 22, 33, false, false, true, false), "cap retries at three");
Check(!startup.Decide(18000, 11, 44, 55, false, false, false, false), "new equipment gets settling period");
Check(startup.Decide(19100, 11, 44, 55, false, false, false, false), "new equipment resets retry cap");
startup.Reset();
Check(!startup.Decide(20000, 11, 44, 55, false, false, false, false), "map/hero reset requires fresh settling");

var shot = new ReleasePolicy();
shot.Begin(101, 201, new ulong[] { 301, 302 });
Check(!shot.Claim(301, 101, 201, true), "previously existing arrow cannot fire a new sound");
Check(!shot.Claim(401, 999, 201, true), "another player's nearby arrow cannot fire sound");
Check(!shot.Claim(401, 101, 999, true), "another weapon cannot fire sound");
Check(!shot.Claim(401, 101, 201, false), "prepared arrow has no firing sound");
Check(shot.Claim(401, 101, 201, true), "actual new owned released arrow triggers once");
Check(!shot.Claim(401, 101, 201, true), "repeated frames cannot double fire");
Check(!shot.Claim(402, 101, 201, true), "one sound per action even with another owned projectile");
shot.Begin(101, 201, new ulong[] { 401 });
Check(shot.Claim(402, 101, 201, true), "next shot rearms sound");
shot.Begin(101, 201, Array.Empty<ulong>());
shot.Cancel();
Check(!shot.Claim(403, 101, 201, true), "cancelled action cannot emit delayed sound");
shot.Begin(0, 0, Array.Empty<ulong>());
Check(!shot.Claim(404, 0, 0, true), "invalid owner/weapon cannot arm sound");

Check(RepairPolicy.CanMute(RepairPolicy.Action, RepairPolicy.Armament, true, 2099, RepairPolicy.CastGuid, .1f, 1.0333334f), "exact authored cast record is eligible");
Check(!RepairPolicy.CanMute(1, RepairPolicy.Armament, true, 2099, RepairPolicy.CastGuid, .1f, 1.0333334f), "other action stays unchanged");
Check(!RepairPolicy.CanMute(RepairPolicy.Action, 1, true, 2099, RepairPolicy.CastGuid, .1f, 1.0333334f), "other weapon stays unchanged");
Check(!RepairPolicy.CanMute(RepairPolicy.Action, RepairPolicy.Armament, false, 2099, RepairPolicy.CastGuid, .1f, 1.0333334f), "nonlocal view stays unchanged");
Check(!RepairPolicy.CanMute(RepairPolicy.Action, RepairPolicy.Armament, true, 1441, RepairPolicy.CastGuid, .1f, 1.0333334f), "preparation track stays unchanged");
Check(!RepairPolicy.CanMute(RepairPolicy.Action, RepairPolicy.Armament, true, 2099, "unknown", .1f, 1.0333334f), "unknown sound stays unchanged");
Check(!RepairPolicy.CanMute(RepairPolicy.Action, RepairPolicy.Armament, true, 2099, RepairPolicy.CastGuid, 1.5999908f, 1.0333334f), "old timing plugin must be removed first");
Check(!RepairPolicy.CanMute(RepairPolicy.Action, RepairPolicy.Armament, true, 2099, RepairPolicy.CastGuid, float.NaN, 1.0333334f), "invalid constraints fail closed");
Check(NativeAudioPolicy.NeedsCompanion("reinforcedBowgunView(Clone)",0,false), "native unbound target prefab needs audio companion before pooling callback");
Check(NativeAudioPolicy.NeedsCompanion("reinforcedBowgunView",NativeAudioPolicy.WeaponData,false), "bound target prefab remains eligible");
Check(!NativeAudioPolicy.NeedsCompanion("shortBowView(Clone)",0,false), "ordinary bow must not be changed");
Check(!NativeAudioPolicy.NeedsCompanion("reinforcedBowgunViewOther(Clone)",0,false), "similar prefab name cannot match");
Check(!NativeAudioPolicy.NeedsCompanion("reinforcedBowgunView(Clone)",111,false), "conflicting weapon identity cannot match");
Check(!NativeAudioPolicy.NeedsCompanion("reinforcedBowgunView(Clone)",0,true), "existing native companion must be preserved");
Check(NativeAudioPolicy.CanReplaceRaise(NativeAudioPolicy.OldRaiseGuid,1441,.0166667f,.6f), "known fire-dart preparation track can use native crossbow raise");
Check(!NativeAudioPolicy.CanReplaceRaise(RepairPolicy.CastGuid,1441,.0166667f,.6f), "other event must not be replaced as raise");
Check(!NativeAudioPolicy.CanReplaceRaise(NativeAudioPolicy.OldRaiseGuid,2099,.0166667f,.6f), "cast track must not be replaced as raise");
Check(!NativeAudioPolicy.CanReplaceRaise(NativeAudioPolicy.OldRaiseGuid,1441,float.NaN,.6f), "invalid preparation constraint fails closed");
var binding = new ShotBinding(101,201,501,301,401);
Check(binding.Matches(binding), "unchanged shot binding remains eligible");
Check(!binding.Matches(binding with { Item=202 }), "equipment switch rejects delayed release");
Check(!binding.Matches(binding with { WeaponView=302 }), "same item rebound to new weapon view rejects stale release");
Check(!binding.Matches(binding with { Hero=102 }), "new hero rejects old release");
Check(!binding.Matches(binding with { HeroView=502 }), "same entity rebound to new hero view rejects stale release");
Check(!binding.Matches(binding with { ActionView=402 }), "different action rejects old release");
Check(!default(ShotBinding).Matches(default), "unbound views cannot match");
Check(!RestorePolicy.CanRestore(false,true,true), "disable cannot unmute live action");
Check(!RestorePolicy.CanRestore(false,false,true), "interrupted but still playing/paused timeline remains muted");
Check(!RestorePolicy.CanRestore(false,true,false), "current action stays muted even after timeline ends");
Check(RestorePolicy.CanRestore(false,false,false), "ended inactive action can be cleaned without pool callback");
Check(RestorePolicy.CanRestore(true,true,true), "return/reuse boundary can restore saved fields");

int cell = 10;
var owned = new OwnedField<int>(()=>cell,v=>cell=v,20);
owned.Apply();
Check(cell==20 && owned.Pending, "owned replacement is recorded");
owned.Restore();
Check(cell==10 && !owned.Pending, "unchanged owned field restores original");
owned.Apply(); cell=30;
bool collided = false;
try { collided=owned.Restore(); } catch { }
Check(cell==30 && !owned.Pending && collided, "foreign changes are preserved and do not block cleanup");
cell=10;
bool failApply = true;
var partial = new OwnedField<int>(()=>cell,v=> { cell=v; if (failApply) throw new InvalidOperationException("after-write failure"); },20);
try { partial.Apply(); } catch { }
Check(cell==20 && partial.Pending, "partial native setter failure is tracked before write");
failApply=false; partial.Restore();
Check(cell==10 && !partial.Pending, "partial mutation rolls back on retry");
cell=10;
bool failRestore=false;
var retry = new OwnedField<int>(()=>cell,v=> { if (failRestore) throw new InvalidOperationException("temporary setter failure"); cell=v; },20);
retry.Apply(); failRestore=true;
try { retry.Restore(); } catch { }
Check(retry.Pending && cell==20, "failed restoration retains retry state");
failRestore=false; retry.Restore();
Check(!retry.Pending && cell==10, "restoration can recover after temporary failure");
var first = new OwnedField<int>(()=>cell,v=>cell=v,20);
first.Apply();
int secondCell=11;
var second = new OwnedField<int>(()=>secondCell,v=>secondCell=v,21);
second.Apply(); cell=99;
try { first.Restore(); } catch { }
second.Restore();
Check(cell==99 && secondCell==11 && !first.Pending && !second.Pending, "independent fields restore despite foreign event collision");
var nativeCell = new NativeReference(17);
var reference = new OwnedField<NativeReference?>(()=>nativeCell,v=>nativeCell=v,new NativeReference(19),(a,b)=>a?.Pointer==b?.Pointer);
reference.Apply(); nativeCell=new NativeReference(19);
reference.Restore();
Check(nativeCell?.Pointer==17 && !reference.Pending, "different managed wrapper for same native pointer remains owned");
reference.Apply(); nativeCell=null;
Check(reference.Restore() && nativeCell==null && !reference.Pending, "foreign null native reference is preserved");
cell=10;
var afterWriteRestore = new OwnedField<int>(()=>cell,v=> { cell=v; if (v==10) throw new InvalidOperationException("restore failed after write"); },20);
afterWriteRestore.Apply();
try { afterWriteRestore.Restore(); } catch { }
Check(cell==10 && afterWriteRestore.Pending, "restore failure after native write remains retryable");
afterWriteRestore.Restore();
Check(!afterWriteRestore.Pending && cell==10, "already restored value resolves retry without another native write");
int published=0; bool offhand=false, failSecond=true;
var componentSlot = new OwnedField<int>(()=>published,v=>published=v,123);
var flagSlot = new OwnedField<bool>(()=>offhand,v=> { offhand=v; if (failSecond) throw new InvalidOperationException("publish flag failed"); },true);
try { componentSlot.Apply(); flagSlot.Apply(); } catch { }
failSecond=false;
flagSlot.Restore(); componentSlot.Restore();
Check(published==0 && !offhand, "partial companion publish restores both fields before child destruction");
Check(BlockInputPolicy.ShouldRoute(RepairPolicy.Armament,true,true,true,ControlStyle.KeyboardMouse), "keyboard native block action routes bowgun");
Check(BlockInputPolicy.ShouldRoute(RepairPolicy.Armament,true,true,true,ControlStyle.Gamepad), "Xbox and PlayStation both use normalized gamepad block");
Check(!BlockInputPolicy.ShouldRoute(RepairPolicy.Armament,true,true,true,ControlStyle.None), "no input style cannot route");
Check(!BlockInputPolicy.ShouldRoute(123,true,true,true,ControlStyle.KeyboardMouse), "other offhand weapons keep native block behavior");
Check(!BlockInputPolicy.ShouldRoute(RepairPolicy.Armament,false,true,true,ControlStyle.KeyboardMouse), "locked cinematic/menu/gameplay does not route");
Check(!BlockInputPolicy.ShouldRoute(RepairPolicy.Armament,true,false,true,ControlStyle.KeyboardMouse), "unbound local hero/equipment does not route");
Check(!BlockInputPolicy.ShouldRoute(RepairPolicy.Armament,true,true,false,ControlStyle.KeyboardMouse), "releasing block does not synthesize another shot");
Check(BlockInputPolicy.ShouldHideHudBlock(true,true,true,ControlStyle.KeyboardMouse), "keyboard bowgun consumes HUD block overlay like gamepad");
Check(BlockInputPolicy.ShouldHideHudBlock(true,true,true,ControlStyle.Gamepad), "existing controller HUD suppression remains eligible");
Check(!BlockInputPolicy.ShouldHideHudBlock(false,true,true,ControlStyle.KeyboardMouse), "HUD outside local bowgun scope remains native");
Check(!BlockInputPolicy.ShouldHideHudBlock(true,true,false,ControlStyle.KeyboardMouse), "special attack and other HUD buttons survive");
Check(!BlockInputPolicy.ShouldHideHudBlock(true,false,true,ControlStyle.KeyboardMouse), "false native button remains false");
Console.WriteLine($"{checks} checks; {failures} failures");
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed record NativeReference(long Pointer);
