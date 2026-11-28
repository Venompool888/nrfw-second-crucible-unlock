using System;
using System.Globalization;
using Necromancy;

internal static class Program
{
    private static int _tests, _failures;

    private static int Main()
    {
        var raise = new MinionLookProfile(new(0.55f, 0.72f, 0.78f, 1), null, new(0.6f, 2.2f, 2.6f, 1), null, 0.35f, null, 0.85f);
        var revive = new MinionLookProfile(new(0.66f, 0.60f, 0.82f, 1), null, new(1.6f, 0.7f, 3.0f, 1), null, 0.35f, null, 0.85f);
        var expected = new MinionLookConfig(true, false, 5, raise, revive, raise);
        var (defaults, warnings) = MinionLookPolicy.Parse(MinionLookPolicy.DefaultFileText);
        Check(defaults == expected && warnings.Count == 0, "shipped file parses to all defaults without warnings");
        Check(MinionLookPolicy.Parse("").Config == expected, "empty file uses defaults");

        var (none, noneWarnings) = MinionLookPolicy.Parse("raise.tint=none\nraise.overlay=none\nraise.effect_color=none\nraise.emissive=none\n" +
            "raise.effect_amount=none\nraise.effect_local_y=none\nraise.dithered_alpha=none");
        Check(none.Raise == new MinionLookProfile(null, null, null, null, null, null, null) && none.Revive == revive && noneWarnings.Count == 0,
            "none disables each property, without changing other profiles");
        const string customText = "# comment\n\n LoOk = stock # inline\n BlooD = on\n REFRESH_SECONDS = 2.5\n" +
            " ReViVe.TiNt = 1.25, 2.5, 3.75\nRevive.Overlay=0.25,0.5,0.75,0.125\nBoss.Emissive=1,2,3,4\nboss.effect_local_y=1.5";
        var (custom, customWarnings) = MinionLookPolicy.Parse(customText);
        Check(!custom.Ghost && custom.Blood && custom.RefreshSeconds == 2.5f && customWarnings.Count == 0,
            "mixed-case keys, comments and global overrides");
        Check(custom.Revive.Tint == new LookColor(1.25f, 2.5f, 3.75f, 1) && custom.Revive.Overlay == new LookColor(0.25f, 0.5f, 0.75f, 0.125f) &&
              custom.Boss.Emissive == new LookColor(1, 2, 3, 4) && custom.Boss.EffectLocalY == 1.5f, "three-component color adds alpha, four-component color keeps it");

        var (limits, limitWarnings) = MinionLookPolicy.Parse("refresh_seconds=601\nraise.effect_amount=-1\nrevive.effect_amount=5\n" +
            "raise.effect_local_y=5\nrevive.effect_local_y=-1\nraise.dithered_alpha=0\nrevive.dithered_alpha=2\nraise.tint=-1,65,0.5,100\nboss.tint=65,-1,80,-1");
        Check(limits.RefreshSeconds == 600 && MinionLookPolicy.Parse("refresh_seconds=-1").Config.RefreshSeconds == 0 && limitWarnings.Count == 0,
            "refresh clamps to 0–600 without warnings");
        Check(limits.Raise.EffectAmount == 0 && limits.Revive.EffectAmount == 4, "effect amount clamps to 0–4");
        Check(limits.Raise.EffectLocalY == 4 && limits.Revive.EffectLocalY == 0, "local Y clamps to 0–4");
        Check(limits.Raise.DitheredAlpha == 0.05f && limits.Revive.DitheredAlpha == 1, "dithered alpha clamps to 0.05–1");
        Check(limits.Raise.Tint == new LookColor(0, 64, 0.5f, 64) && limits.Boss.Tint == new LookColor(64, 0, 64, 0), "color clamps RGBA to 0–64");

        foreach (var (key, value) in new[] { ("raise.tint", "1,2"), ("raise.tint", "1,2,3,4,5"), ("raise.tint", "1,xxx,3"),
                     ("raise.tint", "NaN,2,3"), ("raise.emissive", "2,3,Infinity"), ("boss.effect_amount", "false"),
                     ("revive.dithered_alpha", "NaN"), ("raise.effect_local_y", "1e99"), ("refresh_seconds", "none"),
                     ("refresh_seconds", "1,5"), ("look", "purple"), ("blood", "true") })
        {
            var (fallback, errors) = MinionLookPolicy.Parse(key + "=" + value);
            Check(fallback == expected && errors.Count == 1 && errors[0].Contains(key), "invalid value falls back: " + key + "=" + value);
        }
        var (duplicate, duplicateWarnings) = MinionLookPolicy.Parse("raise.effect_amount=2\nraise.effect_amount=bad\nblood=on");
        Check(duplicate.Raise.EffectAmount == 0.35f && duplicate.Blood && duplicateWarnings.Count == 1, "invalid override resets only that key");
        var (unknown, unknownWarnings) = MinionLookPolicy.Parse("shade=red\nboss.unknown=5");
        Check(unknown == expected && unknownWarnings.Count == 2 && unknownWarnings[0].Contains("shade") && unknownWarnings[1].Contains("boss.unknown"),
            "one warning per unknown key");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var (german, germanWarnings) = MinionLookPolicy.Parse(customText);
            Check(german == custom && germanWarnings.Count == 0, "de-DE still parses decimal points and comma-separated colors");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }

        Check(MinionLookPolicy.ProfileName(false, false) == "raise", "ordinary raise profile");
        Check(MinionLookPolicy.ProfileName(false, true) == "revive", "revive profile");
        Check(MinionLookPolicy.ProfileName(true, false) == "boss" && MinionLookPolicy.ProfileName(true, true) == "boss", "boss profile takes precedence");
        Console.WriteLine($"{_tests - _failures}/{_tests} minion look checks passed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string label)
    {
        ++_tests;
        if (!ok) ++_failures;
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + label);
    }
}
