namespace Necromancy
{
    // Pure data and decisions; no game types so the offline audit can compile this file alone.
    internal static class NecromancyPolicy
    {
        internal const string Version = "0.1.0";

        // Public Build 22928553 rune items (staff/wand, CompatibleClasses [25, 24], ThomasApproved=true).
        internal const long RaiseMinionRune = 2160606268309502004;   // raiseMinion: fixed npcRisenWildlingSwordA
        internal const long ReviveRune = 565977864064423183;         // revive: UseCorpseNpcType=true
        internal const long HealMinionsRune = 4408421902141782894;   // healMinions: flat heal 50

        internal static readonly long[] Runes = { RaiseMinionRune, ReviveRune, HealMinionsRune };
        internal static readonly string[] RuneKeys = { "raiseMinion", "revive", "healMinions" };

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
        };

        // Names of required systems absent from the created tree, in RequiredSystems order.
        internal static string[] Missing(System.Collections.Generic.ICollection<string> present)
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (string name in RequiredSystems) if (!present.Contains(name)) missing.Add(name);
            return missing.ToArray();
        }

        // Grant a rune only into a known hero and only when the hero holds none of it anywhere.
        internal static bool ShouldGrant(bool requested, bool verified, bool heroValid, int ownedCount) =>
            requested && verified && heroValid && ownedCount == 0;
    }
}
