using System;
using System.Globalization;
using System.Text.Json;

namespace Necromancy
{
    internal sealed record BossEntrance(bool Enabled, long PlayerAction, long PlayerCutRaw, long[] MinionActions, bool Invincible, bool PauseAi,
        long MinionDelayRaw = 0, long MinionStartRaw = 0);

    internal static class BossEntrancePolicy
    {
        private const long DefaultCutRaw = 2L << 16;

        internal static BossEntrance DefaultFor(long npc) => npc == 790324362694538264
            ? new BossEntrance(true, 203502697017737707, SecondsRaw(2.4), new[] { 2918908058815642059, 2653262915339319819 }, true, true,
                SecondsRaw(1.55), SecondsRaw(2.45))
            : null;

        internal static BossEntrance Read(JsonElement rule, long npc)
        {
            if (!rule.TryGetProperty("entrance", out var entrance)) return DefaultFor(npc);
            if (entrance.ValueKind != JsonValueKind.Object) throw new FormatException("entrance must be an object");
            bool enabled = true, invincible = true, pauseAi = true;
            long player = 0, cut = DefaultCutRaw, delay = 0, start = 0;
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
                    case "player_action_seconds": cut = ReadSeconds(value, key, 0.1); break;
                    case "minion_delay_seconds": delay = ReadSeconds(value, key, 0); break;
                    case "minion_start_seconds": start = ReadSeconds(value, key, 0); break;
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
            return new BossEntrance(enabled, player, cut, actions, invincible, pauseAi, delay, start);
        }

        private static long SecondsRaw(double seconds) => (long)Math.Round(seconds * 65536.0);

        private static long ReadSeconds(JsonElement value, string key, double min)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double seconds) || !(seconds >= min && seconds <= 10))
                throw new FormatException(key + " must be a number in " + min.ToString(CultureInfo.InvariantCulture) + "..10");
            return SecondsRaw(seconds);
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
        private readonly long _start, _playerCutAt;
        private long _stepStart;
        private int _index = -1;
        private bool _executed, _startedExecuting, _playerCut;
        private string _reason;

        internal bool InPrelude { get; private set; }

        internal EntranceRun(BossEntrance spec, long now)
        {
            _spec = spec;
            _start = _stepStart = now;
            _playerCutAt = now + spec.PlayerCutRaw;
        }

        internal (EntranceCommand Command, int Index, string Reason, long StartRaw, bool Prelude) Start() => _reason != null
            ? (EntranceCommand.Finish, _index, _reason, 0, false) : _index < 0 ? Execute(0, _start) : (EntranceCommand.Wait, _index, null, 0, false);

        internal (EntranceCommand Command, int Index, string Reason, long StartRaw, bool Prelude) Next(long now, bool alive, bool executingCurrent)
        {
            if (_reason != null) return (EntranceCommand.Finish, _index, _reason, 0, false);
            if (!alive) return Finish("minion gone");
            if (now < _stepStart) return Finish("clock went backwards");
            if (_index < 0) return Execute(0, now);
            if (InPrelude)
            {
                if (now - _start >= _spec.MinionDelayRaw) return Execute(0, now);
                _stepStart = now;
                return (EntranceCommand.Wait, _index, null, 0, false);
            }
            if (_executed && !_startedExecuting) return Finish("action " + (_index + 1) + " did not start");
            long elapsed = now - _stepStart;
            if (!_executed || elapsed < (1L << 14)) return (EntranceCommand.Wait, _index, null, 0, false);
            if (executingCurrent) return elapsed > (20L << 16) ? Finish("action " + (_index + 1) + " timed out") : (EntranceCommand.Wait, _index, null, 0, false);
            return _index + 1 < _spec.MinionActions.Length ? Execute(_index + 1, now) : Finish("done");
        }

        internal void Executed(long now, bool startedExecuting)
        {
            if (InPrelude) return;
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

        private (EntranceCommand, int, string, long, bool) Execute(int index, long now)
        {
            InPrelude = _index < 0 && _spec.MinionDelayRaw > 0;
            _index = index;
            _stepStart = now;
            _executed = false;
            return (EntranceCommand.Execute, index, null, index == 0 && !InPrelude ? _spec.MinionStartRaw : 0, InPrelude);
        }

        private (EntranceCommand, int, string, long, bool) Finish(string reason)
        {
            _reason = reason;
            return (EntranceCommand.Finish, _index, reason, 0, false);
        }
    }
}
