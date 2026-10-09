namespace CrucibleUnlock
{
    // Shared by the runtime registration and the PE metadata-only signature gate.
    internal sealed class BowgunHookSpec
    {
        internal readonly string Assembly, Type, Method, ReturnType, Prefix, Postfix;
        internal readonly string[] Parameters;
        internal BowgunHookSpec(string assembly, string type, string method, string result,
            string[] parameters, string prefix = null, string postfix = null)
        {
            Assembly = assembly; Type = type; Method = method; ReturnType = result;
            Parameters = parameters; Prefix = prefix; Postfix = postfix;
        }
    }

    internal static class BowgunHookPlan
    {
        internal static readonly BowgunHookSpec[] Hooks = {
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.HeroView",
                "UpdateEquipment", "Void", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "System.Single" },
                postfix: "AfterEquipmentUpdate"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.HeroView",
                "GetTempListOfItemActions", "Il2CppSystem.Collections.Generic.List`1<Il2CppQuantum.AssetRefActionData>",
                new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, postfix: "AfterCollectItemActions"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.EntityView",
                "UpdateActionView", "Void", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef", "System.Single" },
                postfix: "AfterEntityActionView"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.EntityView",
                "GetActionView", "Il2CppMoon.Forsaken.ActionView", new[] { "Il2CppQuantum.ActionData", "System.Boolean&" },
                postfix: "AfterResolveActionView"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.ActionView",
                "StartView", "Void", new[] { "Il2CppMoon.Forsaken.EntityView", "System.Single", "System.Int32", "System.Int32" },
                prefix: "BeforeAnimationStart", postfix: "AfterAnimationStart"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.ActionView",
                "TryStartViewQBridge", "System.Boolean", new string[0], postfix: "AfterAnimationBridge"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.ActionView",
                "OnReturnedToPool", "Void", new string[0], prefix: "BeforeAnimationReturn"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.QuantumLocalInputSource",
                "UpdateRInputQuantumInput", "Void", new[] { "Il2CppQuantum.Frame" }, postfix: "AfterInput"),
            // The live overload of the special-combo chain. Native GetSpecialStart#56744 calls
            // GetSpecialCombo#56742, whose return type is a reference type, so a prefix can supply the
            // action list without touching a value-type return. The 0.9.7/0.9.8 candidates hooked the
            // (IAssetResolutionContext, EntityRef, HeroSpecialType) overload instead, which only sits
            // behind this one on a branch that the bowgun's empty armament table never takes.
            new BowgunHookSpec("Il2Cppmoon.quantum.forsaken", "Il2CppQuantum.WeaponStaticData",
                "GetSpecialCombo", "Il2CppMoon.Collections.PooledList`1<Il2CppQuantum.RuntimeActionInfo>",
                new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.HeroSpecialType", "Il2CppQuantum.EntityRef", "Il2CppQuantum.EntityRef", "System.Boolean" },
                prefix: "BeforeSpecialComboList"),
            new BowgunHookSpec("Il2Cppmoon.quantum.forsaken", "Il2CppQuantum.WeaponStaticData",
                "GetProjectile", "Il2CppQuantum.ProjectileData",
                new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" }, prefix: "BeforeProjectile"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.PlayerEquipmentHUD",
                "UpdateState", "Void", new[] { "Il2CppQuantum.Frame", "Il2CppQuantum.EntityRef" },
                prefix: "BeforeHudState", postfix: "AfterHudState"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.RevisedInput",
                "GetButton", "System.Boolean", new[] { "System.Int32", "System.Boolean" }, postfix: "AfterHudButton"),
            new BowgunHookSpec("Il2Cpp__forsaken", "Il2CppMoon.Forsaken.QuantumLocalInputSource",
                "Poll", "Il2CppQuantum.Input", new[] { "Il2CppQuantum.Frame" }, postfix: "AfterPoll"),
            new BowgunHookSpec("Il2Cppmoon.quantum.forsaken", "Il2CppQuantum.PlayerControllerData",
                "OnUpdateEntityInstance", "Void", new[] { "Il2CppQuantum.Frame", "Il2CppPhoton.Deterministic.FP", "Il2CppQuantum.EntityRef" },
                postfix: "AfterControllerUpdate"),
            new BowgunHookSpec("Il2Cppmoon.quantum.forsaken", "Il2CppQuantum.RuntimeArmament",
                "GetCombo", "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1<Il2CppQuantum.RuntimeActionInfo>",
                new[] { "Il2CppQuantum.IAssetResolutionContext", "Il2CppQuantum.ArmamentActionType" }, postfix: "AfterArmamentCombo")
        };
    }
}
