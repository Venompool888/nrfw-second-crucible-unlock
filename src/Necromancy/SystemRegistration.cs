using System;
using System.Collections.Generic;
using System.Threading;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppQuantum;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private static readonly List<SystemBase> Added = new();
        private static readonly Dictionary<string, SystemBase> Found = new();
        private static int _enableChecks, _corpseTicks;

        // ---- simulation tree -------------------------------------------------------------

        private static void AfterCreateSystems(ref Il2CppReferenceArray<SystemBase> __result)
        {
            if (!_enabled || __result == null) return;
            try
            {
                ResetSessionState();
                if (_roarEnabled) _reactionsReloadRequested = true;
                var present = new HashSet<string>();
                foreach (var system in __result) Collect(system, present, 0);
                string[] missing = NecromancyPolicy.Missing(present);
                Write("systems: CorpseMarker=" + present.Contains(NecromancyPolicy.CorpseMarkerSystem) +
                      "; RaisedMinion=" + present.Contains(NecromancyPolicy.RaisedMinionSystem) + "; total=" + present.Count);
                if (missing.Length == 0) return;
                var grown = new Il2CppReferenceArray<SystemBase>(__result.Length + missing.Length);
                for (int i = 0; i < __result.Length; ++i) grown[i] = __result[i];
                for (int i = 0; i < missing.Length; ++i)
                {
                    SystemBase created = missing[i] == NecromancyPolicy.CorpseMarkerSystem ? new CorpseMarkerSystem() : new RaisedMinionSystem();
                    grown[__result.Length + i] = created;
                    Found[missing[i]] = created;
                    Added.Add(created);
                }
                __result = grown;
                Write("APPENDED missing systems: " + string.Join(", ", missing));
            }
            catch (Exception e) { Error("create-systems", e); }
        }

        private static void Collect(SystemBase system, HashSet<string> present, int depth)
        {
            if (system == null || depth > 16) return;
            string name = system.GetIl2CppType().Name;
            present.Add(name);
            if (Array.IndexOf(NecromancyPolicy.RequiredSystems, name) >= 0 && !Found.ContainsKey(name)) Found[name] = system;
            var children = system._children;
            if (children != null) foreach (var child in children) Collect(child, present, depth + 1);
        }

        // ---- per-frame work (simulation thread) -------------------------------------------

        private static void AfterEndOfFrame(Frame __0)
        {
            if (!_enabled || __0 == null || !__0.IsVerified) return;
            try
            {
                EnsureEnabled(__0);
                if (_grantRequested && !_grantDone) TryGrant(__0);
                if (_bossEnabled) TickBossRunes(__0);
                TickLifetime(__0);
                if (_roarEnabled)
                {
                    try { TickRoarTraces(__0); }
                    catch (Exception e)
                    {
                        RoarTraces.Clear();
                        RoarError("roar trace", e);
                    }
                }
            }
            catch (Exception e) { Error("end-of-frame", e); return; }

            if (_rosterOff) return;
            try { LogRoster(__0); }
            catch (Exception e) { RosterError("roster", e); }
        }

        private static void EnsureEnabled(Frame f)
        {
            if (_enableChecks >= 3) return;   // first frames of a session only
            ++_enableChecks;
            foreach (var pair in Found)
            {
                bool on = pair.Value.IsEnabledInHierarchy(f);
                if (!on)
                {
                    pair.Value.Enable(f);
                    Write("ENABLED " + pair.Key + " (was off); now=" + pair.Value.IsEnabledInHierarchy(f));
                }
                else if (_enableChecks == 1) Write(pair.Key + " enabled");
            }
        }

        private static void AfterCorpseUpdate(Frame __0)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _corpseTicks) == 1) Write("CorpseMarkerSystem running");
        }
    }
}
