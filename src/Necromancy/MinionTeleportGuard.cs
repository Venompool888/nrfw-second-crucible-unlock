using System;
using System.Collections.Generic;
using System.Threading;
using Il2CppQuantum;
using MelonLoader;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private static bool _stuckTeleportInCombat, _heightTeleportInRange;
        private static volatile bool _stuckHoldOff;
        private static int _stuckResets, _stuckLogs, _stuckHoldMaxSeconds, _heightLogs;
        private static float _stuckHoldNearRadius, _heightHoldMaxDy;
        private static readonly HashSet<ulong> TeleportMinions = new(); // sim thread; no visual-cache timeout
        private static readonly Dictionary<ulong, (long Duration, bool Released)> HeldStuck = new();
        private static readonly HashSet<ulong> HeldHeight = new();
        private static Frame _simFrame; // teleport events fire inside RaisedMinionSystem.Update

        // FollowStartDistance values raised for this frame (sim thread only). Restored by the postfix; if Update threw and
        // the postfix did not run, the next prefix restores them first. No Harmony __state/finalizer: neither is proven on
        // detoured IL2CPP methods in this runtime.
        // Field: 0 = FollowStartDistance (height branch), 1 = MaxDistanceBeforeTeleport (far branch).
        private static readonly List<(EntityRef Minion, int Field, long Original)> FollowStartRestore = new();

        private static readonly Dictionary<ulong, long> LastTeleportLog = new();
        private static int _teleportLogs, _raisedTicks;
        private static int _raisedUpdateHit, _minionSpawnedHit, _minionTeleportedHit;

        private static void InstallTeleportGuard(MelonPreferences_Category category, HarmonyLib.Harmony harmony)
        {
            var stuckEntry = category.CreateEntry("minion_stuck_teleport_in_combat", false,
                description: "true = vanilla stuck teleports; false = hold near the owner and while off-follow within teleport range, subject to the hold cap outside the near radius.");
            _stuckTeleportInCombat = stuckEntry.Value;
            _stuckHoldMaxSeconds = category.CreateEntry("minion_stuck_hold_max_seconds", 10,
                description: "Outside the near radius, seconds to hold stuck teleports without a hit in the last 5 seconds; 0 = unlimited.").Value;
            _stuckHoldNearRadius = category.CreateEntry("minion_stuck_hold_near_radius", 10f,
                description: "3D distance in meters from the owner within which stuck teleports are always held when stuck hold is enabled; 0 = disabled.").Value;
            _heightHoldMaxDy = category.CreateEntry("minion_height_hold_max_dy", 8f,
                description: "Height difference in meters up to which far/height/stuck pull-backs are held (with horizontal distance <= the teleport distance); beyond it vanilla applies; 0 = no height limit.").Value;
            _heightTeleportInRange = category.CreateEntry("minion_height_teleport_in_range", false,
                description: "true = vanilla height teleports; false = suppress them within the minion's teleport range.").Value;

            // Co-locate RaisedMinionSystem.Update Prefix + Postfix under the same Harmony owner (NRFW.Necromancy.MinionLifecycle).
            try
            {
                PatchPrefixPostfix(harmony, typeof(RaisedMinionSystem), "Update", new[] { typeof(Frame) },
                    nameof(BeforeRaisedUpdate), nameof(AfterRaisedUpdate));
                Write("minion update teleport hooks installed");
            }
            catch (Exception e) { Write("minion update teleport hooks FAILED " + e.GetType().Name + ": " + e.Message); }

            try
            {
                Patch(harmony, typeof(Frame.FrameEvents), "RaisedMinionSpawned",
                    new[] { typeof(EntityRef), typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(AssetGuid) },
                    nameof(AfterMinionSpawned), typeof(EventRaisedMinionSpawned));
                Write("minion spawn registration installed");
            }
            catch (Exception e) { Write("minion spawn registration FAILED " + e.GetType().Name + ": " + e.Message); }

            Write("minion_stuck_teleport_in_combat=" + _stuckTeleportInCombat);

            try
            {
                Patch(harmony, typeof(Frame.FrameEvents), "RaisedMinionTeleported",
                    new[] { typeof(EntityRef), typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(AssetGuid) },
                    nameof(AfterMinionTeleported), typeof(EventRaisedMinionTeleported));
                Write("minion teleport log installed");
            }
            catch (Exception e) { Write("minion teleport log FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        private static unsafe void BeforeRaisedUpdate(Frame __0)
        {
            if (FollowStartRestore.Count > 0) RestoreFollowStartDistances(__0);
            try
            {
                FirstHit(ref _raisedUpdateHit, nameof(BeforeRaisedUpdate));
                if (!_enabled || __0 == null) return;
                _simFrame = __0;
                if (_stuckHoldOff || (_stuckTeleportInCombat && _heightTeleportInRange)) return;
                bool verified = __0.IsVerified;
                List<ulong> gone = null;
                foreach (ulong raw in TeleportMinions)
                {
                    var minion = new EntityRef { Raw = raw };
                    if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, __0))
                    {
                        if (verified) (gone ??= new()).Add(raw);
                        continue;
                    }
                    var comp = EntityRefExtensions.Get<RaisedMinionComponent>(minion, __0);
                    if (comp == null) continue;
                    long before = comp->StuckDuration.RawValue;
                    if (before == 0) HeldStuck.Remove(raw);
                    bool checkStuck = !_stuckTeleportInCombat && before > 0;
                    if (_heightTeleportInRange && !checkStuck) continue;
                    var ownerTransform = GetSimTransform(__0, comp->Owner);
                    var minionTransform = GetSimTransform(__0, minion);
                    if (ownerTransform == null || minionTransform == null) continue;
                    var owner = ownerTransform->RawPosition;
                    var pos = minionTransform->RawPosition;
                    double dx = (owner.X.RawValue - pos.X.RawValue) / 65536.0,
                           dy = (owner.Y.RawValue - pos.Y.RawValue) / 65536.0,
                           dz = (owner.Z.RawValue - pos.Z.RawValue) / 65536.0;
                    double horizSq = dx * dx + dz * dz;
                    double d3 = Math.Sqrt(horizSq + dy * dy);
                    double horiz = Math.Sqrt(horizSq);
                    double maxDist = comp->MaxDistanceBeforeTeleport.RawValue / 65536.0;
                    // Maintainer 2026-11-28: judge by horizontal distance (<= teleport distance) and height difference
                    // (<= minion_height_hold_max_dy) instead of the native 3D distance; beyond either, vanilla applies.
                    bool withinRange = NecromancyPolicy.InHoldRange(horiz, dy, maxDist, _heightHoldMaxDy);
                    long start = comp->FollowStartDistance.RawValue;
                    if (!_heightTeleportInRange && withinRange && d3 > maxDist)
                    {
                        FollowStartRestore.Add((minion, 1, comp->MaxDistanceBeforeTeleport.RawValue));
                        comp->MaxDistanceBeforeTeleport.RawValue = (long)((d3 + 1.0) * 65536.0);
                    }
                    if (!_heightTeleportInRange && withinRange && Math.Abs(dy) > 3.0 && horiz > start / 65536.0)
                    {
                        FollowStartRestore.Add((minion, 0, start));
                        comp->FollowStartDistance.RawValue = (long)((horiz + 1.0) * 65536.0);
                        if (verified && HeldHeight.Add(raw) && Interlocked.Increment(ref _heightLogs) <= 100)
                            Write("height-teleport held entity=" + NecromancyPolicy.EntityLabel(raw) +
                                  " dy=" + dy.ToString("0.#") + " horiz=" + horiz.ToString("0.#"));
                    }
                    else if (verified) HeldHeight.Remove(raw);

                    bool near = _stuckHoldNearRadius > 0 && d3 <= _stuckHoldNearRadius && (_heightHoldMaxDy <= 0 || Math.Abs(dy) <= _heightHoldMaxDy);
                    bool following = comp->IsFollowing.Value != 0;
                    if (!checkStuck || (!near && !withinRange)) continue;
                    HeldStuck.TryGetValue(raw, out var held);
                    if (!near) held.Duration += __0.DeltaTime.RawValue;
                    long? lastHit = MinionLastDamage.TryGetValue(raw, out long hit) ? hit : null;
                    if (!NecromancyPolicy.ShouldHoldStuckTeleport(following, withinRange, near,
                        held.Duration, _stuckHoldMaxSeconds, __0.TimeSinceStart.RawValue, lastHit))
                    {
                        if (!held.Released)
                            Write("stuck-teleport released (no progress " + (held.Duration / 65536.0).ToString("0.##") +
                                  "s, no hits) entity=" + NecromancyPolicy.EntityLabel(raw));
                        HeldStuck[raw] = (held.Duration, true);
                        continue;
                    }
                    if (near) HeldStuck.Remove(raw);
                    else HeldStuck[raw] = held;
                    comp->StuckDuration = default;
                    int n = Interlocked.Increment(ref _stuckResets);
                    if (verified && before >= (2L << 16) && Interlocked.Increment(ref _stuckLogs) <= 100)
                        Write("stuck-teleport held (" + (near ? "near owner" : following ? "following in range" : "off-follow in range") + ") entity=" +
                              NecromancyPolicy.EntityLabel(raw) + " stuck=" + (before / 65536.0).ToString("0.##") + "s resets=" + n);
                }
                if (gone != null) foreach (ulong raw in gone)
                {
                    TeleportMinions.Remove(raw);
                    HeldStuck.Remove(raw);
                    HeldHeight.Remove(raw);
                    MinionLastDamage.TryRemove(raw, out _);
                }
            }
            catch (Exception e)
            {
                _simFrame = null;
                BreakTeleportHolds(e);
            }
        }

        private static void AfterRaisedUpdate(Frame __0)
        {
            try
            {
                if (FollowStartRestore.Count > 0) RestoreFollowStartDistances(__0);
                if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _raisedTicks) == 1) Write("RaisedMinionSystem running");
            }
            catch (Exception e) { BreakTeleportHolds(e); }
            finally { _simFrame = null; }
        }

        private static unsafe void RestoreFollowStartDistances(Frame __0)
        {
            var pending = FollowStartRestore;
            Exception failure = null;
            for (int i = pending.Count - 1; i >= 0; --i)
            {
                var (minion, field, original) = pending[i];
                try
                {
                    if (__0 != null && EntityRefExtensions.Has<RaisedMinionComponent>(minion, __0))
                    {
                        var c = EntityRefExtensions.Get<RaisedMinionComponent>(minion, __0);
                        if (field == 1) c->MaxDistanceBeforeTeleport.RawValue = original;
                        else c->FollowStartDistance.RawValue = original;
                    }
                    pending.RemoveAt(i);
                }
                catch (Exception e) { failure ??= e; }
            }
            if (failure != null) BreakTeleportHolds(failure);
        }

        private static void BreakTeleportHolds(Exception e)
        {
            _stuckHoldOff = true;
            Write("teleport-hold ERROR " + e.GetType().Name + ": " + e.Message + "; teleport holds turned off, rest of the plugin unaffected");
        }

        private static unsafe TransformComponent* GetSimTransform(Frame f, EntityRef entity) =>
            entity.IsValid && EntityRefExtensions.Has<TransformComponent>(entity, f)
                ? EntityRefExtensions.Get<TransformComponent>(entity, f) : null;

        private static unsafe bool OffFollowWithinRange(RaisedMinionComponent* comp, double ownerDist) =>
            comp->IsFollowing.Value == 0 && ownerDist <= comp->MaxDistanceBeforeTeleport.RawValue / 65536.0;

        private static void AfterMinionSpawned(EntityRef __0, AssetGuid __2)
        {
            try
            {
                FirstHit(ref _minionSpawnedHit, nameof(AfterMinionSpawned));
                if (!_enabled || !__0.IsValid) return;
                Minions[__0.Raw] = Environment.TickCount64;
                if (!_stuckHoldOff && (!_stuckTeleportInCombat || !_heightTeleportInRange)) TeleportMinions.Add(__0.Raw);
                TrackBossSpawn(__0, __2);
            }
            catch (Exception e) { Write("spawn-register ERROR " + e.GetType().Name + ": " + e.Message); }
        }

        // Which native branch fired (static analysis T-011): far = 3D owner distance > comp.MaxDistanceBeforeTeleport;
        // height = |dy| > 3 m and horizontal > comp.FollowStartDistance; stuck = StuckDuration >= 3 s (not yet reset
        // when the event is raised). Read-only.
        private static unsafe string ClassifyTeleport(EntityRef minion, Il2CppPhoton.Deterministic.FPVector3 from)
        {
            var f = _simFrame;
            if (f == null || !EntityRefExtensions.Has<RaisedMinionComponent>(minion, f)) return "state=?";
            var comp = EntityRefExtensions.Get<RaisedMinionComponent>(minion, f);
            if (comp == null) return "state=?";
            var t = GetSimTransform(f, comp->Owner);
            string where = " offFollowWithinRange=?";
            string branch = comp->StuckDuration.RawValue >= (3L << 16) ? "stuck" : "?";
            if (t != null)
            {
                var owner = t->RawPosition;
                double dx = (from.X.RawValue - owner.X.RawValue) / 65536.0, dy = (from.Y.RawValue - owner.Y.RawValue) / 65536.0,
                       dz = (from.Z.RawValue - owner.Z.RawValue) / 65536.0;
                double d3 = Math.Sqrt(dx * dx + dy * dy + dz * dz), dh = Math.Sqrt(dx * dx + dz * dz);
                double maxD = comp->MaxDistanceBeforeTeleport.RawValue / 65536.0, start = comp->FollowStartDistance.RawValue / 65536.0;
                if (d3 > maxD) branch = "far";
                else if (branch != "stuck" && Math.Abs(dy) > 3.0 && dh > start) branch = "height";
                where = " offFollowWithinRange=" + OffFollowWithinRange(comp, d3) +
                        " ownerDist=" + d3.ToString("0.#") + " horiz=" + dh.ToString("0.#") + " dy=" + dy.ToString("0.#") +
                        " max=" + maxD.ToString("0.#") + " followStart=" + start.ToString("0.#");
            }
            return "branch=" + branch + " stuck=" + (comp->StuckDuration.RawValue / 65536.0).ToString("0.##") +
                   "s following=" + (comp->IsFollowing.Value != 0) + where;
        }

        // Logs each native RaisedMinionSystem teleport (raised through Frame.FrameEvents). Predicted frames can raise
        // the same teleport again, so one line per minion per second.
        private static void AfterMinionTeleported(EntityRef __0, Il2CppPhoton.Deterministic.FPVector3 __1, Il2CppPhoton.Deterministic.FPVector3 __2)
        {
            try
            {
                FirstHit(ref _minionTeleportedHit, nameof(AfterMinionTeleported));
                if (!_enabled) return;
                long now = Environment.TickCount64;
                lock (LastTeleportLog)
                {
                    if (LastTeleportLog.TryGetValue(__0.Raw, out long last) && now - last < 1000) return;
                    LastTeleportLog[__0.Raw] = now;
                }
                if (Interlocked.Increment(ref _teleportLogs) > 400) return;
                double d = NecromancyPolicy.DistanceRaw(__1.X.RawValue, __1.Y.RawValue, __1.Z.RawValue, __2.X.RawValue, __2.Y.RawValue, __2.Z.RawValue);
                Minions[__0.Raw] = Environment.TickCount64;
                string state;
                try { state = ClassifyTeleport(__0, __1); } catch (Exception ce) { state = "state=ERR " + ce.GetType().Name; }
                Write("minion TELEPORTED entity=" + __0.Raw + " " + NecromancyPolicy.EntityLabel(__0.Raw) + " moved=" + d.ToString("0.#") + " m " + state);
            }
            catch (Exception e) { DiagnosticsError("teleport-log", e); }
        }
    }
}
