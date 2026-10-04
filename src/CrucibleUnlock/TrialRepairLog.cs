using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using PatchOwner = HarmonyLib.Harmony;

namespace CrucibleUnlock
{
    // Simulation callbacks enqueue values only. Loader logging runs on OnUpdate.
    internal static class TrialRepairLog
    {
        private static readonly ConcurrentQueue<string> Pending = new ConcurrentQueue<string>();
        private static int _count, _dropped;
        public static void Write(string text)
        {
            if (Interlocked.Increment(ref _count) > 256) { Interlocked.Decrement(ref _count); Interlocked.Increment(ref _dropped); return; }
            Pending.Enqueue(DateTime.UtcNow.ToString("O") + " " + text);
        }
        public static void Flush(Action<string> log)
        {
            for (int i = 0; i < 64 && Pending.TryDequeue(out var text); ++i) { Interlocked.Decrement(ref _count); log(text); }
            int dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped != 0) log("[trial-repair] dropped diagnostic records=" + dropped);
        }
        public static void Patch(PatchOwner harmony, Type type, string name, Type[] args, Type patchType, string prefix = null, string postfix = null, Type returnType = null)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                null, args, null);
            if (method == null || method.ReturnType != (returnType ?? typeof(void))) throw new MissingMethodException(type.FullName, name);
            HarmonyMethod Hook(string value) => value == null ? null : new HarmonyMethod(patchType.GetMethod(value, BindingFlags.NonPublic | BindingFlags.Static));
            harmony.Patch(method, prefix: Hook(prefix), postfix: Hook(postfix));
            var info = PatchOwner.GetPatchInfo(method);
            bool found = false;
            if (info != null) foreach (string owner in info.Owners) if (owner == harmony.Id) found = true;
            if (!found) throw new InvalidOperationException("Patch not registered: " + type.FullName + "." + name);
        }
    }
}
