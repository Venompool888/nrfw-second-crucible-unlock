using System.Reflection;
using System.Runtime.CompilerServices;
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
    static bool Publish(Frame frame, bool requireStorage = false)
    {
        var publisher = typeof(EnemyRestorationRuntime).GetMethod("PublishSimulationViews", BindingFlags.Static | BindingFlags.NonPublic);
        if (publisher == null) return false;
        publisher.Invoke(null, new object[] { frame, requireStorage });
        return true;
    }
    static Frame Simulation()
    {
        var frame = new Frame();
        EnemyRestorationRuntime.EnsureSimulationAssets(frame);
        Core.ResourceManager = frame.Context.ResourceManager;
        return frame;
    }
    static bool RejectsPublication(Frame frame, bool storage = false)
    {
        try { Publish(frame, storage); return false; }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { return true; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference PublishUnobservedManager()
    {
        var frame=new Frame();
        EnemyRestorationRuntime.EnsureSimulationAssets(frame);
        Publish(frame);
        return new WeakReference(frame.Context.ResourceManager);
    }
    static int Main(string[] args)
    {
        string source = args.Length == 0 ? "../../src/CrucibleUnlock/EnemyRestorationRuntime.cs" : args[0];
        EnemyRestorationRuntime.Install("managed-fixture", attachSimulationHook: false);
        Check(BuildGuard.Checks == 1, "Actual Install invokes build guard");
        Check(EnemyRestorationActionHooks.Installs == 1, "R10: view hooks install within module owner transaction");
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
        Check(EnemyRestorationViews.Calls == 0, "R5: Unity must wait for simulation-owned views before Ensure");
        Check(typeof(EnemyRestorationRuntime).GetMethod("PublishSimulationViews", BindingFlags.Static | BindingFlags.NonPublic) != null,
            "R5: simulation-owned snapshot publisher must exist");
        Check(first.m_assetByGuid.Count == 0, "Waiting for publication does not write Unity caches");
        var simulation = Simulation();
        LoaderUpdate(); Check(EnemyRestorationViews.Calls == 0, "Known manager without publication still cannot Ensure");
        Check(Publish(simulation), "Actual simulation snapshot can publish all ten views");
        LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == 1, "LUT wait visits actual pump and remains unavailable");
        Check(first.m_assetByGuid.Count == 0, "LUT wait does not invent views");
        EnemyRestorationViews.LutReady = true; EnemyRestorationWeaponViews.Ready = false; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationWeaponViews.Calls == 1,
            "R10: character views alone cannot open third route while private weapon wrappers wait");
        Check(first.m_assetByGuid.Count == 10, "Weapon wrapper wait preserves only the ten completed character views");
        EnemyRestorationWeaponViews.Ready = true; EnemyRestorationKeeperWeaponViews.Ready = false; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationKeeperWeaponViews.Calls == 1 && EnemyRestorationReactionViews.Calls == 0,
            "R10: three private wrappers cannot open route before keeper true item root is available");
        EnemyRestorationKeeperWeaponViews.Ready = true; EnemyRestorationReactionViews.Ready = false; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationReactionViews.Calls == 1,
            "R10: route remains closed until sword parry source clip/timeline is available");
        EnemyRestorationReactionViews.Ready = true; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady, "Loader pump retries and publishes ready");
        Check(first.m_assetByGuid.Count == 13 && first.m_loadedWrappers.Count == 13, "All ten character and three private weapon wrappers available before ready");
        var publishedWeapons = EnemyRestorationWeaponViews.LastWeapons;
        Check(publishedWeapons != null && publishedWeapons.Length == 10 && EnemyRestorationRuntime.Catalog.Profiles.Select((p,i) =>
            p.BlockedActions.Length == 0 ? publishedWeapons[i] == null : ReferenceEquals(publishedWeapons[i],simulation.DynamicAssetDB.Assets[p.PrivateWeaponGuid])).All(v=>v),
            "R10: weapon publication uses exact three simulation clones; seven unmodified weapons are absent");
        Check(EnemyRestorationViews.LastViews != null && EnemyRestorationViews.LastViews.Length == 10 &&
            EnemyRestorationViews.LastViews.Select((v,i) => ReferenceEquals(v,simulation.DynamicAssetDB.Assets[EnemyRestorationRuntime.Catalog.Profiles[i].ViewGuid])).All(v=>v),
            "Ensure receives the exact ten simulation objects rather than Unity-created duplicates");
        Check(Publish(simulation) && TrialRepairLog.Messages.Count(m=>m.Contains("SIMULATION VIEWS PUBLISHED")) == 1,
            "Same manager/same pointers republish is idempotent");
        var weakManager=PublishUnobservedManager(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weakManager.IsAlive,"Published snapshot strongly retains manager even when no Frame/current Core reference remains");
        var snapshotType=typeof(EnemyRestorationRuntime).GetNestedType("SimulationViewSnapshot",BindingFlags.NonPublic);
        Check(snapshotType!=null&&!snapshotType.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Any(f=>
            f.FieldType==typeof(Frame)||f.FieldType==typeof(InstanceDataStorage)),"Snapshot retains no Frame or storage");
        var collisionGuid = EnemyRestorationRuntime.Catalog.Profiles[9].ViewGuid;
        var retained = simulation.DynamicAssetDB.Assets[collisionGuid];
        simulation.DynamicAssetDB.Assets[collisionGuid] = new EntityViewData { Guid=retained.Guid, Path=retained.Path };
        Check(RejectsPublication(simulation), "Same manager cannot republish a different tenth view pointer");
        simulation.DynamicAssetDB.Assets[collisionGuid] = retained;
        var privateGuid = EnemyRestorationRuntime.Catalog.Profiles[1].PrivateWeaponGuid;
        var retainedWeapon = simulation.DynamicAssetDB.Assets[privateGuid];
        simulation.DynamicAssetDB.Assets[privateGuid] = new EnemyWeaponData { Guid=retainedWeapon.Guid,Path=retainedWeapon.Path };
        Check(RejectsPublication(simulation), "R10: same manager cannot republish different private weapon pointer");
        simulation.DynamicAssetDB.Assets[privateGuid] = retainedWeapon;
        Check(TrialRepairLog.Messages.Count(m => m.Contains("VIEWS READY")) == 1, "One first-context ready record");
        int calls = EnemyRestorationViews.Calls; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == calls + 1, "Healthy same context remains ready and is strictly revalidated");

        var profiles = EnemyRestorationRuntime.Catalog.Profiles;
        Global.Resolve = guid => guid.Value == profiles[9].ViewGuid ? new AssetObject { Guid = guid } : first.m_assetByGuid.GetValueOrDefault(guid);
        LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Tenth global lookup pointer mismatch blocks complete readiness");
        Use(first); LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "Global mismatch is retryable");
        Global.Resolve = guid => guid.Value == privateGuid ? new EnemyWeaponData { Guid = guid } : first.m_assetByGuid.GetValueOrDefault(guid);
        LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "R10: private weapon global lookup pointer mismatch blocks readiness");
        Use(first); LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "Private weapon global mismatch is retryable");
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
        Check(EnemyRestorationRuntime.CurrentViewsReady && ReferenceEquals(EnemyRestorationViews.LastContext, racedCurrent) && racedCurrent.m_assetByGuid.Count == 13,
            "Identity race does not permanently disable module; next update readies current resolver");

        TrialRepairLog.ResetResourceManager(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Actual reset hook clears readiness synchronously");
        Check(EnemyRestorationKeeperWeaponViews.Stops == 1 && EnemyRestorationReactionViews.Stops == 1,
            "R10: resolver reset withdraws both animation templates without retaining retired context");
        EnemyRestorationViews.LutReady = false;
        var second = Context(); LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && ReferenceEquals(EnemyRestorationViews.LastContext, second), "Resolver swap reads new Core.AssetCtx and waits for LUT");
        EnemyRestorationViews.LutReady = true; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady && second.m_assetByGuid.Count == 13, "Resolver swap becomes ready with ten character and three weapon wrappers");
        Check(TrialRepairLog.Messages.Count(m => m.Contains("VIEWS READY")) == 3, "One ready record for each first/raced/second successful context");
        Use(null); LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady, "Loss of active context clears a prior ready flag");
        Use(second); LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "Current context restoration can retry");

        var nextSimulation = Simulation();
        var nextContext = Context();
        calls = EnemyRestorationViews.Calls; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls == calls,
            "New manager cannot borrow previously published manager snapshot");
        foreach (var profile in profiles)
        {
            var slot = nextSimulation.InstanceDataStorage.m_storageEntityViewData;
            slot.m_storage.Add((EntityViewData)nextSimulation.DynamicAssetDB.Assets[profile.ViewGuid]);
            slot.m_lookup.Add(profile.ViewGuid, slot.m_storage.Count);
        }
        var lastSlot = nextSimulation.InstanceDataStorage.m_storageEntityViewData;
        lastSlot.m_lookup[profiles[9].ViewGuid]=0;
        Check(RejectsPublication(nextSimulation,true),"Zero storage lookup index rejected before array access");
        lastSlot.m_lookup[profiles[9].ViewGuid]=lastSlot.m_storage.Count+1;
        Check(RejectsPublication(nextSimulation,true),"Out-of-range storage lookup index rejected before array access");
        lastSlot.m_lookup[profiles[9].ViewGuid]=10;
        var storageLast = lastSlot.m_storage[9]; lastSlot.m_storage[9] = new EntityViewData {Guid=storageLast.Guid,Path=storageLast.Path};
        Check(RejectsPublication(nextSimulation,true), "Tenth storage pointer mismatch prevents any snapshot publication");
        LoaderUpdate(); Check(EnemyRestorationViews.Calls == calls, "Failed storage publication remains waiting without Ensure");
        lastSlot.m_storage[9] = storageLast;
        Check(Publish(nextSimulation,true), "Complete exact storage pointers publish successfully");
        LoaderUpdate(); Check(EnemyRestorationRuntime.CurrentViewsReady, "New manager becomes ready after valid publication");
        var readyForFrame = typeof(EnemyRestorationRuntime).GetMethod("IsSimulationViewsReady",BindingFlags.Static|BindingFlags.NonPublic);
        Check(readyForFrame != null && (bool)readyForFrame.Invoke(null,new object[]{nextSimulation}) && !(bool)readyForFrame.Invoke(null,new object[]{simulation}),
            "Ready frame is scoped to current manager, preventing retired frame readiness");
        Core.ScriptedManagers = new Queue<FakeResourceManager>(new[] {nextSimulation.Context.ResourceManager,simulation.Context.ResourceManager});
        calls=EnemyRestorationViews.Calls; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady && EnemyRestorationViews.Calls==calls, "Manager identity race before Ensure performs no writes");
        Core.ResourceManager=nextSimulation.Context.ResourceManager; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady,"Manager identity race retries without disable");
        Core.ScriptedManagers=new Queue<FakeResourceManager>(new[]{nextSimulation.Context.ResourceManager,nextSimulation.Context.ResourceManager,simulation.Context.ResourceManager});
        LoaderUpdate(); Check(!EnemyRestorationRuntime.CurrentViewsReady,"Manager changed after Ensure cannot publish readiness");
        Core.ResourceManager=nextSimulation.Context.ResourceManager; LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady,"Final manager identity mismatch can retry");

        var missing=Simulation(); missing.DynamicAssetDB.Assets.Remove(profiles[9].ViewGuid);
        Check(RejectsPublication(missing),"Incomplete tenth simulation view rejected");
        var badPath=Simulation(); badPath.DynamicAssetDB.Assets[profiles[9].ViewGuid].Path="foreign";
        Check(RejectsPublication(badPath),"Wrong simulation view path rejected");
        var missingWeapon=Simulation(); missingWeapon.DynamicAssetDB.Assets.Remove(profiles[1].PrivateWeaponGuid);
        Check(RejectsPublication(missingWeapon), "R10: missing canonical private weapon prevents publication");
        var badWeaponPath=Simulation(); badWeaponPath.DynamicAssetDB.Assets[profiles[8].PrivateWeaponGuid].Path="foreign";
        Check(RejectsPublication(badWeaponPath), "R10: incorrect private weapon path rejected");
        var badWeaponType=Simulation(); badWeaponType.DynamicAssetDB.Assets[profiles[9].PrivateWeaponGuid]=new EntityViewData {Guid=new(){Value=profiles[9].PrivateWeaponGuid},Path=profiles[9].Path("weapon")};
        Check(RejectsPublication(badWeaponType), "R10: incorrect private weapon asset type rejected");
        Core.ResourceManager=nextSimulation.Context.ResourceManager;
        Use(nextContext);

        EnemyRestorationViews.ThrowEnsure = true; LoaderUpdate();
        Check(!EnemyRestorationRuntime.CurrentViewsReady, "Strict view validation failure disables readiness");
        EnemyRestorationViews.ThrowEnsure = false;
        int reads = FakeRuntime.Reads.Unity; FakeRuntime.Reads.Forbidden = true;
        LoaderUpdate(); Check(FakeRuntime.Reads.Unity == reads && !EnemyRestorationRuntime.CurrentViewsReady, "Disabled pump performs no Unity reads");
        EnemyRestorationRuntime.StopWithoutUnityReads();
        LoaderUpdate(); Check(FakeRuntime.Reads.Unity == reads && !EnemyRestorationRuntime.CurrentViewsReady, "Explicit shutdown also performs no Unity reads");
        Check(EnemyRestorationKeeperWeaponViews.Stops == 2 && EnemyRestorationReactionViews.Stops == 2,
            "R10: shutdown withdraws helper readiness without native teardown reads");
        FakeRuntime.Reads.Forbidden = false;

        EnemyRestorationRuntime.Install("managed-fixture", attachSimulationHook: false);
        Core.ResourceManager=nextSimulation.Context.ResourceManager;
        Use(nextContext); Check(Publish(nextSimulation,true),"Fresh Install can republish after managed snapshot shutdown cleanup"); LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady, "Independent shutdown fixture starts enabled and ready");
        reads = FakeRuntime.Reads.Unity;
        EnemyRestorationRuntime.StopWithoutUnityReads();
        FakeRuntime.Reads.Forbidden = true; LoaderUpdate();
        Check(FakeRuntime.Reads.Unity == reads && !EnemyRestorationRuntime.CurrentViewsReady,
            "Shutdown from enabled state performs no Unity reads");
        FakeRuntime.Reads.Forbidden = false;
        EnemyRestorationRuntime.Install("managed-fixture", attachSimulationHook:true);
        var standalone=new Frame(); Core.ResourceManager=standalone.Context.ResourceManager;
        var standaloneContext=Context();
        typeof(EnemyRestorationRuntime).GetMethod("AfterSimulationInit",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{standalone});
        LoaderUpdate();
        Check(EnemyRestorationRuntime.CurrentViewsReady&&standaloneContext.m_assetByGuid.Count==13&&standalone.InstanceDataStorage.m_storageEntityViewData.m_storage.Count==0,
            "Actual standalone AfterSimulationInit publishes registered views without requiring third-trial storage");
        EnemyRestorationRuntime.StopWithoutUnityReads();
        Check(missingPumps == 0, "Actual runtime must expose the loader-driven PumpCurrentViews entry");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
            a.GetName().Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)), "No game/Unity/Il2Cpp assemblies loaded");

        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
        var report = new { source = Path.GetFullPath(source), source_sha256 = hash, actual_source_compiled = true, managed_fakes_only = true,
            checks, loader_updates = loaderUpdates, missing_pump_dispatches = missingPumps, ensure_calls = EnemyRestorationViews.Calls,
            ready_records = TrialRepairLog.Messages.Count(m => m.Contains("VIEWS READY")), passed = Failures.Count == 0, failures = Failures,
            limitation = "Exercises actual runtime Install/publication/pump with managed Core/View/Harmony fakes. Does not execute game DLLs, validate ModMain PE wiring, or establish live R5 readiness or native rendering." };
        if (args.Length > 1) File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        Console.WriteLine($"EnemyRestorationRuntime actual-source managed tests: {checks} checks; {Failures.Count} failures; {missingPumps} missing pump dispatches; {EnemyRestorationViews.Calls} Ensure calls; no game DLLs.");
        return Failures.Count == 0 ? 0 : 1;
    }
}
