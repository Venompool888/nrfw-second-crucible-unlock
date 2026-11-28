using System;
using System.Globalization;
using System.Text.Json;

namespace Necromancy
{
    internal sealed record BossEntrance(bool Enabled, long PlayerAction, long PlayerCutRaw, long[] MinionActions, bool Invincible, bool PauseAi);

    internal static class BossEntrancePolicy
    {
        private const long DefaultCutRaw = 2L << 16;

        internal static BossEntrance DefaultFor(long npc) => npc == 790324362694538264
            ? new BossEntrance(true, 203502697017737707, DefaultCutRaw, new[] { 2918908058815642059, 2653262915339319819 }, true, true)
            : null;

        internal static BossEntrance Read(JsonElement rule, long npc)
        {
            if (!rule.TryGetProperty("entrance", out var entrance)) return DefaultFor(npc);
            if (entrance.ValueKind != JsonValueKind.Object) throw new FormatException("entrance must be an object");
            bool enabled = true, invincible = true, pauseAi = true;
            long player = 0, cut = DefaultCutRaw;
            long[] actions = Array.Empty<long>();
            foreach (var property in entrance.EnumerateObject())
            {
                string key;
                try { key = property.Name; }
                catch (InvalidOperationException e) { throw new FormatException("entrance contains a key with invalid Unicode", e); }
                var value = property.Value;
                switch (key)
                {
                    case "enabled": enabled = ReadBool(value, key); break;
                    case "invincible": invincible = ReadBool(value, key); break;
                    case "pause_ai": pauseAi = ReadBool(value, key); break;
                    case "player_action": player = ReadGuid(value, key, allowZero: true); break;
                    case "player_action_seconds":
                        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double seconds) || !(seconds >= 0.1 && seconds <= 10))
                            throw new FormatException(key + " must be a number in 0.1..10");
                        cut = (long)Math.Round(seconds * 65536.0);
                        break;
                    case "minion_actions":
                        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 4)
                            throw new FormatException(key + " must be an array of 1..4 action GUIDs");
                        actions = new long[value.GetArrayLength()];
                        int i = 0;
                        foreach (var action in value.EnumerateArray()) actions[i++] = ReadGuid(action, key, allowZero: false);
                        break;
                    default: throw new FormatException("unknown key '" + key + "'");
                }
            }
            if (enabled && actions.Length == 0) throw new FormatException("minion_actions is required when enabled");
            return new BossEntrance(enabled, player, cut, actions, invincible, pauseAi);
        }

        private static bool ReadBool(JsonElement value, string key) => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new FormatException(key + " must be a boolean");

        private static long ReadGuid(JsonElement value, string key, bool allowZero)
        {
            long guid = 0;
            bool valid;
            try
            {
                valid = value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out guid) : value.ValueKind == JsonValueKind.String &&
                    long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out guid);
            }
            catch (InvalidOperationException e) { throw new FormatException(key + " contains invalid Unicode", e); }
            if (!valid || guid < (allowZero ? 0 : 1))
                throw new FormatException(key + " must be a positive integer GUID" + (allowZero ? " or 0" : ""));
            return guid;
        }
    }

    internal enum EntranceCommand { Wait, Execute, Finish }

    internal sealed class EntranceRun
    {
        private readonly BossEntrance _spec;
        private readonly long _playerCutAt;
        private long _stepStart;
        private int _index = -1;
        private bool _executed, _startedExecuting, _playerCut;
        private string _reason;

        internal EntranceRun(BossEntrance spec, long now)
        {
            _spec = spec;
            _stepStart = now;
            _playerCutAt = now + spec.PlayerCutRaw;
        }

        internal (EntranceCommand Command, int Index, string Reason) Start() => _reason != null
            ? (EntranceCommand.Finish, _index, _reason) : _index < 0 ? Execute(0, _stepStart) : (EntranceCommand.Wait, _index, null);

        internal (EntranceCommand Command, int Index, string Reason) Next(long now, bool alive, bool executingCurrent)
        {
            if (_reason != null) return (EntranceCommand.Finish, _index, _reason);
            if (!alive) return Finish("minion gone");
            if (now < _stepStart) return Finish("clock went backwards");
            if (_index < 0) return Execute(0, now);
            if (_executed && !_startedExecuting) return Finish("action " + (_index + 1) + " did not start");
            long elapsed = now - _stepStart;
            if (!_executed || elapsed < (1L << 14)) return (EntranceCommand.Wait, _index, null);
            if (executingCurrent) return elapsed > (20L << 16) ? Finish("action " + (_index + 1) + " timed out") : (EntranceCommand.Wait, _index, null);
            return _index + 1 < _spec.MinionActions.Length ? Execute(_index + 1, now) : Finish("done");
        }

        internal void Executed(long now, bool startedExecuting)
        {
            _stepStart = now;
            _executed = true;
            _startedExecuting = startedExecuting;
        }

        internal bool PlayerCutDue(long now)
        {
            if (_playerCut || _spec.PlayerAction == 0 || now < _playerCutAt) return false;
            _playerCut = true;
            return true;
        }

        private (EntranceCommand, int, string) Execute(int index, long now)
        {
            _index = index;
            _stepStart = now;
            _executed = false;
            return (EntranceCommand.Execute, index, null);
        }

        private (EntranceCommand, int, string) Finish(string reason)
        {
            _reason = reason;
            return (EntranceCommand.Finish, _index, reason);
        }
    }
}
