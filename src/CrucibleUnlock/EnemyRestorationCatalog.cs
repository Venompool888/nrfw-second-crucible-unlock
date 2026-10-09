using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrucibleUnlock
{
    public sealed class EnemyRestorationCatalog
    {
        public const long LocalGuidBase = 4000000000000000000L;
        public const string ExpectedQdbHash = "c4d93c337d8e5d742d23cc3147b64de844be69bde3b0639a62535a4248960cea";
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
        [JsonPropertyName("steam_build_id")] public string SteamBuildId { get; set; }
        [JsonPropertyName("source_qdb_sha256")] public string SourceQdbHash { get; set; }
        [JsonPropertyName("team_guid")] public long TeamGuid { get; set; }
        [JsonPropertyName("profiles")] public EnemyRestorationProfile[] Profiles { get; set; }

        public static EnemyRestorationCatalog Load()
        {
            using Stream stream = typeof(EnemyRestorationCatalog).Assembly.GetManifestResourceStream("NRFW.EnemyRestoration.Bindings.json")
                ?? throw new InvalidOperationException("Embedded enemy bindings are missing.");
            var result = JsonSerializer.Deserialize<EnemyRestorationCatalog>(stream);
            if (result == null || result.SchemaVersion != 1 || result.SteamBuildId != "22928553" ||
                result.SourceQdbHash != ExpectedQdbHash || result.TeamGuid == 0 || result.Profiles == null || result.Profiles.Length == 0)
                throw new InvalidOperationException("Enemy restoration catalog/version is invalid.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.Profiles.Length; i++)
            {
                var p = result.Profiles[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Key) || !keys.Add(p.Key) || p.NpcTemplateGuid <= 0 ||
                    p.HostTemplateGuid <= 0 || p.ControllerTemplateGuid <= 0 || p.ViewTemplateGuid <= 0 || p.WeaponGuid <= 0 || p.PrefabGuid == null)
                    throw new InvalidOperationException("Enemy restoration profile is invalid or duplicated.");
                p.Index = i;
                p.TeamGuid = result.TeamGuid;
                p.BlockedActions ??= Array.Empty<long>();
            }
            return result;
        }

        // Verified from the shipped AssetGuid(Int64, AssetGuidType) constructor:
        // the top two bits hold the type. DynamicExplicit=3; do not reuse static IDs.
        public static long DynamicGuid(long localGuid)
        {
            if (localGuid < 0 || (ulong)localGuid >= (1UL << 62)) throw new ArgumentOutOfRangeException(nameof(localGuid));
            return unchecked((long)((ulong)localGuid | 0xC000000000000000UL));
        }
    }

    public sealed class EnemyRestorationProfile
    {
        [JsonPropertyName("key")] public string Key { get; set; }
        [JsonPropertyName("label")] public string Label { get; set; }
        [JsonPropertyName("npc_template_guid")] public long NpcTemplateGuid { get; set; }
        [JsonPropertyName("host_template_guid")] public long HostTemplateGuid { get; set; }
        [JsonPropertyName("controller_template_guid")] public long ControllerTemplateGuid { get; set; }
        [JsonPropertyName("weapon_guid")] public long WeaponGuid { get; set; }
        [JsonPropertyName("offhand_guid")] public long OffhandGuid { get; set; }
        [JsonPropertyName("character_type")] public int CharacterType { get; set; }
        [JsonPropertyName("enemy_faction")] public int EnemyFaction { get; set; }
        [JsonPropertyName("blocked_actions")] public long[] BlockedActions { get; set; }
        [JsonPropertyName("view_template_guid")] public long ViewTemplateGuid { get; set; }
        [JsonPropertyName("prefab_guid")] public EnemyRestorationPrefabGuid PrefabGuid { get; set; }
        [JsonPropertyName("prefab_name")] public string PrefabName { get; set; }
        [JsonIgnore] public int Index { get; set; }
        [JsonIgnore] public long TeamGuid { get; set; }
        public long Id(int offset) => EnemyRestorationCatalog.DynamicGuid(checked(EnemyRestorationCatalog.LocalGuidBase + Index * 10L + offset));
        [JsonIgnore] public long HostGuid => Id(0);
        [JsonIgnore] public long ControllerGuid => Id(1);
        [JsonIgnore] public long ViewGuid => Id(2);
        [JsonIgnore] public long NpcGuid => Id(3);
        [JsonIgnore] public long PrivateWeaponGuid => Id(4);
        public string Path(string role) => "enemy-restoration/" + Key + "/" + role;
    }
    public sealed class EnemyRestorationPrefabGuid
    {
        [JsonPropertyName("m_data1")] public ulong Data1 { get; set; }
        [JsonPropertyName("m_data2")] public ulong Data2 { get; set; }
    }
}
