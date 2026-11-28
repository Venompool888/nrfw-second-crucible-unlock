using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Necromancy
{
    internal readonly record struct LookColor(float R, float G, float B, float A);

    internal sealed record MinionLookProfile(LookColor? Tint, LookColor? Overlay, LookColor? EffectColor, LookColor? Emissive,
        float? EffectAmount, float? EffectLocalY, float? DitheredAlpha);

    internal sealed record MinionLookConfig(bool Ghost, bool Blood, float RefreshSeconds,
        MinionLookProfile Raise, MinionLookProfile Revive, MinionLookProfile Boss);

    internal static class MinionLookPolicy
    {
        private static readonly MinionLookProfile Raise = new(new(0.55f, 0.72f, 0.78f, 1), null, new(0.6f, 2.2f, 2.6f, 1), null, 0.35f, null, 0.85f);
        private static readonly MinionLookProfile Revive = new(new(0.66f, 0.60f, 0.82f, 1), null, new(1.6f, 0.7f, 3.0f, 1), null, 0.35f, null, 0.85f);
        internal static readonly MinionLookConfig Default = new(true, false, 5, Raise, Revive, Raise);

        internal const string DefaultFileText = @"# Minion appearance: ghost or stock.
look = ghost
# Allow minion blood buildup: off or on.
blood = off
# Seconds between ghost material refreshes (0 disables refresh).
refresh_seconds = 5

# Raise base tint (RGB or RGBA, or none).
raise.tint = 0.55,0.72,0.78,1
# Raise overlay color.
raise.overlay = none
# Raise effect glow color.
raise.effect_color = 0.6,2.2,2.6,1
# Raise emissive color.
raise.emissive = none
# Raise effect strength.
raise.effect_amount = 0.35
# Raise effect opacity along local Y.
raise.effect_local_y = none
# Raise dithered opacity (1 is opaque).
raise.dithered_alpha = 0.85

# Revive base tint.
revive.tint = 0.66,0.60,0.82,1
# Revive overlay color.
revive.overlay = none
# Revive effect glow color.
revive.effect_color = 1.6,0.7,3.0,1
# Revive emissive color.
revive.emissive = none
# Revive effect strength.
revive.effect_amount = 0.35
# Revive effect opacity along local Y.
revive.effect_local_y = none
# Revive dithered opacity (1 is opaque).
revive.dithered_alpha = 0.85

# Boss summon base tint.
boss.tint = 0.55,0.72,0.78,1
# Boss summon overlay color.
boss.overlay = none
# Boss summon effect glow color.
boss.effect_color = 0.6,2.2,2.6,1
# Boss summon emissive color.
boss.emissive = none
# Boss summon effect strength.
boss.effect_amount = 0.35
# Boss summon effect opacity along local Y.
boss.effect_local_y = none
# Boss summon dithered opacity (1 is opaque).
boss.dithered_alpha = 0.85
";

        internal static string ProfileName(bool isBossAction, bool isReviveAction) => isBossAction ? "boss" : isReviveAction ? "revive" : "raise";

        internal static (MinionLookConfig Config, IReadOnlyList<string> Warnings) Parse(string text)
        {
            var config = Default;
            var warnings = new List<string>();
            using var reader = new StringReader(text);
            for (int lineNumber = 1; reader.ReadLine() is string line; lineNumber++)
            {
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line[..comment];
                line = line.Trim();
                if (line.Length == 0) continue;
                int equals = line.IndexOf('=');
                string key = (equals < 0 ? line : line[..equals]).Trim().ToLowerInvariant();
                string value = equals < 0 ? "" : line[(equals + 1)..].Trim().ToLowerInvariant();
                switch (key)
                {
                    case "look": config = config with { Ghost = Choice("ghost", "stock", Default.Ghost) }; break;
                    case "blood": config = config with { Blood = Choice("on", "off", Default.Blood) }; break;
                    case "refresh_seconds": config = config with { RefreshSeconds = Number(0, 600, Default.RefreshSeconds) }; break;
                    default:
                        int dot = key.IndexOf('.');
                        string profile = dot < 0 ? "" : key[..dot];
                        switch (profile)
                        {
                            case "raise": config = config with { Raise = Update(config.Raise, Default.Raise, key[(dot + 1)..]) }; break;
                            case "revive": config = config with { Revive = Update(config.Revive, Default.Revive, key[(dot + 1)..]) }; break;
                            case "boss": config = config with { Boss = Update(config.Boss, Default.Boss, key[(dot + 1)..]) }; break;
                            default: Unknown(); break;
                        }
                        break;
                }

                MinionLookProfile Update(MinionLookProfile current, MinionLookProfile defaults, string property)
                {
                    switch (property)
                    {
                        case "tint": return current with { Tint = Color(defaults.Tint) };
                        case "overlay": return current with { Overlay = Color(defaults.Overlay) };
                        case "effect_color": return current with { EffectColor = Color(defaults.EffectColor) };
                        case "emissive": return current with { Emissive = Color(defaults.Emissive) };
                        case "effect_amount": return current with { EffectAmount = OptionalNumber(0, 4, defaults.EffectAmount) };
                        case "effect_local_y": return current with { EffectLocalY = OptionalNumber(0, 4, defaults.EffectLocalY) };
                        case "dithered_alpha": return current with { DitheredAlpha = OptionalNumber(0.05f, 1, defaults.DitheredAlpha) };
                        default: Unknown(); return current;
                    }
                }

                void Unknown() => warnings.Add($"line {lineNumber}: unknown key '{key}'");
                T Invalid<T>(T fallback)
                {
                    warnings.Add($"line {lineNumber}: invalid value for '{key}'; using default");
                    return fallback;
                }
                bool Choice(string on, string off, bool fallback) => value == on ? true : value == off ? false : Invalid(fallback);
                float Number(float min, float max, float fallback) =>
                    TryNumber(value, out float number) ? Math.Clamp(number, min, max) : Invalid(fallback);
                float? OptionalNumber(float min, float max, float? fallback) => value == "none" ? null :
                    TryNumber(value, out float number) ? Math.Clamp(number, min, max) : Invalid(fallback);
                LookColor? Color(LookColor? fallback)
                {
                    if (value == "none") return null;
                    string[] parts = value.Split(',');
                    float a = 1;
                    if (parts.Length is not (3 or 4) || !TryNumber(parts[0], out float r) || !TryNumber(parts[1], out float g) ||
                        !TryNumber(parts[2], out float b) || (parts.Length == 4 && !TryNumber(parts[3], out a))) return Invalid(fallback);
                    return new LookColor(Math.Clamp(r, 0, 64), Math.Clamp(g, 0, 64), Math.Clamp(b, 0, 64), Math.Clamp(a, 0, 64));
                }
            }
            return (config, warnings.AsReadOnly());
        }

        private static bool TryNumber(string value, out float number) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && float.IsFinite(number);
    }
}
