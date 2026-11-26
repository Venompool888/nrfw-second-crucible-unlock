using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        private static MelonPreferences_Entry<string> _grantedEntry;
        private static MelonPreferences_Entry<string> _targetEntry;
        private static string _targetCharacter = "";
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
            _targetEntry = category.CreateEntry("grant_character", NecromancyPolicy.DefaultTargetCharacter,
                description: "Character (case-insensitive) that gets a Gold Tome of Healing with raiseMinion/revive/healMinions written into its slots, plus the runes loose. Once per character.");
            _grantedEntry = category.CreateEntry("granted_characters", "",
                description: "Characters already given the necromancy tome (comma-separated). Remove a name to grant it again.");
            _targetCharacter = (_targetEntry.Value ?? "").Trim();
            _grantRequested = NecromancyPolicy.ShouldGrantCharacter(_targetCharacter, _grantedEntry.Value);
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
            if (_enabled) InstallMinionFriendlyFire();
            if (_enabled) InstallMinionLifecycle(category);
            Flush();
        }

        // Tome of Healing accepts the three necromancy runes. Each overload is patched on its own
        // so a failure here leaves the rest of the plugin running.
        private static void InstallTomeRunes()
        {
            var tomeHarmony = new HarmonyLib.Harmony("NRFW.Necromancy.TomeRunes");
            int ok = 0;
            foreach (var (args, postfix) in new[]
            {
                (new[] { typeof(Frame), typeof(EntityRef), typeof(RuneData) }, nameof(AfterCompatibleByEntity)),
                (new[] { typeof(Frame), typeof(WeaponComponent).MakePointerType(), typeof(HeroRuneData) }, nameof(AfterCompatibleByComponent)),
            })
            {
                try { Patch(tomeHarmony, typeof(RunesAPI.Weapon), "IsRuneCompatible", args, postfix, typeof(bool)); ++ok; }
                catch (Exception e) { Write("tome-runes hook " + postfix + " FAILED " + e.GetType().Name + ": " + e.Message); }
            }
            Write("tome-runes hooks installed=" + ok + "/2");
        }

        public override void OnUpdate()
        {
            if (_enabled) CaptureHero();
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
                TickLifetime(__0);
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
            string name = HeroName(f, hero);
            if (!NecromancyPolicy.IsTargetCharacter(_targetCharacter, name))
            {
                if (hero.Raw != _lastRejectedHero) { _lastRejectedHero = hero.Raw; Write("grant waiting: loaded character '" + name + "' is not '" + _targetCharacter + "'"); }
                return;
            }
            _grantedName = name;
            _grantDone = true;   // one attempt per character whatever happens below
            Write("grant target character '" + name + "' hero=" + hero.Raw);
            try { LogRuneTable(f); } catch (Exception e) { Error("rune-table", e); }
            try { ForceTomeRunes(f, hero); } catch (Exception e) { Error("force-tome", e); }
            for (int i = 0; i < NecromancyPolicy.Items.Length; ++i)
            {
                try
                {
                    var rune = new AssetRefHeroItemData { Id = new AssetGuid { Value = NecromancyPolicy.Items[i] } };
                    int before = InventoryAPI.GetItemMatchingCount(f, hero, rune, true);
                    if (before > 0) { Write("loose " + NecromancyPolicy.ItemKeys[i] + " already in bag=" + before); continue; }
                    InventoryAPI.PreviewAwardItemImmediate(f, hero, rune, 1, NewInventoryItemSource.Loot);
                    Write("GRANTED loose " + NecromancyPolicy.ItemKeys[i] + "; count " + before + " -> " + InventoryAPI.GetItemMatchingCount(f, hero, rune, true));
                }
                catch (Exception e) { Error("loose-" + NecromancyPolicy.ItemKeys[i], e); }
            }
        }

        private static ulong _lastRejectedHero;
        private static volatile string _grantedName = "";

        private static string HeroName(Frame f, EntityRef hero)
        {
            try
            {
                if (RuntimeCharacterDataAPI.TryGetCharacterRef(f, hero, out RuntimeCharacterReference character))
                    return SavesAPIReferenceExtensions.GetName(character, f) ?? "";
            }
            catch (Exception e) { Error("hero-name", e); }
            return "";
        }

        private static T Asset<T>(Frame f, long guid) where T : AssetObject =>
            f.Context.ResourceManager.GetAsset(new AssetGuid { Value = guid })?.TryCast<T>();

        private static string RuneRefText(Frame f, long itemGuid)
        {
            RuneDataRef r = RunesAPI.GetRune(f, new AssetRefHeroItemData { Id = new AssetGuid { Value = itemGuid } });
            string data = "";
            if (!r.IsDefault) { var d = r.Get(f); data = d == null ? " data=null" : " data=" + d.Guid.Value + " utility=" + d.IsUtility; }
            return "index=" + r.m_index + " default=" + r.IsDefault + data;
        }

        // Which runes the runtime rune table knows, compared with the released Heal rune.
        private static void LogRuneTable(Frame f)
        {
            Write("rune-ref heal(released) " + RuneRefText(f, NecromancyPolicy.ReleasedHealRune));
            for (int i = 0; i < NecromancyPolicy.RunedTome.Length; ++i)
                Write("rune-ref " + NecromancyPolicy.RuneKeys[i] + " " + RuneRefText(f, NecromancyPolicy.RunedTome[i]));
            var all = f.ItemStatsSystem?.GetOrInitializeAllRunes(f);
            if (all == null) { Write("all-runes list unavailable"); return; }
            var found = new List<string>();
            for (int i = 0; i < all.Count; ++i)
            {
                var r = all[i];
                if (r == null) continue;
                long g = r.Guid.Value;
                if (NecromancyPolicy.IsNecromancyRune(g) || g == NecromancyPolicy.ReleasedHealRune) found.Add(g.ToString());
            }
            Write("all-runes count=" + all.Count + "; contains necro/heal: [" + string.Join(", ", found) + "]");
        }

        // Writes the three runes straight into the tome's WeaponComponent rune slots. The native layout
        // is checked against the interop struct first; any mismatch aborts before a single byte is written.
        private static unsafe void ForceTomeRunes(Frame f, EntityRef hero)
        {
            var tomeData = Asset<HeroItemData>(f, NecromancyPolicy.TomeOfHealing);
            EntityRef tome = tomeData == null ? default : InventoryAPI.GetFirstItemOfType(f, hero, tomeData);
            if (!tome.IsValid && tomeData != null)
            {
                tome = ItemsAPI.Create(f, tomeData, 1, ItemRarity.Gold, NewInventoryItemSource.Loot);
                bool added = tome.IsValid && InventoryAPI.DirectlyAddItemToInventoryOrDropAsBag(f, hero, tome, NewInventoryItemSource.Loot, false);
                Write("force created gold Tome of Healing entity=" + tome.Raw + " added=" + added);
            }
            if (!tome.IsValid) { Write("force SKIP no Tome of Healing"); return; }
            if (!EntityRefExtensions.Has<WeaponComponent>(tome, f)) { Write("force SKIP tome has no WeaponComponent"); return; }
            var wc = EntityRefExtensions.Get<WeaponComponent>(tome, f);
            if (wc == null) { Write("force SKIP WeaponComponent pointer null"); return; }

            int slotSize = HeroRuneSlot.SIZE, wcSize = WeaponComponent.SIZE, count = Constants.MaxRuneSlots, unique = Constants.UniqueRuneSlotIndex;
            int offSlots = (int)Marshal.OffsetOf<WeaponComponent>("_RuneSlots_");
            int offStatic = (int)Marshal.OffsetOf<HeroRuneSlot>(nameof(HeroRuneSlot.IsStaticSlot));
            int offUnlocked = (int)Marshal.OffsetOf<HeroRuneSlot>(nameof(HeroRuneSlot.Unlocked));
            int offRune = (int)Marshal.OffsetOf<HeroRuneSlot>(nameof(HeroRuneSlot.Rune));
            string layout = "layout slotSize=" + slotSize + " managed=" + Marshal.SizeOf<HeroRuneSlot>() + " wcSize=" + wcSize +
                            " managedWc=" + Marshal.SizeOf<WeaponComponent>() + " offSlots=" + offSlots + " static/unlocked/rune=" +
                            offStatic + "/" + offUnlocked + "/" + offRune + " count=" + count + " unique=" + unique + " class=" + (int)wc->Class;
            Write("force " + layout);
            if (!NecromancyPolicy.SlotLayoutOk(slotSize, Marshal.SizeOf<HeroRuneSlot>(), wcSize, Marshal.SizeOf<WeaponComponent>(), offSlots, count,
                    offStatic, offUnlocked, offRune))
            { Write("force ABORT layout mismatch; nothing written"); return; }

            byte* slots = (byte*)wc + offSlots;
            int Read(int s, int off) => *(int*)(slots + s * slotSize + off);
            string State()
            {
                var parts = new List<string>();
                for (int s = 0; s < count; ++s) parts.Add(s + "{st=" + Read(s, offStatic) + ",un=" + Read(s, offUnlocked) + ",rune=" + Read(s, offRune) + "}");
                return string.Join(" ", parts);
            }
            Write("force before " + State());
            for (int s = 0; s < count; ++s)
                if ((uint)Read(s, offStatic) > 1 || (uint)Read(s, offUnlocked) > 1) { Write("force ABORT slot " + s + " bool fields not 0/1; nothing written"); return; }

            int empty = new RuneDataRef().m_index;
            var written = new List<string>();
            for (int r = 0; r < NecromancyPolicy.RunedTome.Length; ++r)
            {
                int index = RunesAPI.GetRune(f, new AssetRefHeroItemData { Id = new AssetGuid { Value = NecromancyPolicy.RunedTome[r] } }).m_index;
                bool present = false;
                for (int s = 0; s < count; ++s) if (Read(s, offRune) == index) present = true;
                if (present) { written.Add(NecromancyPolicy.RuneKeys[r] + "=already"); continue; }
                int target = -1;
                for (int s = 0; s < count && target < 0; ++s)
                    if (s != unique && Read(s, offStatic) == 0 && Read(s, offRune) == empty) target = s;
                if (target < 0) { written.Add(NecromancyPolicy.RuneKeys[r] + "=no-free-slot"); continue; }
                *(int*)(slots + target * slotSize + offUnlocked) = 1;
                *(int*)(slots + target * slotSize + offRune) = index;
                written.Add(NecromancyPolicy.RuneKeys[r] + "=slot" + target + "(index " + index + ", HasRune=" + RunesAPI.Weapon.HasRune(f, tome, target) + ")");
            }
            Write("FORCED tome entity=" + tome.Raw + ": " + string.Join(", ", written));
            Write("force after " + State());
            try
            {
                RunesAPI.Weapon.ApplyRuneModification(f, RuneModificationType.ArmamentReload, tome, 0, default, hero);
                Write("force ArmamentReload requested");
            }
            catch (Exception e) { Write("force ArmamentReload failed " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- raised minion tint ------------------------------------------------------------
        // The shipped RaisedMinionVisualEffect (tint/overlay from RaisedMinionComponent) is never put on
        // enemy prefabs because the feature was not released. Track minions from the simulation and
        // attach the effect to their EntityView on the main thread.

        private static readonly ConcurrentDictionary<ulong, long> Minions = new();
        private static readonly Dictionary<ulong, int> Attached = new();
        private static bool _visualsOn;
        private static int _visualTick, _visualLogs, _effectLogs;

        private static void InstallMinionVisuals()
        {
            var visualHarmony = new HarmonyLib.Harmony("NRFW.Necromancy.MinionVisuals");
            try
            {
                Patch(visualHarmony, typeof(RaisedMinionSystem), "GetMinionFormationSlot", new[] { typeof(Frame), typeof(EntityRef), typeof(EntityRef) },
                    nameof(AfterFormationSlot), typeof(int));
                Patch(visualHarmony, typeof(Il2CppMoon.Forsaken.RaisedMinionVisualEffect), "ApplyTintEffect", Type.EmptyTypes, nameof(AfterApplyTint));
                _visualsOn = true;
                Write("minion-visuals hooks installed");
            }
            catch (Exception e) { visualHarmony.UnpatchSelf(); Write("minion-visuals hook FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        private static void AfterFormationSlot(Frame __0, EntityRef __2)
        {
            if (__0 == null || !__2.IsValid) return;
            Minions[__2.Raw] = Environment.TickCount64;
            if (__0.IsVerified && !MinionBirth.ContainsKey(__2.Raw) && !Expired.Contains(__2.Raw))
                MinionBirth[__2.Raw] = __0.TimeSinceStart.RawValue;
        }

        private static void AfterApplyTint(Il2CppMoon.Forsaken.RaisedMinionVisualEffect __instance)
        {
            if (__instance == null || Interlocked.Increment(ref _effectLogs) > 20) return;
            try
            {
                var r = __instance.m_renderers;
                Write("tint ApplyTintEffect applied=" + __instance.m_effectApplied + " color=" + __instance.m_tintColor +
                      " renderers=" + (r == null ? -1 : r.Length) + " action=" + __instance.m_raiseActionGuid.Value);
            }
            catch (Exception e) { Error("tint-log", e); }
        }

        private static void EnsureMinionVisuals()
        {
            try
            {
                long now = Environment.TickCount64;
                foreach (var pair in Minions) if (now - pair.Value > 5000) { Minions.TryRemove(pair.Key, out _); Attached.Remove(pair.Key); }
                if (Minions.IsEmpty) return;
                var effectType = Il2CppInterop.Runtime.Il2CppType.Of<Il2CppMoon.Forsaken.RaisedMinionVisualEffect>();
                var views = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Il2CppMoon.Forsaken.EntityView>());
                if (views == null) return;
                foreach (var obj in views)
                {
                    var view = obj?.TryCast<Il2CppMoon.Forsaken.EntityView>();
                    if (view == null) continue;
                    ulong raw = view.EntityRef.Raw;
                    if (!Minions.ContainsKey(raw) || Attached.ContainsKey(raw)) continue;
                    var go = view.gameObject;
                    var existing = go.GetComponent(effectType);
                    if (existing == null)
                    {
                        go.AddComponent(effectType);
                        Attached[raw] = 1;
                        if (++_visualLogs <= 20) Write("tint ATTACHED RaisedMinionVisualEffect to minion entity=" + raw + " go=" + go.name);
                    }
                    else
                    {
                        Attached[raw] = 2;
                        if (++_visualLogs <= 20) Write("tint minion entity=" + raw + " already has RaisedMinionVisualEffect (enabled=" +
                                                       existing.TryCast<UnityEngine.Behaviour>()?.enabled + ")");
                    }
                }
            }
            catch (Exception e) { Error("minion-visuals", e); }
        }

        // ---- minion lifecycle: no enemy auto-regen, optional lifetime ----------------------
        // Raised minions are still NPCs, so HealthSystem.UpdateEnemyHealthRegen refilled them whenever no
        // player was in regen distance (e.g. right after a teleport). Skip it for minions. The shipped data
        // has no minion lifetime; minion_lifetime_seconds adds one (0 = unlimited).

        private static readonly Dictionary<ulong, long> MinionBirth = new();   // sim thread only; FP raw
        private static readonly HashSet<ulong> Expired = new();
        private static int _lifetimeSeconds, _regenLogs, _expireLogs;

        private static void InstallMinionLifecycle(MelonPreferences_Category category)
        {
            var entry = category.CreateEntry("minion_lifetime_seconds", NecromancyPolicy.DefaultMinionLifetimeSeconds,
                description: "Seconds a raised minion stays before it falls; 0 = until killed (original data has no limit).");
            _lifetimeSeconds = Math.Max(0, entry.Value);
            var h = new HarmonyLib.Harmony("NRFW.Necromancy.MinionLifecycle");
            try
            {
                PatchPrefix(h, typeof(HealthSystem), "UpdateEnemyHealthRegen",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(HealthComponent).MakePointerType() }, nameof(BeforeEnemyRegen), typeof(void));
                Write("minion regen hook installed");
            }
            catch (Exception e) { Write("minion regen hook FAILED " + e.GetType().Name + ": " + e.Message); }
            try
            {
                PatchPrefix(h, typeof(CorpseMarkerSystem), "OnNpcDied",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(Damage), typeof(DamageResponse), typeof(int) }, nameof(BeforeNpcDied), typeof(void));
                Write("expired-minion corpse hook installed");
            }
            catch (Exception e) { Write("expired-minion corpse hook FAILED " + e.GetType().Name + ": " + e.Message); }
            Write("minion_lifetime_seconds=" + _lifetimeSeconds);
        }

        private static void PatchPrefix(HarmonyLib.Harmony harmony, Type type, string name, Type[] args, string prefix, Type returns)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            if (method == null || method.ReturnType != returns) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(NecromancyMod).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static bool BeforeEnemyRegen(Frame __0, EntityRef __1)
        {
            try
            {
                if (!_enabled || __0 == null || !EntityRefExtensions.Has<RaisedMinionComponent>(__1, __0)) return true;
                if (__0.IsVerified && Interlocked.Increment(ref _regenLogs) <= 5) Write("regen SKIPPED for raised minion entity=" + __1.Raw);
                return false;
            }
            catch (Exception e) { Error("regen", e); return true; }
        }

        // A minion we expired leaves no raisable corpse.
        private static bool BeforeNpcDied(Frame __0, EntityRef __1)
        {
            if (!Expired.Contains(__1.Raw)) return true;
            if (__0 != null && __0.IsVerified) Write("expired minion " + __1.Raw + " leaves no corpse");
            return false;
        }

        // Called from the verified end-of-frame tick.
        private static void TickLifetime(Frame f)
        {
            if (_lifetimeSeconds <= 0) return;
            long now = f.TimeSinceStart.RawValue;
            List<ulong> due = null, gone = null;
            foreach (var pair in MinionBirth)
            {
                var minion = new EntityRef { Raw = pair.Key };
                if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, f)) { (gone ??= new()).Add(pair.Key); continue; }
                if (NecromancyPolicy.LifetimeExpired(pair.Value, now, _lifetimeSeconds)) (due ??= new()).Add(pair.Key);
            }
            if (gone != null) foreach (var k in gone) MinionBirth.Remove(k);
            if (due == null) return;
            foreach (var k in due)
            {
                MinionBirth.Remove(k);
                Expired.Add(k);
                var minion = new EntityRef { Raw = k };
                DamageAPI.Kill(f, minion, default, default, PoiseLevel.None, true, DamageFlags.None, DamageVisualType.None);
                if (Interlocked.Increment(ref _expireLogs) <= 20) Write("minion EXPIRED after " + _lifetimeSeconds + "s entity=" + k);
            }
        }

        // ---- minion friendly fire ----------------------------------------------------------
        // Raised minions and heroes (or two minions) count as allies for damage. Hero-vs-hero keeps the
        // native result, so co-op/PvP friendly fire between players is untouched.

        private static int _allyLogs;

        private static void InstallMinionFriendlyFire()
        {
            var ffHarmony = new HarmonyLib.Harmony("NRFW.Necromancy.MinionFriendlyFire");
            try
            {
                Patch(ffHarmony, typeof(TeamUtils), "AreAllies", new[] { typeof(Frame), typeof(EntityRef), typeof(EntityRef) },
                    nameof(AfterAreAllies), typeof(bool));
                Write("minion friendly-fire hook installed");
            }
            catch (Exception e) { ffHarmony.UnpatchSelf(); Write("minion friendly-fire hook FAILED " + e.GetType().Name + ": " + e.Message); }
            // 0.11.0 gated IgnoreDamageRequest(Frame, ref Damage) and GetFriendlyFireCoefficient (FP return); in game that broke
            // ALL player damage (no plugin log lines, so the detours themselves were at fault). Removed in 0.11.1; see T-006.
        }

        private static void AfterAreAllies(Frame __0, EntityRef __1, EntityRef __2, ref bool __result)
        {
            if (__result || !_enabled || __0 == null || !__1.IsValid || !__2.IsValid) return;
            try
            {
                bool aMinion = EntityRefExtensions.Has<RaisedMinionComponent>(__1, __0);
                bool bMinion = EntityRefExtensions.Has<RaisedMinionComponent>(__2, __0);
                if (!aMinion && !bMinion) return;   // fast path: no minion involved, native result stands
                bool aHero = EntityRefExtensions.Has<HeroComponent>(__1, __0);
                bool bHero = EntityRefExtensions.Has<HeroComponent>(__2, __0);
                if (!NecromancyPolicy.MinionPairIsAllied(aMinion, aHero, bMinion, bHero)) return;
                __result = true;
                if (__0.IsVerified && Interlocked.Increment(ref _allyLogs) <= 20)
                    Write("friendly-fire BLOCKED hit=" + __1.Raw + (aMinion ? "(minion)" : aHero ? "(hero)" : "") +
                          " source=" + __2.Raw + (bMinion ? "(minion)" : bHero ? "(hero)" : ""));
            }
            catch (Exception e) { Error("friendly-fire", e); }
        }

        // ---- tome rune compatibility ------------------------------------------------------

        private static int _compatLogs;

        // Entity overload: exact match on the weapon item's data GUID.
        private static unsafe void AfterCompatibleByEntity(Frame __0, EntityRef __1, RuneData __2, ref bool __result)
        {
            if (__result || !_enabled || __0 == null || __2 == null) return;
            try
            {
                long rune = __2.Guid.Value;
                if (!NecromancyPolicy.IsNecromancyRune(rune)) return;
                if (!EntityRefExtensions.Has<ItemComponent>(__1, __0)) return;
                var component = EntityRefExtensions.Get<ItemComponent>(__1, __0);
                if (component == null) return;
                long item = component->Data.Id.Value;
                if (!NecromancyPolicy.TomeAccepts(item, rune)) return;
                __result = true;
                if (Interlocked.Increment(ref _compatLogs) <= 10) Write("tome-runes ALLOW entity=" + __1.Raw + " rune=" + rune);
            }
            catch (Exception e) { Error("tome-runes/entity", e); }
        }

        // Component overload carries no item GUID; accept only class-less weapons (the tome is Class=None).
        private static unsafe void AfterCompatibleByComponent(Frame __0, WeaponComponent* __1, HeroRuneData __2, ref bool __result)
        {
            if (__result || !_enabled || __1 == null || __2 == null) return;
            try
            {
                long rune = __2.Guid.Value;
                if (!NecromancyPolicy.ClasslessAccepts((int)__1->Class, rune)) return;
                __result = true;
                if (Interlocked.Increment(ref _compatLogs) <= 10) Write("tome-runes ALLOW classless weapon rune=" + rune);
            }
            catch (Exception e) { Error("tome-runes/component", e); }
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
