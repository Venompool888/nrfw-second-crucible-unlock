using System;
using System.Collections.Generic;
using System.Threading;
using Il2CppQuantum;
using MelonLoader;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // ---- minion lifecycle: no enemy auto-regen, optional lifetime ----------------------
        // Raised minions are still NPCs, so HealthSystem.UpdateEnemyHealthRegen refilled them whenever no
        // player was in regen distance (e.g. right after a teleport). Skip it for minions. The shipped data
        // has no minion lifetime; minion_lifetime_seconds adds one (0 = unlimited).

        private static readonly Dictionary<ulong, long> MinionBirth = new();   // sim thread only; FP raw
        private static readonly HashSet<ulong> Expired = new();
        private static int _lifetimeSeconds, _regenLogs, _expireLogs;

        // Every minion comes from RaiseMinionActionData.Execute, so scaling the asset there (and in CanRaiseNearby)
        // covers all of them. Written values are remembered per asset so a reused asset is never scaled twice, and a
        // reloaded asset (back at the shipped value) is scaled again.
        private static readonly Dictionary<IntPtr, long> TeleportWritten = new();
        private static float _teleportMultiplier = NecromancyPolicy.DefaultTeleportDistanceMultiplier;

        // T-010: raiseMinion (UseCorpseNpcType=false) summons the configured NPC instead of npcRisenWildlingSwordA.
        // The target must resolve to an NpcData asset first; otherwise the shipped summon is left alone. Written
        // values are remembered per asset so a reloaded asset is swapped again and an unchanged one is not touched.
        private static long _minionNpc;
        private static int _minionMaxActive;
        private static readonly Dictionary<IntPtr, (long Npc, int Max)> SummonWritten = new();
        private static readonly HashSet<IntPtr> SummonSkipLogged = new();

        private static long _lastRosterRaw;
        private static string _lastRoster = "";
        private static int _rosterLogs;
        private static volatile bool _rosterOff;
        private static readonly NecromancyPolicy.FeatureFaultGuard RosterGuard = new(NecromancyPolicy.DefaultFeatureMaxErrors);

        private static readonly HashSet<ulong> NoNavLogged = new();
        private static int _noNavCount, _guardCount, _navHookErrors;

        private static int _executes, _canRaise, _heals;
        private static int _searchPathHit, _resolveNavDataHit, _raiseDataHit, _enemyRegenHit, _npcDiedHit;

        private static void RosterError(string stage, Exception e)
        {
            if (RosterGuard.RecordError(out bool justDisabled))
                Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (justDisabled)
            {
                _rosterOff = true;
                Write("roster disabled after repeated errors; rest of plugin unaffected");
            }
        }

        private static void InstallMinionLifecycle(MelonPreferences_Category category)
        {
            var entry = category.CreateEntry("minion_lifetime_seconds", NecromancyPolicy.DefaultMinionLifetimeSeconds,
                description: "Seconds a raised minion stays before it falls; 0 = until killed (original data has no limit).");
            _lifetimeSeconds = Math.Max(0, entry.Value);
            var teamEntry = category.CreateEntry("minion_team", NecromancyPolicy.TeamModePlayersNoFF,
                description: "players_noff = move raised minions from the player team to the shipped no-friendly-fire player team; native = leave the team unchanged.");
            _teamMode = (teamEntry.Value ?? "").Trim().ToLowerInvariant();
            Write("minion_team=" + _teamMode);
            var h = new HarmonyLib.Harmony("NRFW.Necromancy.MinionLifecycle");
            try
            {
                PatchPrefix(h, typeof(HealthSystem), "UpdateEnemyHealthRegen",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(HealthComponent).MakePointerType() }, nameof(BeforeEnemyRegen), typeof(void));
                Write("minion regen hook installed");
            }
            catch (Exception e) { Write("minion regen hook FAILED " + e.GetType().Name + ": " + e.Message); }
            try
            {
                PatchPrefix(h, typeof(CorpseMarkerSystem), "OnNpcDied",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(Damage), typeof(DamageResponse), typeof(int) }, nameof(BeforeNpcDied), typeof(void));
                Write("expired-minion corpse hook installed");
            }
            catch (Exception e) { Write("expired-minion corpse hook FAILED " + e.GetType().Name + ": " + e.Message); }
            Write("minion_lifetime_seconds=" + _lifetimeSeconds);
            try
            {
                PatchPrefix(h, typeof(AttachmentAPI), "ResolveNavDataAttachment", new[] { typeof(Frame), typeof(EntityRef) },
                    nameof(BeforeResolveNavData), typeof(RuntimeNavigationData));
                Write("nav-attachment probe installed");
            }
            catch (Exception e) { Write("nav-attachment probe FAILED " + e.GetType().Name + ": " + e.Message); }

            InstallTeleportGuard(category, h);

            try
            {
                PatchPrefix(h, typeof(PathFollowingComponent), "SearchPath", new[] { typeof(Frame), typeof(EntityRef) },
                    nameof(BeforeSearchPath), typeof(void));
                Write("path-search nav guard installed");
            }
            catch (Exception e) { Write("path-search nav guard FAILED " + e.GetType().Name + ": " + e.Message); }
            var tpEntry = category.CreateEntry("minion_teleport_distance_multiplier", NecromancyPolicy.DefaultTeleportDistanceMultiplier,
                description: "Multiplier on the shipped 20 m distance at which a minion snaps back to the player (3 = 60 m; 1 = original).");
            _teleportMultiplier = tpEntry.Value;
            var npcEntry = category.CreateEntry("minion_npc", NecromancyPolicy.DefaultMinionNpc,
                description: "What raiseMinion summons: warrick_boss (Warrick the Torn), warrick_enemy, wildling, native (shipped data), or an NpcData GUID. revive is never changed.");
            var maxEntry = category.CreateEntry("minion_max_active", NecromancyPolicy.DefaultMinionMaxActive,
                description: "Most raiseMinion minions at once while minion_npc swaps the summon (shipped 5); 0 = keep the shipped value.");
            _minionNpc = NecromancyPolicy.MinionNpcGuid(npcEntry.Value);
            _minionMaxActive = maxEntry.Value;
            Write("minion_npc=" + npcEntry.Value + " -> " + (_minionNpc == 0 ? "native" : NecromancyPolicy.NpcName(_minionNpc)) + "; minion_max_active=" + _minionMaxActive);
            try
            {
                PatchPrefix(h, typeof(RaiseMinionActionData), "Execute", new[] { typeof(Frame), typeof(EntityRef) }, nameof(BeforeRaiseData), typeof(bool));
                PatchPrefix(h, typeof(RaiseMinionActionData), "CanRaiseNearby", new[] { typeof(Frame), typeof(EntityRef) }, nameof(BeforeRaiseData), typeof(bool));
                Write("minion_teleport_distance_multiplier=" + _teleportMultiplier);
            }
            catch (Exception e) { Write("minion teleport-distance hook FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        // 0.15.3 playtest: EntityRefExtensions.GetData<NpcData> returned nothing for a raised Warrick (npc=0).
        // The spawned NPC's data lives in NpcAttachmentComponent.NpcData (a storage index resolved by Get(Frame)).
        internal static long MinionNpcGuid(Frame f, EntityRef minion) => MinionNpcData(f, minion)?.Guid.Value ?? 0;

        internal static unsafe NpcData MinionNpcData(Frame f, EntityRef minion)
        {
            if (f == null || !EntityRefExtensions.Has<NpcAttachmentComponent>(minion, f)) return null;
            var a = EntityRefExtensions.Get<NpcAttachmentComponent>(minion, f);
            if (a == null || a->NpcData.IsDefault) return null;
            return a->NpcData.Get(f);
        }

        private static string MinionNpcText(Frame f, EntityRef minion)
        {
            try { return NecromancyPolicy.NpcName(MinionNpcGuid(f, minion)); }
            catch (Exception e) { return "?" + e.GetType().Name; }
        }

        private static void ApplySummon(Frame f, RaiseMinionActionData d)
        {
            if (_minionNpc == 0) return;
            if (f == null || d.UseCorpseNpcType)
            {
                if (SummonSkipLogged.Add(d.Pointer))
                    Write("summon SKIP asset=" + d.Guid.Value + " " + (f == null ? "no frame" : "UseCorpseNpcType=true (revive keeps the corpse's enemy)"));
                return;
            }
            long current = d.MinionNpcData.Id.Value;
            int currentMax = d.MaxActiveMinions;
            if (SummonWritten.TryGetValue(d.Pointer, out var w) && w.Npc == current && w.Max == currentMax) return;
            var npc = f.Context?.ResourceManager?.GetAsset(new AssetGuid { Value = _minionNpc })?.TryCast<NpcData>();
            if (npc == null)
            {
                SummonWritten[d.Pointer] = (current, currentMax);
                Write("summon KEEP " + NecromancyPolicy.NpcName(current) + ": " + NecromancyPolicy.NpcName(_minionNpc) + " is not a loadable NpcData");
                return;
            }
            int max = NecromancyPolicy.MinionMaxActive(_minionNpc, _minionMaxActive, currentMax);
            d.MinionNpcData = new AssetRefNpcData { Id = new AssetGuid { Value = _minionNpc } };
            d.MaxActiveMinions = max;
            SummonWritten[d.Pointer] = (d.MinionNpcData.Id.Value, d.MaxActiveMinions);
            Write("summon SWAP " + NecromancyPolicy.NpcName(current) + " -> " + NecromancyPolicy.NpcName(d.MinionNpcData.Id.Value) +
                  " (enemyType " + npc.EnemyType + "); max active " + currentMax + " -> " + d.MaxActiveMinions +
                  "; health x boss/giant/elite/normal " + Fp(d.HealthMultiplierBoss) + "/" + Fp(d.HealthMultiplierGiant) + "/" + Fp(d.HealthMultiplierElite) + "/" + Fp(d.HealthMultiplierNormal) +
                  "; damage x " + Fp(d.DamageMultiplierBoss) + "/" + Fp(d.DamageMultiplierGiant) + "/" + Fp(d.DamageMultiplierElite) + "/" + Fp(d.DamageMultiplierNormal));
        }

        private static string Fp(Il2CppPhoton.Deterministic.FP v) => (v.RawValue / 65536.0).ToString("0.##");

        private static bool BeforeRaiseData(Frame __0, RaiseMinionActionData __instance)
        {
            try
            {
                FirstHit(ref _raiseDataHit, nameof(BeforeRaiseData));
                if (!_enabled || __instance == null) return true;
                // Boss rune actions keep their own summon target; they still get the teleport-distance multiplier below.
                if (!IsPrivateBossAction(__instance.Guid.Value)) try { ApplySummon(__0, __instance); } catch (Exception e) { Error("summon", e); }
                var d = __instance.MaxDistanceBeforeTeleport;
                if (TeleportWritten.TryGetValue(__instance.Pointer, out long written) && written == d.RawValue) return true;
                long scaled = NecromancyPolicy.ScaledTeleportRaw(d.RawValue, _teleportMultiplier);
                TeleportWritten[__instance.Pointer] = scaled;
                if (scaled == d.RawValue) return true;
                Write("minion teleport distance " + (d.RawValue / 65536.0).ToString("0.##") + " -> " + (scaled / 65536.0).ToString("0.##") + " m");
                d.RawValue = scaled;
                __instance.MaxDistanceBeforeTeleport = d;
            }
            catch (Exception e) { Error("teleport-distance", e); }
            return true;
        }

        private static bool BeforeEnemyRegen(Frame __0, EntityRef __1)
        {
            try
            {
                FirstHit(ref _enemyRegenHit, nameof(BeforeEnemyRegen));
                if (!_enabled || __0 == null || !EntityRefExtensions.Has<RaisedMinionComponent>(__1, __0)) return true;
                if (__0.IsVerified && Interlocked.Increment(ref _regenLogs) <= 5) Write("regen SKIPPED for raised minion entity=" + __1.Raw);
                return false;
            }
            catch (Exception e) { Error("regen", e); return true; }
        }

        // A minion we expired leaves no raisable corpse.
        private static bool BeforeNpcDied(Frame __0, EntityRef __1)
        {
            FirstHit(ref _npcDiedHit, nameof(BeforeNpcDied));
            if (!Expired.Contains(__1.Raw)) return true;
            if (__0 != null && __0.IsVerified) Write("expired minion " + __1.Raw + " leaves no corpse");
            return false;
        }

        // Called from the verified end-of-frame tick.
        // 0.13.1 crashed on ~1200 "E.x does not have UseNavDataComponent" from PathFollowingSystem; the four entities
        // never appeared in this log because several log caps were already hit. List live minions in the Unity
        // E.index.version form every 5 s of simulation time (and whenever the set changes) so they can be matched.
        private static void LogRoster(Frame f)
        {
            long now = f.TimeSinceStart.RawValue;
            if (now - _lastRosterRaw < (5L << 16) && now >= _lastRosterRaw) return;
            var parts = new List<string>();
            foreach (var pair in MinionBirth)
            {
                var minion = new EntityRef { Raw = pair.Key };
                if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, f)) continue;
                bool nav = EntityRefExtensions.Has<UseNavDataComponent>(minion, f);
                parts.Add(NecromancyPolicy.EntityLabel(pair.Key) + (nav ? "" : "(NO-NAV)"));
            }
            parts.Sort(StringComparer.Ordinal);
            string roster = string.Join(" ", parts);
            if (roster == _lastRoster && now - _lastRosterRaw < (30L << 16)) return;
            _lastRosterRaw = now;
            _lastRoster = roster;
            if (Interlocked.Increment(ref _rosterLogs) <= 1000) Write("roster n=" + parts.Count + " " + roster);
        }

        // AttachmentAPI.ResolveNavDataAttachment calls Frame.Get<UseNavDataComponent> unconditionally and throws when
        // the entity has none; 0.13.1 died on ~1200 of those from PathFollowingComponent.SearchPath ("too many
        // exceptions"). Static analysis: SearchPath resolves nav data for its agent once it is outside the acceptance
        // radius, and a null result is dereferenced, so returning null is not safe. Guard one level up instead:
        // skip SearchPath for an agent without UseNavDataComponent (the entity simply gets no new path this frame;
        // NavMeshTrackerSystem adds the component once it stands on an active navmesh). The probe on
        // ResolveNavDataAttachment stays read-only and catches any other caller that still reaches the throw.
        private static bool BeforeSearchPath(Frame __0, EntityRef __1)
        {
            try
            {
                FirstHit(ref _searchPathHit, nameof(BeforeSearchPath));
                if (__0 == null || !__1.IsValid) return true;
                if (EntityRefExtensions.Has<UseNavDataComponent>(__1, __0)) return true;
            }
            catch (Exception e) { LogNavHookException("path-guard", e); return true; }

            try { LogNoNav(__0, __1, "GUARD skipped SearchPath", Interlocked.Increment(ref _guardCount)); }
            catch (Exception e) { LogNavHookException("path-guard", e); }
            return false;
        }

        private static void BeforeResolveNavData(Frame __0, EntityRef __1)
        {
            try
            {
                FirstHit(ref _resolveNavDataHit, nameof(BeforeResolveNavData));
                if (__0 == null || !__1.IsValid) return;
                if (EntityRefExtensions.Has<UseNavDataComponent>(__1, __0)) return;
                LogNoNav(__0, __1, "NO-NAV resolve (native will throw)", Interlocked.Increment(ref _noNavCount));
            }
            catch (Exception e) { LogNavHookException("nav-probe", e); }
        }

        private static void LogNavHookException(string stage, Exception e)
        {
            if (Interlocked.Increment(ref _navHookErrors) > 5) return;
            try { Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message); }
            catch (Exception) { }
        }

        private static void LogNoNav(Frame f, EntityRef e, string what, int n)
        {
            bool first;
            lock (NoNavLogged) first = NoNavLogged.Add(e.Raw ^ (ulong)what.Length);
            if (!first) { if (n % 500 == 0) Write(what + " count=" + n); return; }
            bool minion = EntityRefExtensions.Has<RaisedMinionComponent>(e, f);
            bool corpse = EntityRefExtensions.Has<CorpseMarkerComponent>(e, f);
            Write(what + " entity=" + e.Raw + " " + NecromancyPolicy.EntityLabel(e.Raw) + " minion=" + minion + " corpseMarker=" + corpse +
                  " knownMinion=" + MinionBirth.ContainsKey(e.Raw) + " expired=" + Expired.Contains(e.Raw) + " seenAsMinion=" + Minions.ContainsKey(e.Raw) +
                  " verified=" + f.IsVerified + " count=" + n);
        }

        private static void TickLifetime(Frame f)
        {
            if (_lifetimeSeconds <= 0) return;
            long now = f.TimeSinceStart.RawValue;
            List<ulong> due = null, gone = null;
            foreach (var pair in MinionBirth)
            {
                var minion = new EntityRef { Raw = pair.Key };
                if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, f)) { (gone ??= new()).Add(pair.Key); continue; }
                if (NecromancyPolicy.LifetimeExpired(pair.Value, now, _lifetimeSeconds)) (due ??= new()).Add(pair.Key);
            }
            if (gone != null) foreach (var k in gone) MinionBirth.Remove(k);
            if (due == null) return;
            foreach (var k in due)
            {
                MinionBirth.Remove(k);
                Expired.Add(k);
                var minion = new EntityRef { Raw = k };
                DamageAPI.Kill(f, minion, default, default, PoiseLevel.None, true, DamageFlags.None, DamageVisualType.None);
                if (Interlocked.Increment(ref _expireLogs) <= 20) Write("minion EXPIRED after " + _lifetimeSeconds + "s entity=" + k);
            }
        }

        private static void AfterRaiseExecute(Frame __0, EntityRef __1, RaiseMinionActionData __instance, bool __result)
        {
            try
            {
                if (__instance != null && IsPrivateBossAction(__instance.Guid.Value)) LogBossExecute(__0, __1, __instance, __result);
                else if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _executes) <= 200)
                    Write("raise Execute caster=" + __1.Raw + " -> " + __result);
            }
            catch (Exception e) { BossError("Execute log", e); }
        }

        private static void AfterCanRaise(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _canRaise) <= 10)
                Write("raise CanRaiseNearby caster=" + __1.Raw + " -> " + __result);
        }

        private static void AfterHealExecute(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _heals) <= 100)
                Write("healMinions Execute caster=" + __1.Raw + " -> " + __result);
        }
    }
}
