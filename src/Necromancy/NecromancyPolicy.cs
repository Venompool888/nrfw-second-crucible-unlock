namespace Necromancy
{
    // Pure data and decisions; no game types so the offline audit can compile this file alone.
    internal static class NecromancyPolicy
    {
        internal const string Version = "0.13.5";

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
            ("Il2CppQuantum.AttachmentAPI", "ResolveNavDataAttachment", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "Il2CppQuantum.RuntimeNavigationData"),
            ("Il2CppQuantum.PathFollowingComponent", "SearchPath", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, "Void"),
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
