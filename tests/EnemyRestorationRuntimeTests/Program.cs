using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CrucibleUnlock;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;

static class Program
{
    static readonly List<string> Failures = new();
    static int checks, loaderUpdates, missingPumps;
    static void Check(bool condition, string why) { ++checks; if (!condition) Failures.Add(why); }
    // Drives the exact no-argument pump now authorized for ModMain.OnUpdate.
    // Reflection lets the frozen R3 source compile and fail by missing dispatch,
    // without synthesizing readiness or directly calling a legacy callback.
    static void LoaderUpdate()
    {
        ++loaderUpdates;
        var pump = typeof(EnemyRestorationRuntime).GetMethod("PumpCurrentViews", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            null, Type.EmptyTypes, null);
        if (pump == null) { ++missingPumps; return; }
        Check(pump.ReturnType == typeof(void), "Exact PumpCurrentViews() void ABI");
        try { pump.Invoke(null, null); }
        catch (TargetInvocationException error) { Failures.Add("Pump threw: " + error.InnerException); }
    }
    static RuntimeAssetResolutionContext Context()
    { var ctx = new RuntimeAssetResolutionContext(); Use(ctx); return ctx; }
    static void Use(RuntimeAssetResolutionContext ctx)
    {
        Core.AssetCtx = ctx;
        Global.Resolve = guid => ctx != null && ctx.m_assetByGuid.TryGetValue(guid, out var asset) ? asset : null;
    }
    static int Main(string[] args)
    {
        string source = args.Length == 0 ? "../../src/CrucibleUnlock/EnemyRestorationRuntime.cs" : args[0];
        EnemyRestorationRuntime.Install("managed-fixture", attachSimulationHook: false);
        Check(BuildGuard.Checks == 1, "Actual Install invokes build guard");
        Check(EnemyRestorationRuntime.Catalog.Profiles.Length == 10, "Actual embedded catalog has all ten profiles");
        Check(!TrialRepairLog.Hooks.Any(h => h.TargetType == typeof(RuntimeAssetResolutionContext) && h.Method == "Update"),
            "Install must not depend on the uncalled RuntimeAssetResolutionContext.Update hook");
        Check(TrialRepairLog.Hooks.Count(h => h.TargetType == typeof(Il2CppMoon.Playmode.InitializeQuantumAssetDatabase) && h.Method == "InitializeResourceManager") == 1,
            "Exact ResourceManager reset hook retained");

        Use(null); EnemyRestorationViews.LutReady = false;
        LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == 0, "No current context remains unavailable without Ensure");
        Core.AssetCtx = new IAssetWrapperResolver(); LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == 0, "Non-runtime resolver remains unavailable without Ensure");
        var first = Context(); LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == 1, "LUT wait visits actual pump and remains unavailable");
        Check(first.m_assetByGuid.Count == 0, "LUT wait does not invent views");
        EnemyRestorationViews.LutReady = true; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady, "Loader pump retries and publishes ready");
        Check(first.m_assetByGuid.Count == 10 && first.m_loadedWrappers.Count == 10, "All ten views/wrappers available before ready");
        Check(TrialRepairLog.Messages.Count(m => m.Contains("VIEWS READY")) == 1, "One first-context ready record");
        int calls = EnemyRestorationViews.Calls; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == calls + 1, "Healthy same context remains ready and is strictly revalidated");

        var profiles = EnemyRestorationRuntime.Catalog.Profiles;
        Global.Resolve = guid => guid.Value == profiles[9].ViewGuid ? new AssetObject { Guid = guid } : first.m_assetByGuid.GetValueOrDefault(guid);
        LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Tenth global lookup pointer mismatch blocks complete readiness");
        Use(first); LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "Global mismatch is retryable");
        Global.Resolve = _ => throw new InvalidOperationException("New resolver not loaded");
        LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Global lookup exception blocks readiness without permanent disable");
        Use(first); LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "Global lookup exception retries on later loader update");

        var retired = new RuntimeAssetResolutionContext();
        var racedCurrent = Context();
        Core.ScriptedReads = new Queue<IAssetWrapperResolver>(new IAssetWrapperResolver[] { retired, racedCurrent });
        calls = EnemyRestorationViews.Calls; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == calls && retired.m_assetByGuid.Count == 0,
            "Resolver identity changes before Ensure: retired context receives no validation or writes");
        LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady && ReferenceEquals(EnemyRestorationViews.LastContext, racedCurrent) && racedCurrent.m_assetByGuid.Count == 10,
            "Identity race does not permanently disable module; next update readies current resolver");

        TrialRepairLog.ResetResourceManager(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Actual reset hook clears readiness synchronously");
        EnemyRestorationViews.LutReady = false;
        var second = Context(); LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && ReferenceEquals(EnemyRestorationViews.LastContext, second), "Resolver swap reads new Core.AssetCtx and waits for LUT");
        EnemyRestorationViews.LutReady = true; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady && second.m_assetByGuid.Count == 10, "Resolver swap becomes ready with ten new views");
        Check(TrialRepairLog.Messages.Count(m => m.Contains("VIEWS READY")) == 3, "One ready record for each first/raced/second successful context");
        Use(null); LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Loss of active context clears a prior ready flag");
        Use(second); LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "Current context restoration can retry");

        EnemyRestorationViews.ThrowEnsure = true; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady, "Strict view validation failure disables readiness");
        EnemyRestorationViews.ThrowEnsure = false;
        int reads = FakeRuntime.Reads.Unity; FakeRuntime.Reads.Forbidden = true;
        LoaderUpdate(); Check(FakeRuntime.Reads.Unity == reads && !EnemyRestorationRuntime.CurrentViewsReady, "Disabled pump performs no Unity reads");
        EnemyRestorationRuntime.StopWithoutUnityReads();
        LoaderUpdate(); Check(FakeRuntime.Reads.Unity == reads && !EnemyRestorationRuntime.CurrentViewsReady, "Explicit shutdown also performs no Unity reads");
        FakeRuntime.Reads.Forbidden = false;

        EnemyRestorationRuntime.Install("managed-fixture", attachSimulationHook: false);
        Use(second); LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady, "Independent shutdown fixture starts enabled and ready");
        reads = FakeRuntime.Reads.Unity;
        EnemyRestorationRuntime.StopWithoutUnityReads();
        FakeRuntime.Reads.Forbidden = true; LoaderUpdate();
        Check(FakeRuntime.Reads.Unity == reads && !EnemyRestorationRuntime.CurrentViewsReady,
            "Shutdown from enabled state performs no Unity reads");
        FakeRuntime.Reads.Forbidden = false;
        Check(missingPumps == 0, "Actual runtime must expose the loader-driven PumpCurrentViews entry");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
            a.GetName().Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)), "No game/Unity/Il2Cpp assemblies loaded");

        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
        var report = new { source = Path.GetFullPath(source), source_sha256 = hash, actual_source_compiled = true, managed_fakes_only = true,
            checks, loader_updates = loaderUpdates, missing_pump_dispatches = missingPumps, ensure_calls = EnemyRestorationViews.Calls,
            ready_records = TrialRepairLog.Messages.Count(m => m.Contains("VIEWS READY")), passed = Failures.Count == 0, failures = Failures,
            limitation = "Exercises actual runtime Install/pump with managed Core/View/Harmony fakes. Does not execute game DLLs, validate ModMain PE wiring, or establish live R4 readiness." };
        if (args.Length > 1) File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        Console.WriteLine($"EnemyRestorationRuntime actual-source managed tests: {checks} checks; {Failures.Count} failures; {missingPumps} missing pump dispatches; {EnemyRestorationViews.Calls} Ensure calls; no game DLLs.");
        return Failures.Count == 0 ? 0 : 1;
    }
}
