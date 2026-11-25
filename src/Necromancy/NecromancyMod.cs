using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using MelonLoader;

[assembly: MelonInfo(typeof(Necromancy.NecromancyMod), "NRFW Necromancy", Necromancy.NecromancyPolicy.Version, "NRFW research")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
[assembly: MelonAdditionalDependencies("0Harmony")]

namespace Necromancy
{
    // Restores the shipped-but-unreleased raise-minion rune family.
    //  1. Makes sure CorpseMarkerSystem and RaisedMinionSystem exist in the simulation tree
    //     (appends them when SystemSetup filtered them out) and are enabled.
    //  2. Optionally awards raiseMinion / revive / healMinions and the Tome of Healing once to the local hero.
    //  3. Logs the first corpse/raise/heal calls so a play session can show which stage works.
    // Single-player only: every peer of a session would need identical systems.
    public sealed class NecromancyMod : MelonMod
    {
        private static readonly ConcurrentQueue<string> Pending = new();
        private static MelonPreferences_Entry<bool> _grantEntry;
        private static volatile bool _grantRequested, _grantDone;
        private static long _heroRaw;
        private static readonly List<SystemBase> Added = new();
        private static readonly Dictionary<string, SystemBase> Found = new();
        private static int _heroFrames, _enableChecks, _corpseTicks, _raisedTicks, _executes, _canRaise, _heals, _errors;
        private static bool _enabled;
        private static string _logPath;

        public override void OnInitializeMelon()
        {
            var category = MelonPreferences.CreateCategory("NRFWNecromancy", "NRFW Necromancy");
            _grantEntry = category.CreateEntry("grant_runes_once", true,
                description: "Award raiseMinion, revive, healMinions and the Tome of Healing to the loaded hero once, then switch itself off.");
            _grantRequested = _grantEntry.Value;
            _logPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "NRFWNecromancy.log");
            var harmony = new HarmonyLib.Harmony("NRFW.Necromancy");
            try
            {
                Patch(harmony, typeof(SystemSetup), "CreateSystems", new[] { typeof(RuntimeConfig), typeof(SimulationConfig) },
                    postfix: nameof(AfterCreateSystems), returns: typeof(Il2CppReferenceArray<SystemBase>));
                Patch(harmony, typeof(EndOfFrameSystem), "Update", new[] { typeof(Frame) }, postfix: nameof(AfterEndOfFrame));
                Patch(harmony, typeof(CorpseMarkerSystem), "Update", new[] { typeof(Frame) }, postfix: nameof(AfterCorpseUpdate));
                Patch(harmony, typeof(RaisedMinionSystem), "Update", new[] { typeof(Frame) }, postfix: nameof(AfterRaisedUpdate));
                Patch(harmony, typeof(RaiseMinionActionData), "Execute", new[] { typeof(Frame), typeof(EntityRef) },
                    postfix: nameof(AfterRaiseExecute), returns: typeof(bool));
                Patch(harmony, typeof(RaiseMinionActionData), "CanRaiseNearby", new[] { typeof(Frame), typeof(EntityRef) },
                    postfix: nameof(AfterCanRaise), returns: typeof(bool));
                Patch(harmony, typeof(HealMinionsActionData), "Execute", new[] { typeof(Frame), typeof(EntityRef) },
                    postfix: nameof(AfterHealExecute), returns: typeof(bool));
                _enabled = true;
                Write("installed " + NecromancyPolicy.Version + "; grant_runes_once=" + _grantRequested);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                _enabled = false;
                Write("DISABLED install failed: " + e.GetType().Name + ": " + e.Message);
            }
            Flush();
        }

        public override void OnUpdate()
        {
            if (_enabled) CaptureHero();
            if (_grantDone && _grantEntry != null && _grantEntry.Value)
            {
                _grantEntry.Value = false;
                MelonPreferences.Save();
                Write("grant_runes_once switched off and saved");
            }
            Flush();
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string name, Type[] args, string postfix, Type returns = null)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            if (method == null || method.ReturnType != (returns ?? typeof(void))) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(NecromancyMod).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static)));
        }

        // ---- simulation tree -------------------------------------------------------------

        private static void AfterCreateSystems(ref Il2CppReferenceArray<SystemBase> __result)
        {
            if (!_enabled || __result == null) return;
            try
            {
                Found.Clear(); Added.Clear(); _enableChecks = 0; _heroFrames = 0;
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
                    Found[missing[i]] = created; Added.Add(created);
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
            }
            catch (Exception e) { Error("end-of-frame", e); }
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

        private static void TryGrant(Frame f)
        {
            var hero = new EntityRef { Raw = unchecked((ulong)Interlocked.Read(ref _heroRaw)) };
            if (!hero.IsValid || !InventoryAPI.GetInventoryEntity(f, hero).IsValid) { _heroFrames = 0; return; }
            if (++_heroFrames < NecromancyPolicy.GrantSettleFrames) return;   // let the saved inventory finish loading
            for (int i = 0; i < NecromancyPolicy.Items.Length; ++i)
            {
                var rune = new AssetRefHeroItemData { Id = new AssetGuid { Value = NecromancyPolicy.Items[i] } };
                int owned = InventoryAPI.GetItemMatchingCount(f, hero, rune, true);
                if (!NecromancyPolicy.ShouldGrant(true, f.IsVerified, true, owned))
                {
                    Write("grant skip " + NecromancyPolicy.ItemKeys[i] + ": already owned=" + owned);
                    continue;
                }
                InventoryAPI.PreviewAwardItemImmediate(f, hero, rune, 1, NewInventoryItemSource.Loot);
                Write("GRANTED " + NecromancyPolicy.ItemKeys[i] + " (" + NecromancyPolicy.Items[i] + ") to hero=" + hero.Raw +
                      "; count now=" + InventoryAPI.GetItemMatchingCount(f, hero, rune, true));
            }
            _grantDone = true;
        }

        // ---- diagnostics -------------------------------------------------------------------

        private static void AfterCorpseUpdate(Frame __0)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _corpseTicks) == 1) Write("CorpseMarkerSystem running");
        }

        private static void AfterRaisedUpdate(Frame __0)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _raisedTicks) == 1) Write("RaisedMinionSystem running");
        }

        private static void AfterRaiseExecute(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _executes) <= 20)
                Write("raise Execute caster=" + __1.Raw + " -> " + __result);
        }

        private static void AfterCanRaise(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _canRaise) <= 10)
                Write("raise CanRaiseNearby caster=" + __1.Raw + " -> " + __result);
        }

        private static void AfterHealExecute(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _heals) <= 20)
                Write("healMinions Execute caster=" + __1.Raw + " -> " + __result);
        }

        // ---- main thread helpers -----------------------------------------------------------

        private static void CaptureHero()
        {
            try
            {
                var view = ViewFrame.Active;
                ulong raw = 0;
                if (view != null && !view.IsDisposed)
                {
                    var players = view.Players.Item2;
                    if (players != null && players.TryGetBoundHeroEntity(out EntityRef hero) && hero.IsValid) raw = hero.Raw;
                }
                long previous = Interlocked.Exchange(ref _heroRaw, unchecked((long)raw));
                if (unchecked((ulong)previous) != raw && raw != 0) Write("hero bound=" + raw);
            }
            catch (Exception e) { Error("hero", e); }
        }

        private static void Error(string stage, Exception e)
        {
            if (Interlocked.Increment(ref _errors) <= 5) Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (_errors >= 5 && _enabled) { _enabled = false; Write("disabled after repeated errors; restart required"); }
        }

        private static void Write(string text)
        {
            if (Pending.Count < 512) Pending.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
        }

        private static void Flush()
        {
            if (Pending.IsEmpty) return;
            var lines = new List<string>();
            while (lines.Count < 64 && Pending.TryDequeue(out var line)) { lines.Add(line); MelonLogger.Msg("[necromancy] " + line); }
            try { File.AppendAllLines(_logPath, lines); } catch { }
        }
    }
}
