using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Necromancy
{
    internal sealed record BossRuneRule(string Key, int Slot, long Npc, bool Enabled, int MaxActive, string Icon,
        string NameZh, string NameEn, string DescZh, string DescEn)
    {
        private ulong GuidBase => 0x0000543031350000UL + (ulong)Slot * 0x10;
        internal long ItemGuid => unchecked((long)((GuidBase + 1) | 0xC000000000000000UL));
        internal long ActionGuid => unchecked((long)((GuidBase + 2) | 0xC000000000000000UL));
        internal long ItemDataGuid => unchecked((long)((GuidBase + 3) | 0x8000000000000000UL));
    }

    internal static class BossRunePolicy
    {
        internal const string FileName = "NRFWNecromancyBossRunes.json";
        internal const string IconDirectory = "NRFWNecromancyBossRunes";
        internal const string TomeGrantKey = "@tome";   // not a valid rule key, so it cannot clash with a rune grant
        internal const string DefaultJson = @"// NRFW Necromancy Boss 召唤符文（T-015）。修改后重启游戏生效。
// slot (0..255) 决定存档里的 GUID，发放后不得更改或复用；key 是发放记录的一部分。
// npc 是 NpcData GUID（建议用字符串）；max_active 为 1..5；icon 留空则使用原版图标。
[
  {
    ""key"": ""warrick"", ""slot"": 0, ""npc"": ""2680182020363611965"", ""enabled"": true, ""max_active"": 1,
    ""icon"": ""warrick.png"", ""name_zh"": ""沃里克召唤符文"", ""name_en"": ""Rune of Warrick"",
    ""desc_zh"": ""在 8 米内的尸体上召出沃里克为你作战（同时最多 1 个）。"",
    ""desc_en"": ""Summon Warrick from a corpse within 8 meters to fight for you (maximum 1 active).""
  },
  {
    ""key"": ""echo_knight"", ""slot"": 1, ""npc"": ""790324362694538264"", ""enabled"": true, ""max_active"": 1,
    ""icon"": ""echo_knight.png"", ""name_zh"": ""回声骑士召唤符文"", ""name_en"": ""Rune of the Echo Knight"",
    ""desc_zh"": ""在 8 米内的尸体上召出回声骑士为你作战（同时最多 1 个）。"",
    ""desc_en"": ""Summon the Echo Knight from a corpse within 8 meters to fight for you (maximum 1 active).""
  }
]
";
        private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        internal static BossRuneRule[] Parse(string json, out string[] errors, out bool fallback)
        {
            try
            {
                using var document = JsonDocument.Parse(json, Options);
                if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("expected an array of Boss runes");
                var rules = new List<BossRuneRule>();
                var rejected = new List<string>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var slots = new HashSet<int>();
                int index = 0;
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    ++index;
                    try
                    {
                        var rule = ReadRule(row);
                        if (keys.Contains(rule.Key)) throw new FormatException("duplicate key '" + rule.Key + "'");
                        if (slots.Contains(rule.Slot)) throw new FormatException("duplicate slot " + rule.Slot);
                        keys.Add(rule.Key);
                        slots.Add(rule.Slot);
                        rules.Add(rule);
                    }
                    catch (FormatException e) { rejected.Add("#" + index + ": " + e.Message); }
                }
                errors = rejected.ToArray();
                fallback = false;
                return rules.ToArray();
            }
            catch (Exception e) when (e is JsonException || e is ArgumentNullException)
            {
                errors = new[] { e.Message };
                fallback = true;
                return Parse(DefaultJson, out _, out _);
            }
        }

        private static BossRuneRule ReadRule(JsonElement row)
        {
            if (row.ValueKind != JsonValueKind.Object) throw new FormatException("expected an object");
            string key = Text(row, "key");
            if (key.Length == 0 || key.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
                throw new FormatException("invalid key (expected lowercase letters, digits or underscore)");
            int slot = Integer(row, "slot", 0, 255, -1);
            if (slot < 0) throw new FormatException("missing slot");
            row.TryGetProperty("npc", out var npcValue);
            long npc = 0;
            bool npcOk = npcValue.ValueKind == JsonValueKind.Number ? npcValue.TryGetInt64(out npc) :
                npcValue.ValueKind == JsonValueKind.String && long.TryParse(Text(row, "npc"), NumberStyles.Integer, CultureInfo.InvariantCulture, out npc);
            if (!npcOk || npc <= 0) throw new FormatException("npc must be a positive integer GUID");
            int max = Integer(row, "max_active", 1, 5, 1);
            bool enabled = true;
            if (row.TryGetProperty("enabled", out var value))
            {
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                    throw new FormatException("enabled must be a boolean");
                enabled = value.GetBoolean();
            }
            string icon = Text(row, "icon");
            if (icon.Length > 0 && (!icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || icon.Length == 4 ||
                icon.Any(c => c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0)))
                throw new FormatException("icon must be a PNG filename");
            return new BossRuneRule(key, slot, npc, enabled, max, icon,
                Text(row, "name_zh"), Text(row, "name_en"), Text(row, "desc_zh"), Text(row, "desc_en"));
        }

        private static string Text(JsonElement row, string field)
        {
            if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return "";
            if (value.ValueKind != JsonValueKind.String) throw new FormatException(field + " must be a string");
            try { return value.GetString(); }
            catch (InvalidOperationException e) { throw new FormatException(field + " contains invalid Unicode", e); }
        }

        private static int Integer(JsonElement row, string field, int min, int max, int defaultValue)
        {
            if (!row.TryGetProperty(field, out var value)) return defaultValue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= min && number <= max) return number;
            throw new FormatException(field + " must be an integer in " + min + ".." + max);
        }

        private static string GrantKey(string character, string key) => (character ?? "").Trim() + ":" + (key ?? "").Trim();
        internal static bool HasGrant(string list, string character, string key) => NecromancyPolicy.ListContains(list, GrantKey(character, key));
        internal static string AddGrant(string list, string character, string key) => NecromancyPolicy.AddCharacter(list, GrantKey(character, key));
        internal static string RemoveGrant(string list, string character, string key) => string.Join(",", (list ?? "").Split(',')
            .Where(s => !string.Equals(s.Trim(), GrantKey(character, key), StringComparison.OrdinalIgnoreCase)));
    }
}
