using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Necromancy
{
    // T-014: "which summon x which gesture -> which move". Pure data and decisions, no game types, so the offline
    // tests (tests/NecromancyReactionTests) and the metadata audit can compile this file alone.
    internal enum ReactionPick { First, Random, Cycle }

    internal sealed class ReactionRule
    {
        internal int Index;              // 1-based position in the file, used in logs and as the cycle key
        internal string Name = "";
        internal string Who = "";        // original who text, for logs
        internal long[] Npcs = Array.Empty<long>();
        internal string[] Profiles = Array.Empty<string>();
        internal string[] Tiers = Array.Empty<string>();
        internal bool Any;
        internal long Gesture;
        internal long[] Actions = Array.Empty<long>();
        internal ReactionPick Pick = ReactionPick.First;
        internal int CooldownSeconds = GestureReactions.DefaultCooldownSeconds;

        internal string Label => "#" + Index + (Name.Length > 0 ? "(" + Name + ")" : "");
    }

    internal sealed class ReactionTable
    {
        internal readonly ReactionRule[] Rules;
        internal readonly string[] Errors;   // rejected rules, one line each; the rest of the table still loads

        internal ReactionTable(ReactionRule[] rules, string[] errors) { Rules = rules; Errors = errors; }

        internal bool HasGesture(long gesture)
        {
            if (gesture <= 0) return false;
            foreach (var r in Rules) if (r.Gesture == gesture) return true;
            return false;
        }

        internal bool UsesProfiles
        {
            get { foreach (var r in Rules) if (r.Profiles.Length > 0) return true; return false; }
        }
    }

    internal static class GestureReactions
    {
        internal const string FileName = "NRFWNecromancyReactions.json";
        internal const int DefaultCooldownSeconds = 3;   // T-012's per-minion cooldown
        internal const int MaxCooldownSeconds = 600;
        internal const int MaxActionsPerRule = 16;
        internal const int MaxRules = 256;

        // who keys, most specific first. A rule matches a minion with its most specific key that fits.
        internal const int SpecNpc = 3, SpecProfile = 2, SpecTier = 1, SpecAny = 0, NoMatch = -1;

        // Enemy tiers as RaiseMinionActionData scales them (HealthMultiplierBoss/Giant/Elite/Normal). The runtime value
        // is NpcData.EnemyType.ToString(), compared case-insensitively; reaction logs print it for every minion.
        internal static readonly string[] KnownTiers = { "boss", "giant", "elite", "normal" };

        internal static int Specificity(ReactionRule rule, long npc, string profile, string tier)
        {
            if (npc > 0 && Array.IndexOf(rule.Npcs, npc) >= 0) return SpecNpc;
            if (!string.IsNullOrEmpty(profile) && ContainsIgnoreCase(rule.Profiles, profile)) return SpecProfile;
            if (!string.IsNullOrEmpty(tier) && ContainsIgnoreCase(rule.Tiers, tier)) return SpecTier;
            return rule.Any ? SpecAny : NoMatch;
        }

        // Most specific rule for this gesture and minion; ties go to the rule that comes first in the file. Null = the
        // minion does not react.
        internal static ReactionRule Match(ReactionTable table, long gesture, long npc, string profile, string tier)
        {
            if (table == null || gesture <= 0) return null;
            ReactionRule best = null;
            int bestSpec = NoMatch;
            foreach (var rule in table.Rules)
            {
                if (rule.Gesture != gesture) continue;
                int spec = Specificity(rule, npc, profile, tier);
                if (spec > bestSpec) { best = rule; bestSpec = spec; }
            }
            return best;
        }

        // FP raw values are 16.16 fixed point. 0 s never blocks; a clock that went backwards (new session) never blocks.
        internal static bool CooldownElapsed(long lastRaw, long nowRaw, int seconds) =>
            seconds <= 0 || nowRaw < lastRaw || nowRaw - lastRaw >= (long)seconds << 16;

        // Index into rule.Actions. random is a hash of (minion, sim time): no game RNG is touched, so the simulation
        // state is unchanged. cycle uses the caller's per-(minion, rule) counter.
        internal static int PickIndex(ReactionPick pick, int count, ulong minion, long nowRaw, int cycleCount)
        {
            if (count <= 1) return 0;
            switch (pick)
            {
                case ReactionPick.Cycle: return (int)((uint)cycleCount % (uint)count);
                case ReactionPick.Random: return (int)(Mix(minion ^ unchecked((ulong)nowRaw)) % (ulong)count);
                default: return 0;
            }
        }

        private static ulong Mix(ulong z)
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        // ---- default table -----------------------------------------------------------------

        // T-012's behaviour as the one default rule: your raised Warrick (boss or giant version) uses the configured
        // move when you do the configured gesture, 3 s per-minion cooldown, forced on the action layer.
        internal static ReactionTable Default(long gesture, long action) => new(new[]
        {
            new ReactionRule
            {
                Index = 1, Name = "warrick-bestial-power", Who = NecromancyPolicy.WarrickBossNpc + "," + NecromancyPolicy.WarrickEnemyNpc,
                Npcs = new[] { NecromancyPolicy.WarrickBossNpc, NecromancyPolicy.WarrickEnemyNpc },
                Gesture = gesture, Actions = new[] { action },
            },
        }, Array.Empty<string>());

        // GUIDs from the old roar_gesture / roar_action settings when they are GUIDs; otherwise the T-012 defaults.
        internal static long SeedGuid(string setting, string fallback)
        {
            long? g = NecromancyPolicy.RoarAssetGuid(setting);
            return g is long v && v > 0 ? v : long.Parse(fallback, CultureInfo.InvariantCulture);
        }

        internal static string DefaultJson(long gesture, long action)
        {
            var sb = new StringBuilder();
            sb.Append("// NRFW Necromancy 表情反应表（T-014）。改完存盘，下次进游戏或读档时生效。\n");
            sb.Append("// 发表情时，你召出的每只随从各自找一条最具体的规则，放规则里的招；没有规则的随从不受影响。\n");
            sb.Append("//\n");
            sb.Append("// 每条规则：\n");
            sb.Append("//   who      匹配哪些随从，可写一个或一组，优先级从高到低：\n");
            sb.Append("//              \"2680182020363611965\"  NPC 资产 GUID（十进制）\n");
            sb.Append("//              \"profile:warrick\"      T-013 的召唤物配置名（T-013 合入前不会匹配）\n");
            sb.Append("//              \"tier:boss\"            敌人档位 boss / giant / elite / normal\n");
            sb.Append("//              \"*\"                    任何随从\n");
            sb.Append("//   gesture  表情资产 GUID。已知：凶残之力 bestialPower = 2126111680215882670，\n");
            sb.Append("//            fisticuffs = 1770795828947168763。不知道 GUID 时随便做个表情，日志会写出 gesture GUID=…\n");
            sb.Append("//   action   动作（ActionData）GUID，一个或一组（最多 16 个）。招式生成的震波实体不是动作，填了日志会报实际类型。\n");
            sb.Append("//   pick     有多个 action 时怎么选：first（默认）/ random / cycle（轮流）\n");
            sb.Append("//   cooldown 每只随从的冷却秒数，默认 3，0 = 不冷却，最多 600\n");
            sb.Append("//   mode     force（默认，立即打断当前动作执行）。prefer 还没做。\n");
            sb.Append("//   name     可选，日志里显示的名字\n");
            sb.Append("//\n");
            sb.Append("// GUID 请写成带引号的字符串，避免编辑器把大整数改掉。写错的规则会被跳过并在 NRFWNecromancy.log 里写原因。\n");
            sb.Append("// 删掉这个文件，插件会按 MelonPreferences.cfg 里的 roar_gesture / roar_action 重新生成默认表。\n");
            sb.Append("{\n");
            sb.Append("  \"version\": 1,\n");
            sb.Append("  \"rules\": [\n");
            sb.Append("    {\n");
            sb.Append("      \"name\": \"warrick-bestial-power\",\n");
            sb.Append("      // 沃里克（Boss 版 npcWarrickBoss、巨型版 npcWarrickEnemy）\n");
            sb.Append("      \"who\": [\"" + NecromancyPolicy.WarrickBossNpc + "\", \"" + NecromancyPolicy.WarrickEnemyNpc + "\"],\n");
            sb.Append("      \"gesture\": \"" + gesture.ToString(CultureInfo.InvariantCulture) + "\",\n");
            sb.Append("      // 4386907888524794362 = 延迟范围攻击 mutantSoldierAttackDelayedAOEData；\n");
            sb.Append("      // 1578638975510328898 = 下劈短震波，2630019745959013014 = 跳劈（快）\n");
            sb.Append("      \"action\": [\"" + action.ToString(CultureInfo.InvariantCulture) + "\"],\n");
            sb.Append("      \"pick\": \"first\",\n");
            sb.Append("      \"cooldown\": 3,\n");
            sb.Append("      \"mode\": \"force\"\n");
            sb.Append("    }\n");
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // ---- parsing ------------------------------------------------------------------------

        private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        private static readonly HashSet<string> RuleKeys = new(StringComparer.Ordinal)
            { "name", "who", "gesture", "action", "pick", "cooldown", "mode" };

        // Null with fileError set: the file as a whole is unusable (bad JSON, no rules array, wrong version).
        // Otherwise a table of the valid rules; each rejected rule has one line in Errors.
        internal static ReactionTable Parse(string json, out string fileError)
        {
            fileError = null;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json ?? "", Options); }
            catch (JsonException e) { fileError = "not valid JSON: " + e.Message; return null; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { fileError = "top level must be an object with \"rules\""; return null; }
                if (root.TryGetProperty("version", out var ver) && !(ver.ValueKind == JsonValueKind.Number && ver.TryGetInt32(out int v) && v == 1))
                { fileError = "unsupported version (expected 1)"; return null; }
                if (!root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
                { fileError = "missing \"rules\" array"; return null; }

                var ok = new List<ReactionRule>();
                var errors = new List<string>();
                int index = 0;
                foreach (var item in rules.EnumerateArray())
                {
                    ++index;
                    if (index > MaxRules) { errors.Add("rules after #" + MaxRules + " ignored"); break; }
                    var rule = ParseRule(item, index, out string error);
                    if (rule != null) ok.Add(rule); else errors.Add("rule #" + index + ": " + error);
                }
                return new ReactionTable(ok.ToArray(), errors.ToArray());
            }
        }

        private static ReactionRule ParseRule(JsonElement e, int index, out string error)
        {
            error = null;
            if (e.ValueKind != JsonValueKind.Object) { error = "must be an object"; return null; }
            foreach (var p in e.EnumerateObject())
                if (!RuleKeys.Contains(p.Name)) { error = "unknown key \"" + p.Name + "\""; return null; }

            var rule = new ReactionRule { Index = index };
            if (e.TryGetProperty("name", out var name))
            {
                if (name.ValueKind != JsonValueKind.String) { error = "name must be a string"; return null; }
                rule.Name = (name.GetString() ?? "").Trim();
            }

            if (!e.TryGetProperty("who", out var who)) { error = "missing who"; return null; }
            var whoItems = StringList(who, out error);
            if (whoItems == null) { error = "who: " + error; return null; }
            if (whoItems.Count == 0) { error = "who is empty"; return null; }
            var npcs = new List<long>(); var profiles = new List<string>(); var tiers = new List<string>();
            foreach (string raw in whoItems)
            {
                string w = raw.Trim();
                if (w == "*") { rule.Any = true; continue; }
                if (w.StartsWith("tier:", StringComparison.OrdinalIgnoreCase))
                {
                    string t = w.Substring(5).Trim();
                    if (t.Length == 0) { error = "who \"" + w + "\": empty tier"; return null; }
                    tiers.Add(t); continue;
                }
                if (w.StartsWith("profile:", StringComparison.OrdinalIgnoreCase))
                {
                    string pr = w.Substring(8).Trim();
                    if (pr.Length == 0) { error = "who \"" + w + "\": empty profile name"; return null; }
                    profiles.Add(pr); continue;
                }
                if (TryGuid(w, out long npc)) { npcs.Add(npc); continue; }
                error = "who \"" + w + "\": expected an NPC GUID, \"tier:…\", \"profile:…\" or \"*\"";
                return null;
            }
            rule.Npcs = npcs.ToArray(); rule.Profiles = profiles.ToArray(); rule.Tiers = tiers.ToArray();
            rule.Who = string.Join(",", whoItems);

            if (!e.TryGetProperty("gesture", out var gesture)) { error = "missing gesture"; return null; }
            if (!TryGuid(gesture, out rule.Gesture)) { error = "gesture must be a positive decimal GUID"; return null; }

            if (!e.TryGetProperty("action", out var action)) { error = "missing action"; return null; }
            var actionItems = StringList(action, out error);
            if (actionItems == null) { error = "action: " + error; return null; }
            if (actionItems.Count == 0) { error = "action is empty"; return null; }
            if (actionItems.Count > MaxActionsPerRule) { error = "more than " + MaxActionsPerRule + " actions"; return null; }
            var actions = new long[actionItems.Count];
            for (int i = 0; i < actions.Length; ++i)
                if (!TryGuid(actionItems[i], out actions[i])) { error = "action \"" + actionItems[i] + "\" is not a positive decimal GUID"; return null; }
            rule.Actions = actions;

            if (e.TryGetProperty("pick", out var pick))
            {
                string pk = pick.ValueKind == JsonValueKind.String ? (pick.GetString() ?? "").Trim().ToLowerInvariant() : null;
                switch (pk)
                {
                    case "first": rule.Pick = ReactionPick.First; break;
                    case "random": rule.Pick = ReactionPick.Random; break;
                    case "cycle": rule.Pick = ReactionPick.Cycle; break;
                    default: error = "pick must be first, random or cycle"; return null;
                }
            }

            if (e.TryGetProperty("cooldown", out var cd))
            {
                if (cd.ValueKind != JsonValueKind.Number || !cd.TryGetDouble(out double s) || double.IsNaN(s) || s < 0 || s > MaxCooldownSeconds ||
                    Math.Floor(s) != s)
                { error = "cooldown must be a whole number of seconds from 0 to " + MaxCooldownSeconds; return null; }
                rule.CooldownSeconds = (int)s;
            }

            if (e.TryGetProperty("mode", out var mode))
            {
                string m = mode.ValueKind == JsonValueKind.String ? (mode.GetString() ?? "").Trim().ToLowerInvariant() : null;
                if (m == "prefer") { error = "mode prefer is not implemented yet (T-014 stage 4); use force"; return null; }
                if (m != "force") { error = "mode must be force"; return null; }
            }
            return rule;
        }

        // A string, a number, or an array of them.
        private static List<string> StringList(JsonElement e, out string error)
        {
            error = null;
            var list = new List<string>();
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    string s = Scalar(item);
                    if (s == null) { error = "array items must be strings or numbers"; return null; }
                    list.Add(s);
                }
                return list;
            }
            string one = Scalar(e);
            if (one == null) { error = "must be a string, a number or an array"; return null; }
            list.Add(one);
            return list;
        }

        private static string Scalar(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            _ => null,
        };

        private static bool TryGuid(JsonElement e, out long guid)
        {
            guid = 0;
            string s = Scalar(e);
            return s != null && TryGuid(s, out guid);
        }

        internal static bool TryGuid(string s, out long guid) =>
            long.TryParse((s ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out guid) && guid > 0;

        private static bool ContainsIgnoreCase(string[] items, string value)
        {
            foreach (string s in items) if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
