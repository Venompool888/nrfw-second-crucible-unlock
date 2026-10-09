using System;
using System.Collections.Generic;

// Every dependency that would attach hooks or touch Unity/IL2CPP is replaced at
// the entry point's boundary. These fixtures record calls; they never load game
// DLLs, create files, attach detours or interpret real player configuration.
namespace MelonLoader
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute
    { public MelonInfoAttribute(Type type, string name, string version, string author, string url) { } }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonGameAttribute : Attribute
    { public MelonGameAttribute(string author, string game) { } }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonAdditionalDependenciesAttribute : Attribute
    { public MelonAdditionalDependenciesAttribute(string name) { } }
    public class MelonMod
    {
        public MelonLogger.Instance LoggerInstance { get; } = new MelonLogger.Instance();
        public virtual void OnInitializeMelon() { }
        public virtual void OnLateInitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnLateUpdate() { }
        public virtual void OnApplicationQuit() { }
    }
    public static class MelonLogger
    {
        public sealed class Instance
        {
            public readonly List<string> Messages = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public void Msg(string message) { Messages.Add(message); }
            public void Error(string message) { Errors.Add(message); }
            public void Warning(string message) { Warnings.Add(message); }
        }
    }
    public sealed class MelonPreferences_Entry<T>
    { public T Value { get; set; } }
    public sealed class MelonPreferences_Category
    {
        public MelonPreferences_Entry<T> CreateEntry<T>(string name, T value, string display, string description)
        {
            MelonPreferences.Registered.Add(name);
            if (MelonPreferences.Existing.TryGetValue(name, out object configured))
                value = (T)configured;
            return new MelonPreferences_Entry<T> { Value = value };
        }
    }
    public static class MelonPreferences
    {
        public static readonly List<string> Registered = new List<string>();
        public static readonly Dictionary<string, object> Existing = new Dictionary<string, object>();
        public static MelonPreferences_Category CreateCategory(string id, string display)
            => new MelonPreferences_Category();
    }
}

namespace CrucibleUnlock
{
    internal static class Boundary
    {
        public static readonly List<string> Calls = new List<string>();
        public static bool RejectBuild, RejectRuntime;
        public static int ConfiguredWeaponClass;
        public static void Reset()
        { Calls.Clear(); RejectBuild = false; RejectRuntime = false; ConfiguredWeaponClass = -1; }
        public static void Install(string name) { Calls.Add(name); }
    }
    internal static class BuildGuard
    {
        public static void Verify(string path, Action<string> log)
        {
            Boundary.Calls.Add("verify-build");
            if (Boundary.RejectBuild) throw new InvalidOperationException("fixture build mismatch");
        }
    }
    internal static class RuntimeUnlock
    {
        public static void Install(Action<string> info, Action<string> error)
        {
            Boundary.Install("runtime-unlock");
            if (Boundary.RejectRuntime) throw new InvalidOperationException("fixture runtime installation failed");
        }
        public static void Flush() { Boundary.Calls.Add("runtime-flush"); }
    }
    internal static class BrokenWarrickMusicGuard
    {
        public static void Install(Action<string> info, Action<string> error) { Boundary.Install("music-guard"); }
        public static void Dispose() { Boundary.Calls.Add("music-rollback"); }
    }
    internal static class WarrickPhase2TargetRepair
    {
        public static void Install(Action<string> info, Action<string> error) { Boundary.Install("phase2-repair"); }
        public static void Tick() { Boundary.Calls.Add("phase2-tick"); }
        public static void StopWithoutUnityReads() { Boundary.Calls.Add("phase2-stop"); }
    }
    internal static class BossRushProgressionRepair { public static void Install() { Boundary.Install("progression-repair"); } }
    internal static class BossTraceDropRepair { public static void Install() { Boundary.Install("trace-drop"); } }
    internal static class BrokenVowBossRepair { public static void Install() { Boundary.Install("broken-vow"); } }
    internal static class BossTracePickupDiagnostics { public static void Install() { Boundary.Install("trace-pickup"); } }
    internal static class HuskBossNameRepair { public static void Install() { Boundary.Install("husk-name"); } }
    internal static class EchoCapRepair { public static void Install() { Boundary.Install("echo-cap"); } }
    internal static class RitualAnimatorRepair { public static void Install() { Boundary.Install("ritual-animator"); } }
    internal static class RitualViewRepair { public static void Install() { Boundary.Install("ritual-view"); } }
    internal static class BowgunInputRepair
    {
        public static void Configure(int value) { Boundary.ConfiguredWeaponClass = value; }
        public static void Install() { Boundary.Install("bowgun-input"); }
        public static void UpdateLocalHero() { Boundary.Calls.Add("bowgun-update"); }
    }
    internal static class TrialRepairLog { public static void Flush(Action<string> info) { } }
    internal static class BossMotionTrace
    {
        public static void Install(MotionTraceSink sink, Action<string> info, Action<string> error) { Boundary.Install("motion-sampling"); }
        public static void LateUpdate() { Boundary.Calls.Add("motion-sample"); }
        public static void StopSampling() { Boundary.Calls.Add("motion-stop"); }
    }
    internal sealed class MotionTraceRecord { public string note; }
    internal sealed class MotionTraceSink : IDisposable
    {
        public MotionTraceSink(string path) { Boundary.Calls.Add("motion-writer"); }
        public string Error => null;
        public long DroppedCount => 0;
        public long WrittenCount => 0;
        public MotionTraceRecord NewRecord(string kind) => new MotionTraceRecord();
        public bool TryWrite(MotionTraceRecord record) => true;
        public void Dispose() { }
    }
}
