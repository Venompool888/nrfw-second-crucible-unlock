using System;
using System.Collections.Generic;
using System.Globalization;

namespace Necromancy
{
    internal enum StuckHoldMode { Vanilla, OffFollow, Never }

    internal sealed record MinionProfile(
        double? TeleportDistance = null, double? FollowStart = null, double? FollowStop = null,
        double? FormationRadius = null, double? EnemySearchRadius = null, double? FollowSpeed = null,
        double? AvoidRadius = null, double? AvoidSpeed = null, StuckHoldMode? StuckHold = null);

    // Pure data and decisions; no game types so the offline audit can compile this file alone.
    internal static class NecromancyPolicy
    {
        internal const string Version = "0.18.5-t013";

        internal static MinionProfile ParseMinionProfile(string text, ICollection<string> errors, string source = "")
        {
            var profile = new MinionProfile();
            foreach (string token in (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = token.IndexOf('=');
                if (equals < 0) { errors.Add(source + $"'{token}': expected key=value"); continue; }
                string key = token[..equals].Trim().ToLowerInvariant();
                string value = token[(equals + 1)..].Trim();
                double? Number(double? previous)
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
                        number >= 0 && number * 65536d < long.MaxValue) return number;
                    errors.Add(source + $"'{token}': expected a nonnegative number in FP range");
                    return previous;
                }
                if (key == "stuck")
                {
                    StuckHoldMode? mode = value.ToLowerInvariant() switch
                    {
                        "vanilla" => StuckHoldMode.Vanilla,
                        "offfollow" => StuckHoldMode.OffFollow,
                        "never" => StuckHoldMode.Never,
                        _ => null
                    };
                    if (mode.HasValue) profile = profile with { StuckHold = mode };
                    else errors.Add(source + $"'{token}': expected vanilla, offfollow or never");
                    continue;
                }
                profile = key switch
                {
                    "teleport" => profile with { TeleportDistance = Number(profile.TeleportDistance) },
                    "follow_start" => profile with { FollowStart = Number(profile.FollowStart) },
                    "follow_stop" => profile with { FollowStop = Number(profile.FollowStop) },
                    "formation_radius" => profile with { FormationRadius = Number(profile.FormationRadius) },
                    "enemy_search_radius" => profile with { EnemySearchRadius = Number(profile.EnemySearchRadius) },
                    "follow_speed" => profile with { FollowSpeed = Number(profile.FollowSpeed) },
                    "avoid_radius" => profile with { AvoidRadius = Number(profile.AvoidRadius) },
                    "avoid_speed" => profile with { AvoidSpeed = Number(profile.AvoidSpeed) },
                    _ => profile
                };
            }
            return profile;
        }

        internal static Dictionary<long, MinionProfile> ParseMinionProfileOverrides(string text, ICollection<string> errors)
        {
            var overrides = new Dictionary<long, MinionProfile>();
            foreach (string token in (text ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = token.IndexOf(':');
                if (colon < 0 || !long.TryParse(token.AsSpan(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out long guid) || guid == 0)
                {
                    errors.Add($"'{token}': expected a nonzero decimal GUID followed by ':'");
                    continue;
                }
                overrides[guid] = ParseMinionProfile(token[(colon + 1)..], errors, FormattableString.Invariant($"npc:{guid} "));
            }
            return overrides;
        }

        internal static (string Name, MinionProfile Profile) SelectMinionProfile(long? guid, int? enemyType,
            IReadOnlyDictionary<string, MinionProfile> tiers, IReadOnlyDictionary<long, MinionProfile> overrides)
        {
            if (guid.HasValue && overrides.TryGetValue(guid.Value, out var profile))
                return (FormattableString.Invariant($"npc:{guid.Value}"), profile);
            // Interop EnemyType: Normal=0, Giant=1, Elite=2, Boss=3, Critters=4, PlotEnemy=5.
            string tier = enemyType switch { 1 => "giant", 2 => "elite", 3 => "boss", _ => "normal" };
            return (enemyType.HasValue ? tier : "default", tiers[tier]);
        }

        // A profile without stuck= (a tier or override that only sets distances) keeps the documented default, offfollow;
        // default(StuckHoldMode) is Vanilla, so GetValueOrDefault() would silently turn the hold off for that minion.
        internal static StuckHoldMode StuckModeOf(MinionProfile profile) => profile?.StuckHold ?? StuckHoldMode.OffFollow;

        // Per-minion stuck mode on top of the 0.16.1 hold rules: offfollow = those rules unchanged (default);
        // never = a following minion is held like an off-follow one (hits extend the hold); vanilla = no stuck hold.
        internal static bool ShouldHoldStuck(StuckHoldMode mode, bool following, bool withinRange, bool near,
            long heldRaw, int maxSeconds, long nowRaw, long? lastHitRaw) =>
            mode != StuckHoldMode.Vanilla &&
            ShouldHoldStuckTeleport(following && mode != StuckHoldMode.Never, withinRange, near, heldRaw, maxSeconds, nowRaw, lastHitRaw);

        // Public Build 22928553 rune items (staff/wand, CompatibleClasses [25, 24], ThomasApproved=true).
        internal const long RaiseMinionRune = 2160606268309502004;   // raiseMinion: fixed npcRisenWildlingSwordA
        internal const long ReviveRune = 565977864064423183;         // revive: UseCorpseNpcType=true
        internal const long HealMinionsRune = 4408421902141782894;   // healMinions: flat heal 50

        // Tome of Healing (治疗宝典): WeaponStaticData offhand, HandType 4, ThomasApproved=false, no loot/vendor/recipe source.
        // Its .actions reuse the released Heal rune action 2452854199486692768; special is the light-shield parry data.
        internal const long TomeOfHealing = 3937520191132810478;

        // Released Heal rune (治疗), used as a known-good reference when logging the runtime rune table.
        internal const long ReleasedHealRune = 4384702748080131279;

        // One-shot loose award (all HeroItemData; none in StrippedAssetGuidsSet). The tome is awarded separately,
        // already carrying RunedTome in its slots.
        internal static readonly long[] Items = { RaiseMinionRune, ReviveRune, HealMinionsRune };
        internal static readonly string[] ItemKeys = { "raiseMinion", "revive", "healMinions" };
        internal static readonly long[] RunedTome = { RaiseMinionRune, ReviveRune, HealMinionsRune };
        internal static readonly string[] RuneKeys = ItemKeys;

        // Character that receives the one-shot items (save name, compared case-insensitively). Empty = any.
        internal const string DefaultTargetCharacter = "";   // empty = grant nothing until a character is configured

        internal static bool ListContains(string list, string name) =>
            !string.IsNullOrWhiteSpace(name) && System.Array.Exists((list ?? "").Split(','),
                s => string.Equals(s.Trim(), name.Trim(), System.StringComparison.OrdinalIgnoreCase));

        internal static bool ShouldGrantCharacter(string target, string granted) =>
            !string.IsNullOrWhiteSpace(target) && !ListContains(granted, target);

        internal static string AddCharacter(string list, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || ListContains(list, name)) return list ?? "";
            return string.IsNullOrWhiteSpace(list) ? name.Trim() : list.Trim() + "," + name.Trim();
        }

        internal static bool IsTargetCharacter(string target, string loaded) =>
            string.IsNullOrWhiteSpace(target) ||
            string.Equals(target.Trim(), (loaded ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase);

        // Verified frames the hero must stay bound before the one-shot award (~5 s at 60 Hz).
        internal const int GrantSettleFrames = 300;

        internal const string CorpseMarkerSystem = "CorpseMarkerSystem";
        internal const string RaisedMinionSystem = "RaisedMinionSystem";
        internal static readonly string[] RequiredSystems = { CorpseMarkerSystem, RaisedMinionSystem };

        // Hooks the plugin installs: (declaring type, method, parameter types, return type).
        // Checked against interop metadata by tools/NecromancyHookAudit before any build is installed.
        internal static readonly (string Type, string Method, string[] Args, string Return)[] Hooks =
        {
            ("Il2CppQuantum.SystemSetup", "CreateSystems", new[] { "Il2CppQuantum.RuntimeConfig", "Il2CppQuantum.SimulationConfig" },
                "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1<Il2CppQuantum.SystemBase>"),
            ("Il2CppQuantum.EndOfFrameSystem", "Update", new[] { "Il2CppQuantum.Frame" }, "Void"),
            ("Il2CppQuantum.InitializeContentSystem", "OnInit", new[] { "Il2CppQuantum.Frame" }, "Void"),
            ("Il2CppQuantum.RaiseMinionActionData", "CountActiveMinions", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "System.Int32"),
            ("Il2CppQuantum.PlayerSerialization", "LoadHeroFromPlayerSerializedData",
                new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.CharacterContents" }, "System.Boolean"),
            ("Il2Cpp.HeroItemDataAsset", "GetName", new string[0], "System.String"),
            ("Il2Cpp.HeroItemDataAsset", "GetDescription", new string[0], "System.String"),
            ("Il2CppQuantum.CorpseMarkerSystem", "Update", new[] { "Il2CppQuantum.Frame" }, "Void"),
            ("Il2CppQuantum.RaisedMinionSystem", "Update", new[] { "Il2CppQuantum.Frame" }, "Void"),
            ("Il2CppQuantum.RaiseMinionActionData", "Execute", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "System.Boolean"),
            ("Il2CppQuantum.RaiseMinionActionData", "CanRaiseNearby", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "System.Boolean"),
            ("Il2CppQuantum.HealMinionsActionData", "Execute", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "System.Boolean"),
            ("Il2CppQuantum.RunesAPI+Weapon", "IsRuneCompatible", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.RuneData" }, "System.Boolean"),
            ("Il2CppQuantum.RaisedMinionSystem", "GetMinionFormationSlot", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.EntityRef" }, "System.Int32"),
            ("Il2CppMoon.Forsaken.RaisedMinionVisualEffect", "ApplyTintEffect", new string[0], "Void"),
            ("Il2CppQuantum.HealthSystem", "UpdateEnemyHealthRegen", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.HealthComponent*" }, "Void"),
            ("Il2CppQuantum.CorpseMarkerSystem", "OnNpcDied", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.Damage", "Il2CppQuantum.DamageResponse", "System.Int32" }, "Void"),
            ("Il2CppMoon.Forsaken.HeroView", "OnDamageResolved", new[] { "Il2CppQuantum.EventDamageResolved" }, "Void"),
            ("Il2CppMoon.Forsaken.CharacterViewEffects", "OnDamageResolved", new[] { "Il2CppQuantum.EventDamageResolved" }, "Void"),
            ("Il2CppQuantum.TeamUtils", "AreAllies", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "Il2CppQuantum.EntityRef" }, "System.Boolean"),
            ("Il2CppQuantum.RunesAPI+Weapon", "IsRuneCompatible", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.WeaponComponent*", "Il2CppQuantum.HeroRuneData" }, "System.Boolean"),
            ("Il2CppQuantum.Frame+FrameEvents", "RaisedMinionTeleported",
                new[] { "Il2CppQuantum.EntityRef", "Il2CppPhoton.Deterministic.FPVector3", "Il2CppPhoton.Deterministic.FPVector3", "Il2CppQuantum.AssetGuid" },
                "Il2CppQuantum.EventRaisedMinionTeleported"),
            ("Il2CppQuantum.Frame+FrameEvents", "RaisedMinionSpawned",
                new[] { "Il2CppQuantum.EntityRef", "Il2CppPhoton.Deterministic.FPVector3", "Il2CppQuantum.AssetGuid" },
                "Il2CppQuantum.EventRaisedMinionSpawned"),
            ("Il2CppQuantum.AttachmentAPI", "ResolveNavDataAttachment", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "Il2CppQuantum.RuntimeNavigationData"),
            ("Il2CppQuantum.PathFollowingComponent", "SearchPath", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "Void"),
            ("Il2CppQuantum.CommandProcessingSystem", "ExecutePerformGesturePlayerCommand",
                new[] { "Il2CppQuantum.Frame", "System.Int32", "Il2CppQuantum.PerformGesturePlayerCommand" }, "Void"),
        };

        // Names of required systems absent from the created tree, in RequiredSystems order.
        internal static string[] Missing(System.Collections.Generic.ICollection<string> present)
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (string name in RequiredSystems) if (!present.Contains(name)) missing.Add(name);
            return missing.ToArray();
        }

        // A tint component that was tried this many times without one successful ApplyTintEffect is disabled.
        internal const int TintMaxFailedAttempts = 3;
        internal static bool ShouldRetryTint(int attempts, int successes) => successes > 0 || attempts < TintMaxFailedAttempts;

        // Shipped team assets (Assets/quantumAssetDatabase/teams). Players and PlayersNoFF differ only in HasFriendlyFire.
        internal const long TeamPlayers = 3356349825642775935;      // HasFriendlyFire = true
        internal const long TeamPlayersNoFF = 1448319240138723968;  // HasFriendlyFire = false
        internal const long TeamPets = 3854117583988179113;
        internal const long TeamEnemies = 370725232116713263;
        internal const string TeamModePlayersNoFF = "players_noff";
        internal const string TeamModeNative = "native";

        // Team a raised minion should be moved to, or 0 to leave it. Only a minion on the player team moves.
        internal static long MinionTeamTarget(string mode, long currentTeam) =>
            mode == TeamModePlayersNoFF && currentTeam == TeamPlayers ? TeamPlayersNoFF : 0;

        internal static string TeamName(long team) => team switch
        {
            TeamPlayers => "Players", TeamPlayersNoFF => "PlayersNoFF", TeamPets => "Pets", TeamEnemies => "Enemies", 0 => "none",
            _ => team.ToString(),
        };

        // The shipped raise data has no minion lifetime; this is the plugin's default (0 = unlimited).
        internal const int DefaultMinionLifetimeSeconds = 600;   // maintainer 2026-11-26: 120 s was too short for boss rush

        // RaiseMinionActionData.MaxDistanceBeforeTeleport ships as 20 m; minions farther than that snap back to the
        // player. Maintainer 2026-11-26: at least 3x. <= 0 or NaN keeps the shipped value; capped at 50x.
        internal const float DefaultTeleportDistanceMultiplier = 3f;
        internal static long ScaledTeleportRaw(long nativeRaw, float multiplier)
        {
            if (float.IsNaN(multiplier) || multiplier <= 0f || nativeRaw <= 0) return nativeRaw;
            return (long)System.Math.Round(nativeRaw * (double)System.Math.Min(multiplier, 50f));
        }

        // T-010: which NPC raiseMinion summons. The shipped raise data (UseCorpseNpcType=false) always summons
        // npcRisenWildlingSwordA. Maintainer 2026-11-26: summon Warrick the Torn (world boss version), at most one,
        // still needs a corpse, no boss bar/phases. revive (UseCorpseNpcType=true) is never changed.
        internal const long WildlingSwordNpc = 2598229157892424643;   // npcRisenWildlingSwordA (shipped)
        internal const long WarrickBossNpc = 2680182020363611965;     // npcWarrickBoss, EnemyType Boss
        internal const long WarrickEnemyNpc = 670058593051072033;     // npcWarrickEnemy, EnemyType Giant, same AI
        internal const string DefaultMinionNpc = "native";
        internal const int DefaultMinionMaxActive = 1;

        // Runtime asset Path is empty in the release build (0.15.2 playtest), so defaults are GUIDs read offline from
        // qdb_assets_all: bestialPowerGestureData (no gesture is named roar; maintainer used this one) and the inner ring.
        internal const string DefaultRoarGesture = "2126111680215882670";   // bestialPowerGestureData
        internal const string DefaultRoarAction = "4386907888524794362";    // mutantSoldierAttackDelayedAOEData (Warrick's delayed-AOE move). The SwordAOEInner/Middle/Outer assets are the spawned cascades, not ActionData (0.15.12 playtest).
        internal const int RoarCooldownSeconds = 3;

        internal static bool IsWarrickNpc(long npc) => npc == WarrickBossNpc || npc == WarrickEnemyNpc;

        // Null denotes a keyword; zero denotes an invalid setting.
        internal static long? RoarAssetGuid(string setting)
        {
            if (string.IsNullOrWhiteSpace(setting)) return 0;
            return long.TryParse(setting, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
                out long guid) ? System.Math.Max(0, guid) : null;
        }

        internal static bool MatchesRoarAsset(string setting, long guid, string path) =>
            guid > 0 && (RoarAssetGuid(setting) is long expected ? guid == expected :
                path != null && path.IndexOf(setting.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0);

        // "native" -> 0 (keep shipped data); named presets; or a positive NpcData GUID. Anything else -> 0.
        internal static long MinionNpcGuid(string setting)
        {
            string s = (setting ?? "").Trim().ToLowerInvariant();
            switch (s)
            {
                case "warrick_boss": return WarrickBossNpc;
                case "warrick_enemy": return WarrickEnemyNpc;
                case "wildling": return WildlingSwordNpc;
                case "": case "native": return 0;
            }
            return long.TryParse(s, out long guid) && guid > 0 ? guid : 0;
        }

        // MaxActiveMinions to write on the raiseMinion asset: only while the summon is swapped and the setting is
        // positive (capped at the shipped 5); otherwise keep the shipped value.
        internal static int MinionMaxActive(long swapGuid, int setting, int shipped) =>
            swapGuid == 0 || setting <= 0 ? shipped : System.Math.Min(setting, System.Math.Max(shipped, 1));

        internal static string NpcName(long npc) => npc switch
        {
            WildlingSwordNpc => "npcRisenWildlingSwordA", WarrickBossNpc => "npcWarrickBoss", WarrickEnemyNpc => "npcWarrickEnemy",
            _ => npc.ToString(),
        };

        // 0.15.12 playtest: Warrick was stuck-teleported 10.9 m away while following. Within range a following minion is
        // now held too, but always for a bounded time (maxSeconds, or 10 s when maxSeconds is 0) and hits do not extend
        // it, so a minion blocked on its way back still returns. Off-follow keeps the hit extension and the 0 = unlimited.
        internal const int FollowingStuckHoldDefaultSeconds = 10;

        // Hold range: horizontal distance within the teleport distance and |dy| within maxDy (maxDy <= 0 = no limit).
        internal static bool InHoldRange(double horiz, double dy, double maxDist, double maxDy) =>
            horiz <= maxDist && (maxDy <= 0 || System.Math.Abs(dy) <= maxDy);
        internal static bool ShouldHoldStuckTeleport(bool following, bool withinRange, bool near, long heldRaw, int maxSeconds,
            long nowRaw, long? lastHitRaw)
        {
            if (near) return true;
            if (!withinRange) return false;
            if (following) return heldRaw < ((long)(maxSeconds > 0 ? maxSeconds : FollowingStuckHoldDefaultSeconds) << 16);
            return maxSeconds == 0 || heldRaw < ((long)maxSeconds << 16) ||
                (lastHitRaw is long hit && hit <= nowRaw && nowRaw - hit <= (5L << 16));
        }

        // Unity/Quantum log form of an EntityRef: E.<index:5>.<version:3> (raw = flag bit 63 | version << 32 | index).
        internal static string EntityLabel(ulong raw) =>
            "E." + (raw & 0xFFFFFFFFUL).ToString("D5") + "." + ((raw >> 32) & 0x7FFFFFFFUL).ToString("D3");

        internal static double DistanceRaw(long ax, long ay, long az, long bx, long by, long bz)
        {
            double dx = (bx - ax) / 65536.0, dy = (by - ay) / 65536.0, dz = (bz - az) / 65536.0;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Native PayloadProxyGroup inside Damage inside EventDamageResolved: the proxy buffer (8-byte EntityRefs) must
        // fit inside the group, the group inside Damage, and the slot counter must sit before the buffer.
        internal static bool ProxyLayoutOk(int offDamage, int offProxy, int groupSize, int damageSize, int offSlots, int offSlotIndex, int slotCount) =>
            offDamage >= 16 && offProxy >= 0 && groupSize > 0 && offProxy + groupSize <= damageSize &&
            offSlots >= 0 && (offSlots & 7) == 0 && slotCount >= 1 && slotCount <= 32 && offSlots + slotCount * 8 <= groupSize &&
            offSlotIndex >= 0 && offSlotIndex + 4 <= offSlots;

        // FP raw values are 16.16 fixed point.
        internal static bool LifetimeExpired(long birthRaw, long nowRaw, int lifetimeSeconds) =>
            lifetimeSeconds > 0 && nowRaw >= birthRaw && nowRaw - birthRaw >= (long)lifetimeSeconds << 16;

        // Minion<->hero and minion<->minion are allies; hero<->hero is never changed (keeps player friendly fire).
        internal static bool BlocksRaisedMinionDamage(bool sourcePlayer, bool sourceRaisedByPlayer, bool hitPlayer, bool hitRaisedByPlayer)
            => sourceRaisedByPlayer && (hitPlayer || hitRaisedByPlayer) || sourcePlayer && hitRaisedByPlayer;

        internal static bool MinionPairIsAllied(bool aMinion, bool aHero, bool bMinion, bool bHero) =>
            (aMinion && (bHero || bMinion)) || (bMinion && (aHero || aMinion));

        internal static bool IsNecromancyRune(long rune) =>
            rune == RaiseMinionRune || rune == ReviveRune || rune == HealMinionsRune;

        // Only the Tome of Healing gains the three necromancy runes; every other pairing keeps the native result.
        internal static bool TomeAccepts(long itemData, long rune) => itemData == TomeOfHealing && IsNecromancyRune(rune);

        // WeaponClass.None == 0. The pointer overload cannot see the item GUID, so it relies on the tome's
        // empty class; staff/wand already accept these runes natively.
        internal static bool ClasslessAccepts(int weaponClass, long rune) => weaponClass == 0 && IsNecromancyRune(rune);

        // Native HeroRuneSlot is three QBoolean (int) + RuneDataRef (int). Write only when the interop layout
        // matches the native sizes and the slot array fits inside the component.
        internal static bool SlotLayoutOk(int slotSize, int managedSlot, int wcSize, int managedWc, int offSlots, int count,
            int offStatic, int offUnlocked, int offRune)
        {
            // managedWc is informational only: the interop struct models the fixed slot buffer as one byte,
            // so its managed size is smaller than native (seen in game: 12 vs 72). Offsets before the buffer still match.
            if (slotSize != 16 || managedSlot != slotSize || count <= 0) return false;
            if (offSlots < 0 || offSlots + count * slotSize > wcSize) return false;
            int[] offs = { offStatic, offUnlocked, offRune };
            foreach (int o in offs) if (o < 0 || o + 4 > slotSize || (o & 3) != 0) return false;
            return offStatic != offUnlocked && offStatic != offRune && offUnlocked != offRune;
        }

        // Grant a rune only into a known hero and only when the hero holds none of it anywhere.
        internal static bool ShouldGrant(bool requested, bool verified, bool heroValid, int ownedCount) =>
            requested && verified && heroValid && ownedCount == 0;
    }
}
