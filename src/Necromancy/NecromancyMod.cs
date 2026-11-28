using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using MelonLoader;

[assembly: MelonInfo(typeof(Necromancy.NecromancyMod), "NRFW Necromancy", Necromancy.NecromancyPolicy.Version, "NRFW research")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
[assembly: MelonAdditionalDependencies("0Harmony")]

namespace Necromancy
{
    // Restores the shipped-but-unreleased raise-minion rune family.
    //  1. Verifies Steam build 22928553 (hash-free) before installing any Harmony or native hook.
    //  2. Ensures CorpseMarkerSystem and RaisedMinionSystem exist in the simulation tree and are enabled.
    //  3. Optionally awards raiseMinion / revive / healMinions and the Tome of Healing once to the local hero.
    //  4. Supports T-014 gesture reactions and T-015 custom Boss summon runes.
    // Single-player only: every peer of a session would need identical systems.
    public sealed partial class NecromancyMod : MelonMod
    {
        private static long _heroRaw;
        private static int _errors;
        private static volatile bool _enabled;

        public override void OnInitializeMelon()
        {
            var category = MelonPreferences.CreateCategory("NRFWNecromancy", "NRFW Necromancy");
            _targetEntry = category.CreateEntry("grant_character", NecromancyPolicy.DefaultTargetCharacter,
                description: "Character (case-insensitive) that gets a Gold Tome of Healing with raiseMinion/revive/healMinions written into its slots, plus the runes loose. Once per character.");
            _grantedEntry = category.CreateEntry("granted_characters", "",
                description: "Characters already given the necromancy tome (comma-separated). Remove a name to grant it again.");
            _targetCharacter = (_targetEntry.Value ?? "").Trim();
            _grantRequested = NecromancyPolicy.ShouldGrantCharacter(_targetCharacter, _grantedEntry.Value);

            string userDataDir = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
            InitLog(userDataDir);

            try
            {
                string modsDir = Path.GetDirectoryName(typeof(NecromancyMod).Assembly.Location);
                NecromancyBuildGuard.Verify(modsDir, userDataDir, Write);
            }
            catch (Exception e)
            {
                _enabled = false;
                Write("DISABLED build guard failed: " + e.GetType().Name + ": " + e.Message);
                Flush(force: true);
                return;
            }

            var harmony = new HarmonyLib.Harmony("NRFW.Necromancy");
            try
            {
                Patch(harmony, typeof(SystemSetup), "CreateSystems", new[] { typeof(RuntimeConfig), typeof(SimulationConfig) },
                    postfix: nameof(AfterCreateSystems), returns: typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SystemBase>));
                Patch(harmony, typeof(EndOfFrameSystem), "Update", new[] { typeof(Frame) }, postfix: nameof(AfterEndOfFrame));
                Patch(harmony, typeof(CorpseMarkerSystem), "Update", new[] { typeof(Frame) }, postfix: nameof(AfterCorpseUpdate));
                Patch(harmony, typeof(RaiseMinionActionData), "Execute", new[] { typeof(Frame), typeof(EntityRef) },
                    postfix: nameof(AfterRaiseExecute), returns: typeof(bool));
                Patch(harmony, typeof(RaiseMinionActionData), "CanRaiseNearby", new[] { typeof(Frame), typeof(EntityRef) },
                    postfix: nameof(AfterCanRaise), returns: typeof(bool));
                Patch(harmony, typeof(HealMinionsActionData), "Execute", new[] { typeof(Frame), typeof(EntityRef) },
                    postfix: nameof(AfterHealExecute), returns: typeof(bool));
                _enabled = true;
                Write("installed " + NecromancyPolicy.Version + "; grant requested=" + _grantRequested + "; character=" + _targetCharacter);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                _enabled = false;
                Write("DISABLED install failed: " + e.GetType().Name + ": " + e.Message);
            }

            if (_enabled) InstallTomeRunes();
            if (_enabled) InstallMinionVisuals();
            if (_enabled) InstallDamageDiagnostics();
            if (_enabled) InstallMinionFriendlyFire();
            if (_enabled) InstallRaisedMinionDamageProtection();
            if (_enabled) InstallMinionLifecycle(category);
            if (_enabled) InstallRoarCommand(category);
            if (_enabled) InstallBossRunes(category);
            Flush(force: true);
        }

        public override void OnUpdate()
        {
            if (_enabled) CaptureHero();
            if (_enabled) PumpReactionsReload();
            if (_enabled && _bossEnabled) PumpBossViews();
            SaveBossGrants();
            if (_enabled && _visualsOn && (++_visualTick % 20) == 0) EnsureMinionVisuals();
            if (_grantDone && _grantedEntry != null && _grantRequested)
            {
                _grantRequested = false;
                _grantedEntry.Value = NecromancyPolicy.AddCharacter(_grantedEntry.Value, _grantedName);
                MelonPreferences.Save();
                Write("granted_characters = \"" + _grantedEntry.Value + "\" saved");
            }
            Flush();
        }

        public override void OnApplicationQuit()
        {
            _damageProtectionActive = false;
            _enabled = false;
            CloseLog();
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string name, Type[] args, string postfix, Type returns = null)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            if (method == null || method.ReturnType != (returns ?? typeof(void))) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(NecromancyMod).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void PatchPrefix(HarmonyLib.Harmony harmony, Type type, string name, Type[] args, string prefix, Type returns)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            if (method == null || method.ReturnType != returns) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(NecromancyMod).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void PatchPrefixPostfix(HarmonyLib.Harmony harmony, Type type, string name, Type[] args, string prefix, string postfix, Type returns = null)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            if (method == null || method.ReturnType != (returns ?? typeof(void))) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(NecromancyMod).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)),
                postfix: new HarmonyMethod(typeof(NecromancyMod).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static)));
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

        private static T Asset<T>(Frame f, long guid) where T : AssetObject =>
            f.Context.ResourceManager.GetAsset(new AssetGuid { Value = guid })?.TryCast<T>();

        private static void Error(string stage, Exception e)
        {
            if (Interlocked.Increment(ref _errors) <= 5) Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (_errors >= 5 && _enabled) { _enabled = false; Write("disabled after repeated errors; restart required"); }
        }

        private static void FirstHit(ref int flag, string name)
        {
            if (Interlocked.Exchange(ref flag, 1) == 0) Write("hook FIRST HIT " + name);
        }
    }
}
