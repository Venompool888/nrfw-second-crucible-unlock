using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrucibleUnlock
{
    public static class ThirdTrialPolicy
    {
        public const long PlaylistGuid = -611686018427287904L;
        public const long ThirdStepGuid = 3364572276970703445L;
        public const long OriginalPlaylistGuid = 5973112006018325L;
        public const long SpawnPointGuid = 641656453459156268L;
        public const long TemplatePlaylistGuid = 1509816363606571268L;
        public const string Path = "third-trial/restored-enemies/playlist";
    }

    public sealed class ThirdTrialCatalog
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
        [JsonPropertyName("source_qdb_sha256")] public string SourceQdbHash { get; set; }
        [JsonPropertyName("stages")] public ThirdTrialStage[] Stages { get; set; }
        public static ThirdTrialCatalog LoadEmbedded()
        {
            using Stream stream = typeof(ThirdTrialCatalog).Assembly.GetManifestResourceStream("NRFW.ThirdTrial.Rooms.json")
                ?? throw new InvalidOperationException("Third trial room manifest is missing.");
            var result = JsonSerializer.Deserialize<ThirdTrialCatalog>(stream);
            result.Validate();
            return result;
        }
        public void Validate()
        {
            if (SchemaVersion != 1 || SourceQdbHash != EnemyRestorationCatalog.ExpectedQdbHash || Stages == null || Stages.Length != 10)
                throw new InvalidOperationException("Third trial manifest/version mismatch.");
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            var chunks = new HashSet<long>();
            var floors = new HashSet<long>();
            foreach (var s in Stages)
                if (s == null || string.IsNullOrWhiteSpace(s.ProfileKey) || !profiles.Add(s.ProfileKey) ||
                    s.FloorGuid <= 0 || !floors.Add(s.FloorGuid) || s.ChunkGuid <= 0 || !chunks.Add(s.ChunkGuid) ||
                    s.BucketGuid <= 0 || s.NuggetGuid <= 0 || s.LayerGuid <= 0 || s.SpawnerGuids == null || s.SpawnerGuids.Length == 0 ||
                    s.SpawnerGuids.Any(x => x == 0) || s.SpawnerGuids.Distinct().Count() != s.SpawnerGuids.Length ||
                    s.EntryNuggetGuids == null || !s.EntryNuggetGuids.Contains(s.NuggetGuid) || s.EntryNuggetGuids.Any(x => x <= 0))
                    throw new InvalidOperationException("Third trial room is invalid/duplicated.");
        }
    }
    public sealed class ThirdTrialStage
    {
        [JsonPropertyName("profile_key")] public string ProfileKey { get; set; }
        [JsonPropertyName("floor_guid")] public long FloorGuid { get; set; }
        [JsonPropertyName("chunk_guid")] public long ChunkGuid { get; set; }
        [JsonPropertyName("bucket_guid")] public long BucketGuid { get; set; }
        [JsonPropertyName("nugget_guid")] public long NuggetGuid { get; set; }
        [JsonPropertyName("layer_guid")] public long LayerGuid { get; set; }
        [JsonPropertyName("spawner_guids")] public long[] SpawnerGuids { get; set; }
        [JsonPropertyName("entry_nugget_guids")] public long[] EntryNuggetGuids { get; set; }
    }
}
