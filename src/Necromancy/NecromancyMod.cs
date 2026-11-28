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
    public sealed partial class NecromancyMod : MelonMod
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
            _followDiagnosticsEnabled = category.CreateEntry("minion_follow_diagnostics", true,
                description: "Log minion follow state and movement when away from the owner.").Value;
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
            if (_enabled) InstallRaisedMinionDamageProtection();
            if (_enabled) InstallMinionLifecycle(category);
            if (_enabled) InstallRoarCommand(category);
            if (_enabled) InstallBossRunes(category);
            Flush();
        }

        public override void OnDeinitializeMelon() => StopRaisedMinionDamageProtection();

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
                _bossSession = null;
                BossStorages.Clear();
                _bossReadWithoutSession = 0;
                RaisedDamageOwners.Clear();
                MinionLastDamage.Clear();
                TeleportMinions.Clear();
                MinionFollowSamples.Clear();
                _followDiagnosticLines = 0;
                _followDiagnosticsFailed = false;
                HeldStuck.Clear();
                HeldHeight.Clear();
                _damageProtectionLogs = 0;
                LastReaction.Clear();
                ReactionCycle.Clear();
                ReactionActionMissing.Clear();
                LoadReactions(force: false);
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
                if (_bossEnabled) TickBossRunes(__0);
                TickLifetime(__0);
                if (_roarEnabled) try { TickRoarTraces(__0); } catch (Exception e) { RoarTraces.Clear(); Write("roar trace FAILED " + e.GetType().Name + ": " + e.Message); }
            }
            catch (Exception e) { Error("end-of-frame", e); return; }

            if (_rosterOff) return;
            try { LogRoster(__0); }
            catch (Exception e)
            {
                _rosterOff = true;
                if (Interlocked.Increment(ref _rosterErrors) <= 3)
                    Write("roster ERROR " + e.GetType().Name + ": " + e.Message);
            }
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
            var indices = new int[NecromancyPolicy.RunedTome.Length];
            for (int r = 0; r < indices.Length; ++r)
                indices[r] = RunesAPI.GetRune(f, new AssetRefHeroItemData { Id = new AssetGuid { Value = NecromancyPolicy.RunedTome[r] } }).m_index;
            WriteTomeRunes(f, hero, tome, NecromancyPolicy.RuneKeys, indices);
        }

        // Layout-checked direct write of rune table indices into free, non-static, non-unique tome slots,
        // then ArmamentReload. Returns how many runes ended up in the tome (written or already present).
        private static unsafe int WriteTomeRunes(Frame f, EntityRef hero, EntityRef tome, string[] keys, int[] indices)
        {
            if (!EntityRefExtensions.Has<WeaponComponent>(tome, f)) { Write("force SKIP tome has no WeaponComponent"); return 0; }
            var wc = EntityRefExtensions.Get<WeaponComponent>(tome, f);
            if (wc == null) { Write("force SKIP WeaponComponent pointer null"); return 0; }

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
            { Write("force ABORT layout mismatch; nothing written"); return 0; }

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
                if ((uint)Read(s, offStatic) > 1 || (uint)Read(s, offUnlocked) > 1) { Write("force ABORT slot " + s + " bool fields not 0/1; nothing written"); return 0; }

            int empty = new RuneDataRef().m_index;
            var written = new List<string>();
            int placed = 0;
            for (int r = 0; r < indices.Length; ++r)
            {
                int index = indices[r];
                if (index == empty) { written.Add(keys[r] + "=no-rune-index"); continue; }
                bool present = false;
                for (int s = 0; s < count; ++s) if (Read(s, offRune) == index) present = true;
                if (present) { written.Add(keys[r] + "=already"); ++placed; continue; }
                int target = -1;
                for (int s = 0; s < count && target < 0; ++s)
                    if (s != unique && Read(s, offStatic) == 0 && Read(s, offRune) == empty) target = s;
                if (target < 0) { written.Add(keys[r] + "=no-free-slot"); continue; }
                *(int*)(slots + target * slotSize + offUnlocked) = 1;
                *(int*)(slots + target * slotSize + offRune) = index;
                ++placed;
                written.Add(keys[r] + "=slot" + target + "(index " + index + ", HasRune=" + RunesAPI.Weapon.HasRune(f, tome, target) + ")");
            }
            Write("FORCED tome entity=" + tome.Raw + ": " + string.Join(", ", written));
            Write("force after " + State());
            try
            {
                RunesAPI.Weapon.ApplyRuneModification(f, RuneModificationType.ArmamentReload, tome, 0, default, hero);
                Write("force ArmamentReload requested");
            }
            catch (Exception e) { Write("force ArmamentReload failed " + e.GetType().Name + ": " + e.Message); }
            return placed;
        }

        // ---- raised minion tint ------------------------------------------------------------
        // The shipped RaisedMinionVisualEffect (tint/overlay from RaisedMinionComponent) is never put on
        // enemy prefabs because the feature was not released. Track minions from the simulation and
        // attach the effect to their EntityView on the main thread.

        private static readonly ConcurrentDictionary<ulong, long> Minions = new();
        private static readonly Dictionary<ulong, int> Attached = new();
        private static readonly Dictionary<ulong, UnityEngine.Component> AddedEffects = new();   // components this plugin added, by minion
        private static readonly Dictionary<IntPtr, int> TintAttempts = new(), TintSuccesses = new();
        private static int _tintGiveUps;
        private static IntPtr _tintInFlight;
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
                PatchPrefix(visualHarmony, typeof(Il2CppMoon.Forsaken.RaisedMinionVisualEffect), "ApplyTintEffect", Type.EmptyTypes, nameof(BeforeApplyTint), typeof(void));
                try
                {
                    Patch(visualHarmony, typeof(Il2CppMoon.Forsaken.CharacterViewEffects), "OnDamageResolved", new[] { typeof(EventDamageResolved) },
                        nameof(AfterCharacterDamageResolvedInstance));
                    Write("minion hit diagnostics installed");
                    Patch(visualHarmony, typeof(Il2CppMoon.Forsaken.HeroView), "OnDamageResolved", new[] { typeof(EventDamageResolved) },
                        nameof(AfterHeroDamageResolved));
                    Write("hero hit diagnostics installed");
                }
                catch (Exception e) { Write("minion hit diagnostics FAILED " + e.GetType().Name + ": " + e.Message); }
                _visualsOn = true;
                Write("minion-visuals hooks installed");
            }
            catch (Exception e) { visualHarmony.UnpatchSelf(); Write("minion-visuals hook FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        private static void AfterFormationSlot(Frame __0, EntityRef __1, EntityRef __2)
        {
            if (__0 == null || !__2.IsValid) return;
            Minions[__2.Raw] = Environment.TickCount64;
            if (__0.IsVerified) RememberRaisedDamageSource(__0, __2);
            if (__0.IsVerified && !MinionBirth.ContainsKey(__2.Raw) && !Expired.Contains(__2.Raw))
                MinionBirth[__2.Raw] = __0.TimeSinceStart.RawValue;
            if (__0.IsVerified && !TeamChecked.Contains(__2.Raw)) { TeamChecked.Add(__2.Raw); try { SetMinionTeam(__0, __1, __2); } catch (Exception e) { Error("minion-team", e); } }
        }

        // ---- minion team (T-006) ------------------------------------------------------------
        // Raised minions join the hero team (teamDataPlayers, HasFriendlyFire=true), so their hits on the
        // hero and the hero's on them go through friendly fire. teamDataPlayersNoFF is the shipped player
        // team without friendly fire. Only the minion's TeamComponent.Data is changed; damage code is untouched.

        private static readonly HashSet<ulong> TeamChecked = new();   // sim thread only
        private static string _teamMode = NecromancyPolicy.TeamModePlayersNoFF;

        private static unsafe long ReadTeam(Frame f, EntityRef e)
        {
            if (!e.IsValid || !EntityRefExtensions.Has<TeamComponent>(e, f)) return 0;
            var t = EntityRefExtensions.Get<TeamComponent>(e, f);
            return t == null ? 0 : t->Data.Id.Value;
        }

        private static unsafe void SetMinionTeam(Frame f, EntityRef owner, EntityRef minion)
        {
            long minionTeam = ReadTeam(f, minion), ownerTeam = ReadTeam(f, owner);
            long target = NecromancyPolicy.MinionTeamTarget(_teamMode, minionTeam);
            if (target == 0)
            {
                Write("minion-team keep entity=" + minion.Raw + " team=" + NecromancyPolicy.TeamName(minionTeam) + " owner=" + NecromancyPolicy.TeamName(ownerTeam) + " mode=" + _teamMode + " npc=" + MinionNpcText(f, minion));
                return;
            }
            var t = EntityRefExtensions.Get<TeamComponent>(minion, f);
            t->Data = new AssetRefTeamData { Id = new AssetGuid { Value = target } };
            Write("minion-team SET entity=" + minion.Raw + " " + NecromancyPolicy.TeamName(minionTeam) + " -> " + NecromancyPolicy.TeamName(ReadTeam(f, minion)) +
                  " (owner " + NecromancyPolicy.TeamName(ownerTeam) + ") npc=" + MinionNpcText(f, minion));
        }

        // ---- read-only damage diagnostics (T-006) ---------------------------------------------
        // CharacterViewEffects.OnDamageResolved receives the resolved event object; nothing here changes damage.

        private static int _hitLogs;

        private static void AfterCharacterDamageResolvedInstance(EventDamageResolved __0) => AfterCharacterDamageResolved(__0);

        // 0.12.0 playtest: the maintainer still lost health near minions but no hero/minion HIT was logged.
        // Log every hit on the local hero with all source proxies so the real attacker (minion, its projectile or
        // cascade, or an enemy) and the damage TargetGroup are visible. Read-only.
        private static int _heroHitLogs;

        private static void AfterHeroDamageResolved(EventDamageResolved __0)
        {
            if (__0 == null || !_enabled) return;
            try
            {
                ulong hero = unchecked((ulong)Interlocked.Read(ref _heroRaw));
                if (hero == 0 || __0.HitEntity.Raw != hero) return;
                if (Interlocked.Increment(ref _heroHitLogs) > 400) return;
                var damage = __0.Damage;
                string sources = DescribeProxies(__0, hero, out bool anyMinion);
                Write("HERO HIT taken=" + (__0.Response.AmountTaken.RawValue / 65536.0).ToString("0.##") + " type=" + __0.Response.Type +
                      " group=" + damage.TargetGroup + " flags=" + damage.Flags + " school=" + damage.School +
                      (anyMinion ? " FROM-MINION" : "") + " " + sources);
            }
            catch (Exception ex) { Error("hero-hit-log", ex); }
        }

        // 0.13.0 playtest: proxy.Main / TryGetProxyOfType returned 0..4 instead of entity refs. The interop struct
        // models PayloadProxyGroup's fixed proxy buffer as one element, so the managed copy is truncated. Read the
        // proxy slots straight from the native event object instead (layout resolved from IL2CPP at runtime).
        private static int _proxyLayout;   // 0 = unresolved, 1 = ok, -1 = unusable
        private static int _offDamage, _offProxy, _offSlots, _offSlotIndex, _slotCount;

        private static bool ResolveProxyLayout()
        {
            if (_proxyLayout != 0) return _proxyLayout > 0;
            _proxyLayout = -1;
            uint align = 0;
            IntPtr evt = Il2CppInterop.Runtime.Il2CppClassPointerStore<EventDamageResolved>.NativeClassPtr;
            IntPtr dmg = Il2CppInterop.Runtime.Il2CppClassPointerStore<Damage>.NativeClassPtr;
            IntPtr ppg = Il2CppInterop.Runtime.Il2CppClassPointerStore<PayloadProxyGroup>.NativeClassPtr;
            if (evt == IntPtr.Zero || dmg == IntPtr.Zero || ppg == IntPtr.Zero) { Write("proxy layout: class missing"); return false; }
            IntPtr fDamage = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(evt, "Damage");
            IntPtr fProxy = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(dmg, "Proxy");
            IntPtr fSlots = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(ppg, "_m_proxies_");
            IntPtr fIndex = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(ppg, "m_availableProxySlotIndex");
            if (fDamage == IntPtr.Zero || fProxy == IntPtr.Zero || fSlots == IntPtr.Zero || fIndex == IntPtr.Zero) { Write("proxy layout: field missing"); return false; }
            int header = 2 * IntPtr.Size;   // value-type field offsets include the object header
            _offDamage = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fDamage);
            _offProxy = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fProxy) - header;
            _offSlots = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fSlots) - header;
            _offSlotIndex = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fIndex) - header;
            int ppgSize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(ppg, ref align);
            int damageSize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(dmg, ref align);
            _slotCount = (ppgSize - _offSlots) / 8;
            bool ok = NecromancyPolicy.ProxyLayoutOk(_offDamage, _offProxy, ppgSize, damageSize, _offSlots, _offSlotIndex, _slotCount);
            Write("proxy layout " + (ok ? "OK" : "REJECTED") + ": event.Damage@" + _offDamage + " Damage.Proxy@" + _offProxy + " size " + damageSize +
                  "; proxies@" + _offSlots + " slotIndex@" + _offSlotIndex + " group size " + ppgSize + " slots=" + _slotCount);
            _proxyLayout = ok ? 1 : -1;
            return ok;
        }

        private static string DescribeProxies(EventDamageResolved e, ulong hero, out bool anyMinion)
        {
            anyMinion = false;
            if (!ResolveProxyLayout()) return "sources=?";
            IntPtr group = IntPtr.Add(e.Pointer, _offDamage + _offProxy);
            int used = Marshal.ReadInt32(group, _offSlotIndex);
            var parts = new List<string>();
            for (int i = 0; i < _slotCount; i++)
            {
                ulong raw = unchecked((ulong)Marshal.ReadInt64(group, _offSlots + i * 8));
                if (raw == 0) continue;
                bool m = Minions.ContainsKey(raw);
                anyMinion |= m;
                parts.Add(i + ":" + raw + (m ? "(MINION)" : raw == hero ? "(HERO)" : ""));
            }
            return "used=" + used + " sources=[" + string.Join(" ", parts) + "]";
        }

        private static void AfterCharacterDamageResolved(EventDamageResolved e)
        {
            if (e == null || !_enabled) return;
            try
            {
                ulong hit = e.HitEntity.Raw, hero = unchecked((ulong)Interlocked.Read(ref _heroRaw));
                bool hitMinion = Minions.ContainsKey(hit), hitHero = hit == hero;
                if (!hitMinion || hitHero) return;   // hero hits are logged by AfterHeroDamageResolved
                string sources = DescribeProxies(e, hero, out bool srcMinion);
                bool srcHero = sources.Contains("(HERO)");
                if (!(srcHero || srcMinion)) return;   // enemy vs minion is expected damage
                if (Interlocked.Increment(ref _hitLogs) > 40) return;
                Write("HIT minion=" + hit + " by " + (srcHero ? "hero" : "minion") + " taken=" + (e.Response.AmountTaken.RawValue / 65536.0).ToString("0.##") +
                      " type=" + e.Response.Type + " " + sources);
            }
            catch (Exception ex) { Error("hit-log", ex); }
        }

        // 0.12.0 playtest: on darakMutantView, CollectRenderers threw inside ApplyTintEffect every frame (~45/s,
        // 26k errors). The postfix only runs when the native call returns, so attempts without a success mean it is
        // throwing. After 3 failed attempts the component is disabled and never retried.
        private static bool BeforeApplyTint(Il2CppMoon.Forsaken.RaisedMinionVisualEffect __instance)
        {
            if (__instance == null) return false;
            IntPtr key = __instance.Pointer;
            TintAttempts.TryGetValue(key, out int attempts);
            TintSuccesses.TryGetValue(key, out int ok);
            if (!NecromancyPolicy.ShouldRetryTint(attempts, ok))
            {
                try
                {
                    __instance.enabled = false;
                    if (Interlocked.Increment(ref _tintGiveUps) <= 10)
                        Write("tint GAVE UP after " + attempts + " failed attempts on go=" + __instance.gameObject.name + "; component disabled");
                }
                catch { }
                return false;
            }
            TintAttempts[key] = attempts + 1;
            _tintInFlight = key;   // the postfix counts a success only for a call that really ran
            return true;
        }

        private static void RemoveAddedEffect(ulong minion)
        {
            if (!AddedEffects.TryGetValue(minion, out var component)) return;
            AddedEffects.Remove(minion);
            try
            {
                if (component != null) { TintAttempts.Remove(component.Pointer); TintSuccesses.Remove(component.Pointer); UnityEngine.Object.Destroy(component); }
                if (++_visualLogs <= 150) Write("tint REMOVED added effect from pooled view of gone minion " + minion);
            }
            catch (Exception e) { Error("tint-remove", e); }
        }

        private static void AfterApplyTint(Il2CppMoon.Forsaken.RaisedMinionVisualEffect __instance)
        {
            if (__instance == null || __instance.Pointer != _tintInFlight) return;   // skipped by the prefix: not a success
            _tintInFlight = IntPtr.Zero;
            TintSuccesses.TryGetValue(__instance.Pointer, out int ok);
            TintSuccesses[__instance.Pointer] = ok + 1;
            if (Interlocked.Increment(ref _effectLogs) > 20) return;
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
                foreach (var pair in Minions) if (now - pair.Value > 5000) { Minions.TryRemove(pair.Key, out _); Attached.Remove(pair.Key); RemoveAddedEffect(pair.Key); }
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
                        AddedEffects[raw] = go.AddComponent(effectType);
                        Attached[raw] = 1;
                        if (++_visualLogs <= 150) Write("tint ATTACHED RaisedMinionVisualEffect to minion entity=" + raw + " go=" + go.name);
                    }
                    else
                    {
                        Attached[raw] = 2;
                        if (++_visualLogs <= 150) Write("tint minion entity=" + raw + " already has RaisedMinionVisualEffect (enabled=" +
                                                       existing.TryCast<UnityEngine.Behaviour>()?.enabled + ")");
                    }
                }
            }
            catch (Exception e) { Error("minion-visuals", e); }
        }

        // ---- gesture reactions (T-014; T-012's Warrick roar is the default rule) --------------
        // The table in UserData/NRFWNecromancyReactions.json says which summon x which gesture -> which move. Execution is
        // T-012's playtested path: confirmed frames only, Owner read through the component pointer, NPC through
        // NpcAttachmentComponent, Has/Get<ActionComponent> (never TryGetPointer), move on ActionComponent.ActionLayer.

        private static readonly Dictionary<ulong, long> LastReaction = new();                 // sim thread only; FP raw
        private static readonly Dictionary<(ulong Minion, int Rule), int> ReactionCycle = new(); // sim thread only
        private static readonly HashSet<long> ReactionActionMissing = new();
        private static bool _roarEnabled, _profilesNoticeLogged;
        private static volatile ReactionTable _reactions;
        private static string _reactionsPath, _seedGesture, _seedAction;
        private static DateTime _reactionsStamp;
        private static int _gestureLogs;

        private static void InstallRoarCommand(MelonPreferences_Category category)
        {
            var h = new HarmonyLib.Harmony("NRFW.Necromancy.RoarAoe");
            try
            {
                _seedGesture = category.CreateEntry("roar_gesture", NecromancyPolicy.DefaultRoarGesture,
                    description: "Gesture GUID for the default Warrick rule; only used when NRFWNecromancyReactions.json does not exist yet.").Value;
                _seedAction = category.CreateEntry("roar_action", NecromancyPolicy.DefaultRoarAction,
                    description: "Action GUID for the default Warrick rule; only used when NRFWNecromancyReactions.json does not exist yet.").Value;
                if (!category.CreateEntry("roar_enabled", true,
                    description: "Gesture reactions (T-014): raised minions use moves from UserData/NRFWNecromancyReactions.json when you do a gesture.").Value) return;
                _reactionsPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, GestureReactions.FileName);
                LoadReactions(force: true);
                Patch(h, typeof(CommandProcessingSystem), "ExecutePerformGesturePlayerCommand",
                    new[] { typeof(Frame), typeof(int), typeof(PerformGesturePlayerCommand) }, nameof(AfterGestureCommand));
                _roarEnabled = true;
                Write("roar command hook installed (gesture reactions)");
            }
            catch (Exception e)
            {
                h.UnpatchSelf();
                Write("roar command hook FAILED " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Writes the default table when the file is missing, then (re)loads it when it changed. A file that cannot be
        // used as a whole falls back to the built-in default rule so T-012's behaviour stays; bad rules are skipped.
        private static void LoadReactions(bool force)
        {
            if (_reactionsPath == null) return;
            long gesture = GestureReactions.SeedGuid(_seedGesture, NecromancyPolicy.DefaultRoarGesture);
            long action = GestureReactions.SeedGuid(_seedAction, NecromancyPolicy.DefaultRoarAction);
            try
            {
                if (!File.Exists(_reactionsPath))
                {
                    if (NecromancyPolicy.RoarAssetGuid(_seedGesture) == null || NecromancyPolicy.RoarAssetGuid(_seedAction) == null)
                        Write("reactions: roar_gesture/roar_action keywords are not supported (runtime asset paths are empty); seeding with GUIDs");
                    File.WriteAllText(_reactionsPath, GestureReactions.DefaultJson(gesture, action));
                    Write("reactions: wrote default table " + GestureReactions.FileName + " (gesture " + gesture + " -> action " + action + ")");
                }
                var stamp = File.GetLastWriteTimeUtc(_reactionsPath);
                if (!force && stamp == _reactionsStamp && _reactions != null) return;
                string text = File.ReadAllText(_reactionsPath);
                // Record the stamp only after a successful read, so a file locked by an editor is retried next session.
                _reactionsStamp = stamp;
                var table = GestureReactions.Parse(text, out string fileError);
                if (table == null)
                {
                    _reactions = GestureReactions.Default(gesture, action);
                    Write("reactions file INVALID (" + fileError + "); using the built-in Warrick rule");
                    return;
                }
                foreach (string err in table.Errors) Write("reactions rule REJECTED " + err);
                _reactions = table;
                _profilesNoticeLogged = false;
                var parts = new List<string>();
                foreach (var r in table.Rules)
                    parts.Add(r.Label + " who=" + r.Who + " gesture=" + r.Gesture + " actions=" + string.Join("/", r.Actions) +
                              " pick=" + r.Pick + " cooldown=" + r.CooldownSeconds);
                Write("reactions loaded " + table.Rules.Length + " rule(s), rejected " + table.Errors.Length + ": " + string.Join("; ", parts));
            }
            catch (Exception e)
            {
                bool had = _reactions != null;
                _reactions ??= GestureReactions.Default(gesture, action);
                Write("reactions load FAILED " + e.GetType().Name + ": " + e.Message + (had ? "; keeping the current table" : "; using the built-in Warrick rule"));
            }
        }

        // T-013 (not merged) gives each minion a profile name; until then profile: rules match nothing.
        private static string MinionProfileName(Frame f, EntityRef minion) => null;

        private static unsafe void AfterGestureCommand(Frame __0, int __1, PerformGesturePlayerCommand __2)
        {
            // React only in the confirmed command stream, not during prediction/replay.
            if (!_enabled || !_roarEnabled || __0 == null || !__0.IsVerified) return;
            try
            {
                EntityRef hero = __0.GetPlayerHero(__1);
                if (!hero.IsValid || hero.Raw != unchecked((ulong)Interlocked.Read(ref _heroRaw))) return;
                var table = _reactions;
                long gestureGuid = __2.GestureData.Id.Value;
                string path = gestureGuid > 0 ? Asset<CollectableGestureData>(__0, gestureGuid)?.Path : null;
                bool match = table != null && table.HasGesture(gestureGuid);
                if (++_gestureLogs <= 60)
                    Write("gesture GUID=" + gestureGuid + " Path=" + (path ?? "<unresolved>") + " matched=" + (match ? "yes" : "no"));
                if (!match) return;
                if (table.UsesProfiles && !_profilesNoticeLogged)
                {
                    _profilesNoticeLogged = true;
                    Write("reactions: profile: rules match nothing until T-013 minion profiles are merged");
                }

                long now = __0.TimeSinceStart.RawValue;
                int owned = 0, commanded = 0;
                var seen = new List<string>();
                var resolved = new Dictionary<long, ActionData>();
                // Candidates: the component iterator plus the minions this plugin recorded from GetMinionFormationSlot,
                // in case the interop iterator yields nothing.
                var candidates = new List<EntityRef>();
                var keys = new HashSet<ulong>();
                int fromIterator = 0;
                foreach (var pair in __0.GetComponentIterator<RaisedMinionComponent>())
                    if (keys.Add(pair.Entity.Raw)) { candidates.Add(pair.Entity); ++fromIterator; }
                foreach (var raw in Minions.Keys)
                    if (keys.Add(raw)) candidates.Add(new EntityRef { Raw = raw });
                seen.Add("iterator=" + fromIterator + " recorded=" + Minions.Count);
                foreach (var minion in candidates)
                {
                    if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, __0)) { seen.Add("gone:" + minion.Raw); continue; }
                    // 0.15.4 playtest: the iterator's copied component never matched the hero (owned minions=0);
                    // read Owner through the component pointer like MinionDamage does.
                    var rc = EntityRefExtensions.Get<RaisedMinionComponent>(minion, __0);
                    ulong owner = rc == null ? 0 : rc->Owner.Raw;
                    if (owner != hero.Raw) { seen.Add("other-owner:" + owner); continue; }
                    ++owned;
                    var npcData = MinionNpcData(__0, minion);
                    long npc = npcData?.Guid.Value ?? 0;
                    string tier = npcData == null ? "" : npcData.EnemyType.ToString();
                    string profile = MinionProfileName(__0, minion);
                    string who = NecromancyPolicy.NpcName(npc) + "/" + (tier.Length > 0 ? tier : "?");
                    var rule = GestureReactions.Match(table, gestureGuid, npc, profile, tier);
                    if (rule == null) { seen.Add(who + "(no rule)"); continue; }
                    if (LastReaction.TryGetValue(minion.Raw, out long last) && !GestureReactions.CooldownElapsed(last, now, rule.CooldownSeconds))
                    { seen.Add(who + "(cooldown " + rule.Label + ")"); continue; }
                    // 0.15.10 playtest: Frame.TryGetPointer(out T*) needs UIntPtr..ctor(IntPtr), missing at runtime
                    // (MissingMethodException). Has + Get return the pointer directly, as elsewhere in this plugin.
                    if (!EntityRefExtensions.Has<ActionComponent>(minion, __0)) { seen.Add(who + "(no-actions)"); continue; }
                    var actions = EntityRefExtensions.Get<ActionComponent>(minion, __0);
                    if (actions == null) { seen.Add(who + "(null-actions)"); continue; }

                    var cycleKey = (minion.Raw, rule.Index);
                    int cycle = ReactionCycle.TryGetValue(cycleKey, out int c) ? c : 0;
                    long actionGuid = rule.Actions[GestureReactions.PickIndex(rule.Pick, rule.Actions.Length, minion.Raw, now, cycle)];
                    // Advance before resolving: an unresolvable GUID in a cycle list is skipped next time instead of
                    // being picked forever.
                    ReactionCycle[cycleKey] = cycle + 1;
                    if (!resolved.TryGetValue(actionGuid, out var action)) resolved[actionGuid] = action = ResolveReactionAction(__0, actionGuid);
                    if (action == null) { seen.Add(who + "(action " + actionGuid + " unresolved)"); continue; }
                    ++commanded;
                    LastReaction[minion.Raw] = now;
                    try
                    {
                        // 0.15.13 playtest: Execute on layer 0 (ActionLayer.Base, locomotion) returned an id but Warrick never
                        // moved. Attacks run on ActionLayer.Action (1).
                        int layer = ActionComponent.ActionLayer;
                        int result = actions->Execute(__0, action, null, false, default, layer, false, true, default);
                        bool running = actions->IsExecutingAction(__0, action);
                        Write("reaction minion entity=" + minion.Raw + " npc=" + who + " rule=" + rule.Label + " action GUID=" + action.Guid.Value +
                              " layer=" + layer + " -> " + result + " executingNow=" + running);
                        RoarTraces.Add((minion.Raw, action.Guid.Value, now, 0));
                    }
                    catch (Exception e) { Write("reaction Execute FAILED entity=" + minion.Raw + " " + e.GetType().Name + ": " + e.Message); }
                }
                if (commanded == 0)
                    Write("reaction gesture matched but no minion commanded: owned minions=" + owned + " [" + string.Join(", ", seen) + "]");
            }
            catch (Exception e)
            {
                _roarEnabled = false;
                Write("roar command DISABLED " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Read-only follow-up: is the commanded move still executing 0.5 s and 1.5 s later? Verified end-of-frame only.
        private static readonly List<(ulong Minion, long Action, long Start, int Stage)> RoarTraces = new();

        private static unsafe void TickRoarTraces(Frame f)
        {
            if (RoarTraces.Count == 0) return;
            long now = f.TimeSinceStart.RawValue;
            for (int i = RoarTraces.Count - 1; i >= 0; --i)
            {
                var tr = RoarTraces[i];
                long due = tr.Stage == 0 ? (1L << 15) : (3L << 15);   // 0.5 s, then 1.5 s
                if (now - tr.Start < due) continue;
                var minion = new EntityRef { Raw = tr.Minion };
                string state = "gone";
                try
                {
                    if (EntityRefExtensions.Has<ActionComponent>(minion, f))
                    {
                        var ac = EntityRefExtensions.Get<ActionComponent>(minion, f);
                        var data = Asset<ActionData>(f, tr.Action);
                        state = ac == null || data == null ? "unreadable" :
                            "executing=" + ac->IsExecutingAction(f, data) + " actionLayerBusy=" + ac->IsExecuting(f, ActionComponent.ActionLayer) +
                            " baseLayerBusy=" + ac->IsExecuting(f, ActionComponent.BaseLayer);
                    }
                }
                catch (Exception e) { state = "?" + e.GetType().Name; }
                Write("reaction trace +" + (tr.Stage == 0 ? "0.5" : "1.5") + "s entity=" + tr.Minion + " action=" + tr.Action + " " + state);
                if (tr.Stage == 0) RoarTraces[i] = (tr.Minion, tr.Action, tr.Start, 1); else RoarTraces.RemoveAt(i);
            }
        }

        // Rules carry GUIDs only (runtime asset paths are empty in the release build). A GUID that is not an ActionData
        // is reported once per session with its actual type.
        private static ActionData ResolveReactionAction(Frame f, long guid)
        {
            var action = guid > 0 ? Asset<ActionData>(f, guid) : null;
            if (action == null && ReactionActionMissing.Add(guid))
            {
                string type = "missing";
                try
                {
                    var raw = guid > 0 ? f.Context.ResourceManager.GetAsset(new AssetGuid { Value = guid }) : null;
                    if (raw != null) type = raw.GetIl2CppType().FullName;
                }
                catch (Exception e) { type = "?" + e.GetType().Name; }
                Write("reaction action not resolved: " + guid + " (asset type " + type + ", needs ActionData)");
            }
            return action;
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
            var teamEntry = category.CreateEntry("minion_team", NecromancyPolicy.TeamModePlayersNoFF,
                description: "players_noff = move raised minions from the player team to the shipped no-friendly-fire player team; native = leave the team unchanged.");
            _teamMode = (teamEntry.Value ?? "").Trim().ToLowerInvariant();
            Write("minion_team=" + _teamMode);
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
            try
            {
                PatchPrefix(h, typeof(AttachmentAPI), "ResolveNavDataAttachment", new[] { typeof(Frame), typeof(EntityRef) },
                    nameof(BeforeResolveNavData), typeof(RuntimeNavigationData));
                Write("nav-attachment probe installed");
            }
            catch (Exception e) { Write("nav-attachment probe FAILED " + e.GetType().Name + ": " + e.Message); }
            var stuckEntry = category.CreateEntry("minion_stuck_teleport_in_combat", false,
                description: "true = vanilla stuck teleports; false = hold near the owner and while off-follow within teleport range, subject to the hold cap outside the near radius.");
            _stuckTeleportInCombat = stuckEntry.Value;
            _stuckHoldMaxSeconds = category.CreateEntry("minion_stuck_hold_max_seconds", 10,
                description: "Outside the near radius, seconds to hold stuck teleports without a hit in the last 5 seconds; 0 = unlimited.").Value;
            _stuckHoldNearRadius = category.CreateEntry("minion_stuck_hold_near_radius", 10f,
                description: "3D distance in meters from the owner within which stuck teleports are always held when stuck hold is enabled; 0 = disabled.").Value;
            _heightHoldMaxDy = category.CreateEntry("minion_height_hold_max_dy", 8f,
                description: "Height difference in meters up to which far/height/stuck pull-backs are held (with horizontal distance <= the teleport distance); beyond it vanilla applies; 0 = no height limit.").Value;
            _heightTeleportInRange = category.CreateEntry("minion_height_teleport_in_range", false,
                description: "true = vanilla height teleports; false = suppress them within the minion's teleport range.").Value;
            try
            {
                PatchPrefix(h, typeof(RaisedMinionSystem), "Update", new[] { typeof(Frame) }, nameof(BeforeRaisedUpdate), typeof(void));
                Write("minion update teleport hooks installed");
            }
            catch (Exception e) { Write("minion update teleport hooks FAILED " + e.GetType().Name + ": " + e.Message); }
            try
            {
                Patch(h, typeof(Frame.FrameEvents), "RaisedMinionSpawned",
                    new[] { typeof(EntityRef), typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(AssetGuid) },
                    nameof(AfterMinionSpawned), typeof(EventRaisedMinionSpawned));
                Write("minion spawn registration installed");
            }
            catch (Exception e) { Write("minion spawn registration FAILED " + e.GetType().Name + ": " + e.Message); }
            Write("minion_stuck_teleport_in_combat=" + _stuckTeleportInCombat);
            try
            {
                PatchPrefix(h, typeof(PathFollowingComponent), "SearchPath", new[] { typeof(Frame), typeof(EntityRef) },
                    nameof(BeforeSearchPath), typeof(void));
                Write("path-search nav guard installed");
            }
            catch (Exception e) { Write("path-search nav guard FAILED " + e.GetType().Name + ": " + e.Message); }
            var tpEntry = category.CreateEntry("minion_teleport_distance_multiplier", NecromancyPolicy.DefaultTeleportDistanceMultiplier,
                description: "Multiplier on the shipped 20 m distance at which a minion snaps back to the player (3 = 60 m; 1 = original).");
            _teleportMultiplier = tpEntry.Value;
            var npcEntry = category.CreateEntry("minion_npc", NecromancyPolicy.DefaultMinionNpc,
                description: "What raiseMinion summons: warrick_boss (Warrick the Torn), warrick_enemy, wildling, native (shipped data), or an NpcData GUID. revive is never changed.");
            var maxEntry = category.CreateEntry("minion_max_active", NecromancyPolicy.DefaultMinionMaxActive,
                description: "Most raiseMinion minions at once while minion_npc swaps the summon (shipped 5); 0 = keep the shipped value.");
            _minionNpc = NecromancyPolicy.MinionNpcGuid(npcEntry.Value);
            _minionMaxActive = maxEntry.Value;
            Write("minion_npc=" + npcEntry.Value + " -> " + (_minionNpc == 0 ? "native" : NecromancyPolicy.NpcName(_minionNpc)) + "; minion_max_active=" + _minionMaxActive);
            try
            {
                PatchPrefix(h, typeof(RaiseMinionActionData), "Execute", new[] { typeof(Frame), typeof(EntityRef) }, nameof(BeforeRaiseData), typeof(bool));
                PatchPrefix(h, typeof(RaiseMinionActionData), "CanRaiseNearby", new[] { typeof(Frame), typeof(EntityRef) }, nameof(BeforeRaiseData), typeof(bool));
                Write("minion_teleport_distance_multiplier=" + _teleportMultiplier);
            }
            catch (Exception e) { Write("minion teleport-distance hook FAILED " + e.GetType().Name + ": " + e.Message); }
            try
            {
                Patch(h, typeof(Frame.FrameEvents), "RaisedMinionTeleported",
                    new[] { typeof(EntityRef), typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(AssetGuid) },
                    nameof(AfterMinionTeleported), typeof(EventRaisedMinionTeleported));
                Write("minion teleport log installed");
            }
            catch (Exception e) { Write("minion teleport log FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        // Every minion comes from RaiseMinionActionData.Execute, so scaling the asset there (and in CanRaiseNearby)
        // covers all of them. Written values are remembered per asset so a reused asset is never scaled twice, and a
        // reloaded asset (back at the shipped value) is scaled again.
        private static readonly Dictionary<IntPtr, long> TeleportWritten = new();
        private static float _teleportMultiplier = NecromancyPolicy.DefaultTeleportDistanceMultiplier;

        private static bool _stuckTeleportInCombat, _heightTeleportInRange;
        private static volatile bool _stuckHoldOff;
        private static int _stuckResets, _stuckLogs, _stuckHoldMaxSeconds, _heightLogs;
        private static float _stuckHoldNearRadius;
        private static readonly HashSet<ulong> TeleportMinions = new(); // sim thread; no visual-cache timeout
        private static readonly Dictionary<ulong, (long Duration, bool Released)> HeldStuck = new();
        private static readonly HashSet<ulong> HeldHeight = new();
        private static Frame _simFrame; // teleport events fire inside RaisedMinionSystem.Update

        // FollowStartDistance values raised for this frame (sim thread only). Restored by the postfix; if Update threw and
        // the postfix did not run, the next prefix restores them first. No Harmony __state/finalizer: neither is proven on
        // detoured IL2CPP methods in this runtime.
        // Field: 0 = FollowStartDistance (height branch), 1 = MaxDistanceBeforeTeleport (far branch).
        private static readonly List<(EntityRef Minion, int Field, long Original)> FollowStartRestore = new();
        private static float _heightHoldMaxDy;

        private static unsafe void BeforeRaisedUpdate(Frame __0)
        {
            if (FollowStartRestore.Count > 0) RestoreFollowStartDistances(__0);
            try
            {
                FirstHit(ref _raisedUpdateHit, nameof(BeforeRaisedUpdate));
                if (!_enabled || __0 == null) return;
                _simFrame = __0;
                if (_stuckHoldOff || (_stuckTeleportInCombat && _heightTeleportInRange)) return;
                bool verified = __0.IsVerified;
                List<ulong> gone = null;
                foreach (ulong raw in TeleportMinions)
                {
                    var minion = new EntityRef { Raw = raw };
                    if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, __0))
                    {
                        if (verified) (gone ??= new()).Add(raw);
                        continue;
                    }
                    var comp = EntityRefExtensions.Get<RaisedMinionComponent>(minion, __0);
                    if (comp == null) continue;
                    long before = comp->StuckDuration.RawValue;
                    if (before == 0) HeldStuck.Remove(raw);
                    bool checkStuck = !_stuckTeleportInCombat && before > 0;
                    if (_heightTeleportInRange && !checkStuck) continue;
                    var ownerTransform = GetSimTransform(__0, comp->Owner);
                    var minionTransform = GetSimTransform(__0, minion);
                    if (ownerTransform == null || minionTransform == null) continue;
                    var owner = ownerTransform->RawPosition;
                    var pos = minionTransform->RawPosition;
                    double dx = (owner.X.RawValue - pos.X.RawValue) / 65536.0,
                           dy = (owner.Y.RawValue - pos.Y.RawValue) / 65536.0,
                           dz = (owner.Z.RawValue - pos.Z.RawValue) / 65536.0;
                    double horizSq = dx * dx + dz * dz;
                    double d3 = Math.Sqrt(horizSq + dy * dy);
                    double horiz = Math.Sqrt(horizSq);
                    double maxDist = comp->MaxDistanceBeforeTeleport.RawValue / 65536.0;
                    // Maintainer 2026-11-28: judge by horizontal distance (<= teleport distance) and height difference
                    // (<= minion_height_hold_max_dy) instead of the native 3D distance; beyond either, vanilla applies.
                    bool withinRange = NecromancyPolicy.InHoldRange(horiz, dy, maxDist, _heightHoldMaxDy);
                    long start = comp->FollowStartDistance.RawValue;
                    if (!_heightTeleportInRange && withinRange && d3 > maxDist)
                    {
                        FollowStartRestore.Add((minion, 1, comp->MaxDistanceBeforeTeleport.RawValue));
                        comp->MaxDistanceBeforeTeleport.RawValue = (long)((d3 + 1.0) * 65536.0);
                    }
                    if (!_heightTeleportInRange && withinRange && Math.Abs(dy) > 3.0 && horiz > start / 65536.0)
                    {
                        FollowStartRestore.Add((minion, 0, start));
                        comp->FollowStartDistance.RawValue = (long)((horiz + 1.0) * 65536.0);
                        if (verified && HeldHeight.Add(raw) && Interlocked.Increment(ref _heightLogs) <= 100)
                            Write("height-teleport held entity=" + NecromancyPolicy.EntityLabel(raw) +
                                  " dy=" + dy.ToString("0.#") + " horiz=" + horiz.ToString("0.#"));
                    }
                    else if (verified) HeldHeight.Remove(raw);

                    bool near = _stuckHoldNearRadius > 0 && d3 <= _stuckHoldNearRadius && (_heightHoldMaxDy <= 0 || Math.Abs(dy) <= _heightHoldMaxDy);
                    bool following = comp->IsFollowing.Value != 0;
                    if (!checkStuck || (!near && !withinRange)) continue;
                    HeldStuck.TryGetValue(raw, out var held);
                    if (!near) held.Duration += __0.DeltaTime.RawValue;
                    long? lastHit = MinionLastDamage.TryGetValue(raw, out long hit) ? hit : null;
                    if (!NecromancyPolicy.ShouldHoldStuckTeleport(following, withinRange, near,
                        held.Duration, _stuckHoldMaxSeconds, __0.TimeSinceStart.RawValue, lastHit))
                    {
                        if (!held.Released)
                            Write("stuck-teleport released (no progress " + (held.Duration / 65536.0).ToString("0.##") +
                                  "s, no hits) entity=" + NecromancyPolicy.EntityLabel(raw));
                        HeldStuck[raw] = (held.Duration, true);
                        continue;
                    }
                    if (near) HeldStuck.Remove(raw);
                    else HeldStuck[raw] = held;
                    comp->StuckDuration = default;
                    int n = Interlocked.Increment(ref _stuckResets);
                    if (verified && before >= (2L << 16) && Interlocked.Increment(ref _stuckLogs) <= 100)
                        Write("stuck-teleport held (" + (near ? "near owner" : following ? "following in range" : "off-follow in range") + ") entity=" +
                              NecromancyPolicy.EntityLabel(raw) + " stuck=" + (before / 65536.0).ToString("0.##") + "s resets=" + n);
                }
                if (gone != null) foreach (ulong raw in gone)
                {
                    TeleportMinions.Remove(raw);
                    HeldStuck.Remove(raw);
                    HeldHeight.Remove(raw);
                    MinionLastDamage.TryRemove(raw, out _);
                }
            }
            catch (Exception e) { BreakTeleportHolds(e); }
        }

        private static void BreakTeleportHolds(Exception e)
        {
            _stuckHoldOff = true;
            Write("teleport-hold ERROR " + e.GetType().Name + ": " + e.Message + "; teleport holds turned off, rest of the plugin unaffected");
        }

        private static unsafe TransformComponent* GetSimTransform(Frame f, EntityRef entity) =>
            entity.IsValid && EntityRefExtensions.Has<TransformComponent>(entity, f)
                ? EntityRefExtensions.Get<TransformComponent>(entity, f) : null;

        private static unsafe bool OffFollowWithinRange(RaisedMinionComponent* comp, double ownerDist) =>
            comp->IsFollowing.Value == 0 && ownerDist <= comp->MaxDistanceBeforeTeleport.RawValue / 65536.0;

        private static void AfterMinionSpawned(EntityRef __0, AssetGuid __2)
        {
            try
            {
                FirstHit(ref _minionSpawnedHit, nameof(AfterMinionSpawned));
                if (!_enabled || !__0.IsValid) return;
                Minions[__0.Raw] = Environment.TickCount64;
                if (!_stuckHoldOff && (!_stuckTeleportInCombat || !_heightTeleportInRange)) TeleportMinions.Add(__0.Raw);
                TrackBossSpawn(__0, __2);
            }
            catch (Exception e) { Write("spawn-register ERROR " + e.GetType().Name + ": " + e.Message); }
        }

        // Which native branch fired (static analysis T-011): far = 3D owner distance > comp.MaxDistanceBeforeTeleport;
        // height = |dy| > 3 m and horizontal > comp.FollowStartDistance; stuck = StuckDuration >= 3 s (not yet reset
        // when the event is raised). Read-only.
        private static unsafe string ClassifyTeleport(EntityRef minion, Il2CppPhoton.Deterministic.FPVector3 from)
        {
            var f = _simFrame;
            if (f == null || !EntityRefExtensions.Has<RaisedMinionComponent>(minion, f)) return "state=?";
            var comp = EntityRefExtensions.Get<RaisedMinionComponent>(minion, f);
            if (comp == null) return "state=?";
            var t = GetSimTransform(f, comp->Owner);
            string where = " offFollowWithinRange=?";
            string branch = comp->StuckDuration.RawValue >= (3L << 16) ? "stuck" : "?";
            if (t != null)
            {
                var owner = t->RawPosition;
                double dx = (from.X.RawValue - owner.X.RawValue) / 65536.0, dy = (from.Y.RawValue - owner.Y.RawValue) / 65536.0,
                       dz = (from.Z.RawValue - owner.Z.RawValue) / 65536.0;
                double d3 = Math.Sqrt(dx * dx + dy * dy + dz * dz), dh = Math.Sqrt(dx * dx + dz * dz);
                double maxD = comp->MaxDistanceBeforeTeleport.RawValue / 65536.0, start = comp->FollowStartDistance.RawValue / 65536.0;
                if (d3 > maxD) branch = "far";
                else if (branch != "stuck" && Math.Abs(dy) > 3.0 && dh > start) branch = "height";
                where = " offFollowWithinRange=" + OffFollowWithinRange(comp, d3) +
                        " ownerDist=" + d3.ToString("0.#") + " horiz=" + dh.ToString("0.#") + " dy=" + dy.ToString("0.#") +
                        " max=" + maxD.ToString("0.#") + " followStart=" + start.ToString("0.#");
            }
            return "branch=" + branch + " stuck=" + (comp->StuckDuration.RawValue / 65536.0).ToString("0.##") +
                   "s following=" + (comp->IsFollowing.Value != 0) + where;
        }

        // Logs each native RaisedMinionSystem teleport (raised through Frame.FrameEvents). Predicted frames can raise
        // the same teleport again, so one line per minion per second.
        private static readonly Dictionary<ulong, long> LastTeleportLog = new();
        private static int _teleportLogs;

        private static void AfterMinionTeleported(EntityRef __0, Il2CppPhoton.Deterministic.FPVector3 __1, Il2CppPhoton.Deterministic.FPVector3 __2)
        {
            try
            {
                FirstHit(ref _minionTeleportedHit, nameof(AfterMinionTeleported));
                if (!_enabled) return;
                long now = Environment.TickCount64;
                lock (LastTeleportLog)
                {
                    if (LastTeleportLog.TryGetValue(__0.Raw, out long last) && now - last < 1000) return;
                    LastTeleportLog[__0.Raw] = now;
                }
                if (Interlocked.Increment(ref _teleportLogs) > 400) return;
                double d = NecromancyPolicy.DistanceRaw(__1.X.RawValue, __1.Y.RawValue, __1.Z.RawValue, __2.X.RawValue, __2.Y.RawValue, __2.Z.RawValue);
                Minions[__0.Raw] = Environment.TickCount64;
                string state;
                try { state = ClassifyTeleport(__0, __1); } catch (Exception ce) { state = "state=ERR " + ce.GetType().Name; }
                Write("minion TELEPORTED entity=" + __0.Raw + " " + NecromancyPolicy.EntityLabel(__0.Raw) + " moved=" + d.ToString("0.#") + " m " + state);
            }
            catch (Exception e) { Error("teleport-log", e); }
        }

        // T-010: raiseMinion (UseCorpseNpcType=false) summons the configured NPC instead of npcRisenWildlingSwordA.
        // The target must resolve to an NpcData asset first; otherwise the shipped summon is left alone. Written
        // values are remembered per asset so a reloaded asset is swapped again and an unchanged one is not touched.
        private static long _minionNpc;
        private static int _minionMaxActive;
        private static readonly Dictionary<IntPtr, (long Npc, int Max)> SummonWritten = new();
        private static readonly HashSet<IntPtr> SummonSkipLogged = new();

        // 0.15.3 playtest: EntityRefExtensions.GetData<NpcData> returned nothing for a raised Warrick (npc=0).
        // The spawned NPC's data lives in NpcAttachmentComponent.NpcData (a storage index resolved by Get(Frame)).
        internal static long MinionNpcGuid(Frame f, EntityRef minion) => MinionNpcData(f, minion)?.Guid.Value ?? 0;

        internal static unsafe NpcData MinionNpcData(Frame f, EntityRef minion)
        {
            if (f == null || !EntityRefExtensions.Has<NpcAttachmentComponent>(minion, f)) return null;
            var a = EntityRefExtensions.Get<NpcAttachmentComponent>(minion, f);
            if (a == null || a->NpcData.IsDefault) return null;
            return a->NpcData.Get(f);
        }

        private static string MinionNpcText(Frame f, EntityRef minion)
        {
            try { return NecromancyPolicy.NpcName(MinionNpcGuid(f, minion)); }
            catch (Exception e) { return "?" + e.GetType().Name; }
        }

        private static void ApplySummon(Frame f, RaiseMinionActionData d)
        {
            if (_minionNpc == 0) return;
            if (f == null || d.UseCorpseNpcType)
            {
                if (SummonSkipLogged.Add(d.Pointer))
                    Write("summon SKIP asset=" + d.Guid.Value + " " + (f == null ? "no frame" : "UseCorpseNpcType=true (revive keeps the corpse's enemy)"));
                return;
            }
            long current = d.MinionNpcData.Id.Value;
            int currentMax = d.MaxActiveMinions;
            if (SummonWritten.TryGetValue(d.Pointer, out var w) && w.Npc == current && w.Max == currentMax) return;
            var npc = f.Context?.ResourceManager?.GetAsset(new AssetGuid { Value = _minionNpc })?.TryCast<NpcData>();
            if (npc == null)
            {
                SummonWritten[d.Pointer] = (current, currentMax);
                Write("summon KEEP " + NecromancyPolicy.NpcName(current) + ": " + NecromancyPolicy.NpcName(_minionNpc) + " is not a loadable NpcData");
                return;
            }
            int max = NecromancyPolicy.MinionMaxActive(_minionNpc, _minionMaxActive, currentMax);
            d.MinionNpcData = new AssetRefNpcData { Id = new AssetGuid { Value = _minionNpc } };
            d.MaxActiveMinions = max;
            SummonWritten[d.Pointer] = (d.MinionNpcData.Id.Value, d.MaxActiveMinions);
            Write("summon SWAP " + NecromancyPolicy.NpcName(current) + " -> " + NecromancyPolicy.NpcName(d.MinionNpcData.Id.Value) +
                  " (enemyType " + npc.EnemyType + "); max active " + currentMax + " -> " + d.MaxActiveMinions +
                  "; health x boss/giant/elite/normal " + Fp(d.HealthMultiplierBoss) + "/" + Fp(d.HealthMultiplierGiant) + "/" + Fp(d.HealthMultiplierElite) + "/" + Fp(d.HealthMultiplierNormal) +
                  "; damage x " + Fp(d.DamageMultiplierBoss) + "/" + Fp(d.DamageMultiplierGiant) + "/" + Fp(d.DamageMultiplierElite) + "/" + Fp(d.DamageMultiplierNormal));
        }

        private static string Fp(Il2CppPhoton.Deterministic.FP v) => (v.RawValue / 65536.0).ToString("0.##");

        private static bool BeforeRaiseData(Frame __0, RaiseMinionActionData __instance)
        {
            try
            {
                FirstHit(ref _raiseDataHit, nameof(BeforeRaiseData));
                if (!_enabled || __instance == null) return true;
                // Boss rune actions keep their own summon target; they still get the teleport-distance multiplier below.
                if (!IsPrivateBossAction(__instance.Guid.Value)) try { ApplySummon(__0, __instance); } catch (Exception e) { Error("summon", e); }
                var d = __instance.MaxDistanceBeforeTeleport;
                if (TeleportWritten.TryGetValue(__instance.Pointer, out long written) && written == d.RawValue) return true;
                long scaled = NecromancyPolicy.ScaledTeleportRaw(d.RawValue, _teleportMultiplier);
                TeleportWritten[__instance.Pointer] = scaled;
                if (scaled == d.RawValue) return true;
                Write("minion teleport distance " + (d.RawValue / 65536.0).ToString("0.##") + " -> " + (scaled / 65536.0).ToString("0.##") + " m");
                d.RawValue = scaled;
                __instance.MaxDistanceBeforeTeleport = d;
            }
            catch (Exception e) { Error("teleport-distance", e); }
            return true;
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
                FirstHit(ref _enemyRegenHit, nameof(BeforeEnemyRegen));
                if (!_enabled || __0 == null || !EntityRefExtensions.Has<RaisedMinionComponent>(__1, __0)) return true;
                if (__0.IsVerified && Interlocked.Increment(ref _regenLogs) <= 5) Write("regen SKIPPED for raised minion entity=" + __1.Raw);
                return false;
            }
            catch (Exception e) { Error("regen", e); return true; }
        }

        // A minion we expired leaves no raisable corpse.
        private static bool BeforeNpcDied(Frame __0, EntityRef __1)
        {
            FirstHit(ref _npcDiedHit, nameof(BeforeNpcDied));
            if (!Expired.Contains(__1.Raw)) return true;
            if (__0 != null && __0.IsVerified) Write("expired minion " + __1.Raw + " leaves no corpse");
            return false;
        }

        // Called from the verified end-of-frame tick.
        // 0.13.1 crashed on ~1200 "E.x does not have UseNavDataComponent" from PathFollowingSystem; the four entities
        // never appeared in this log because several log caps were already hit. List live minions in the Unity
        // E.index.version form every 5 s of simulation time (and whenever the set changes) so they can be matched.
        private static long _lastRosterRaw;
        private static string _lastRoster = "";
        private static int _rosterLogs, _rosterErrors;
        private static volatile bool _rosterOff;

        private static void LogRoster(Frame f)
        {
            long now = f.TimeSinceStart.RawValue;
            if (now - _lastRosterRaw < (5L << 16) && now >= _lastRosterRaw) return;
            var parts = new List<string>();
            foreach (var pair in MinionBirth)
            {
                var minion = new EntityRef { Raw = pair.Key };
                if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, f)) continue;
                bool nav = EntityRefExtensions.Has<UseNavDataComponent>(minion, f);
                parts.Add(NecromancyPolicy.EntityLabel(pair.Key) + (nav ? "" : "(NO-NAV)"));
            }
            parts.Sort(StringComparer.Ordinal);
            string roster = string.Join(" ", parts);
            if (roster == _lastRoster && now - _lastRosterRaw < (30L << 16)) return;
            _lastRosterRaw = now; _lastRoster = roster;
            if (Interlocked.Increment(ref _rosterLogs) <= 1000) Write("roster n=" + parts.Count + " " + roster);
        }

        // AttachmentAPI.ResolveNavDataAttachment calls Frame.Get<UseNavDataComponent> unconditionally and throws when
        // the entity has none; 0.13.1 died on ~1200 of those from PathFollowingComponent.SearchPath ("too many
        // exceptions"). Static analysis: SearchPath resolves nav data for its agent once it is outside the acceptance
        // radius, and a null result is dereferenced, so returning null is not safe. Guard one level up instead:
        // skip SearchPath for an agent without UseNavDataComponent (the entity simply gets no new path this frame;
        // NavMeshTrackerSystem adds the component once it stands on an active navmesh). The probe on
        // ResolveNavDataAttachment stays read-only and catches any other caller that still reaches the throw.
        private static readonly HashSet<ulong> NoNavLogged = new();
        private static int _noNavCount, _guardCount, _navHookErrors;

        private static bool BeforeSearchPath(Frame __0, EntityRef __1)
        {
            try
            {
                FirstHit(ref _searchPathHit, nameof(BeforeSearchPath));
                if (__0 == null || !__1.IsValid) return true;
                if (EntityRefExtensions.Has<UseNavDataComponent>(__1, __0)) return true;
            }
            catch (Exception e) { LogNavHookException("path-guard", e); return true; }

            try { LogNoNav(__0, __1, "GUARD skipped SearchPath", Interlocked.Increment(ref _guardCount)); }
            catch (Exception e) { LogNavHookException("path-guard", e); }
            return false;
        }

        private static void BeforeResolveNavData(Frame __0, EntityRef __1)
        {
            try
            {
                FirstHit(ref _resolveNavDataHit, nameof(BeforeResolveNavData));
                if (__0 == null || !__1.IsValid) return;
                if (EntityRefExtensions.Has<UseNavDataComponent>(__1, __0)) return;
                LogNoNav(__0, __1, "NO-NAV resolve (native will throw)", Interlocked.Increment(ref _noNavCount));
            }
            catch (Exception e) { LogNavHookException("nav-probe", e); }
        }

        private static void LogNavHookException(string stage, Exception e)
        {
            if (Interlocked.Increment(ref _navHookErrors) > 5) return;
            try { Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message); }
            catch (Exception) { }
        }

        private static void LogNoNav(Frame f, EntityRef e, string what, int n)
        {
            bool first;
            lock (NoNavLogged) first = NoNavLogged.Add(e.Raw ^ (ulong)what.Length);
            if (!first) { if (n % 500 == 0) Write(what + " count=" + n); return; }
            bool minion = EntityRefExtensions.Has<RaisedMinionComponent>(e, f);
            bool corpse = EntityRefExtensions.Has<CorpseMarkerComponent>(e, f);
            Write(what + " entity=" + e.Raw + " " + NecromancyPolicy.EntityLabel(e.Raw) + " minion=" + minion + " corpseMarker=" + corpse +
                  " knownMinion=" + MinionBirth.ContainsKey(e.Raw) + " expired=" + Expired.Contains(e.Raw) + " seenAsMinion=" + Minions.ContainsKey(e.Raw) +
                  " verified=" + f.IsVerified + " count=" + n);
        }

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

        private static int _allyLogs, _nativeAllyLogs;

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
            if (__result)
            {
                if (_nativeAllyLogs < 10 && __0 != null && __0.IsVerified && (Minions.ContainsKey(__1.Raw) || Minions.ContainsKey(__2.Raw)) &&
                    Interlocked.Increment(ref _nativeAllyLogs) <= 10)
                    Write("AreAllies native=true hit=" + __1.Raw + (Minions.ContainsKey(__1.Raw) ? "(minion)" : "") + " source=" + __2.Raw + (Minions.ContainsKey(__2.Raw) ? "(minion)" : ""));
                return;
            }
            if (!_enabled || __0 == null || !__1.IsValid || !__2.IsValid) return;
            try
            {
                if (!EntityRefExtensions.Has<RaisedMinionComponent>(__1, __0) &&
                    !EntityRefExtensions.Has<RaisedMinionComponent>(__2, __0)) return;
                var hit = ClassifyDamageActor(__0, __1);
                var source = ClassifyDamageActor(__0, __2);
                bool aMinion = hit.RaisedByPlayer, bMinion = source.RaisedByPlayer;
                bool aHero = hit.Player, bHero = source.Player;
                if (!NecromancyPolicy.MinionPairIsAllied(aMinion, aHero, bMinion, bHero)) return;
                __result = true;
                if (__0.IsVerified && Interlocked.Increment(ref _allyLogs) <= 20)
                    Write("minion allies override hit=" + __1.Raw + (aMinion ? "(minion)" : aHero ? "(hero)" : "") +
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
                bool boss = IsReadyBossItem(__0, rune);
                if (!boss && !NecromancyPolicy.IsNecromancyRune(rune)) return;
                if (!EntityRefExtensions.Has<ItemComponent>(__1, __0)) return;
                var component = EntityRefExtensions.Get<ItemComponent>(__1, __0);
                if (component == null) return;
                long item = component->Data.Id.Value;
                if (!(boss ? item == NecromancyPolicy.TomeOfHealing : NecromancyPolicy.TomeAccepts(item, rune))) return;
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
                int weaponClass = (int)__1->Class;
                if (!NecromancyPolicy.ClasslessAccepts(weaponClass, rune) && !(weaponClass == 0 && IsReadyBossItem(__0, rune))) return;
                __result = true;
                if (Interlocked.Increment(ref _compatLogs) <= 10) Write("tome-runes ALLOW classless weapon rune=" + rune);
            }
            catch (Exception e) { Error("tome-runes/component", e); }
        }

        // ---- diagnostics -------------------------------------------------------------------

        private const int FollowDiagnosticLimit = 1500;
        private static bool _followDiagnosticsEnabled, _followDiagnosticsFailed;
        private static int _followDiagnosticLines;
        private static readonly Dictionary<ulong, (long Time, Il2CppPhoton.Deterministic.FPVector3 Position)> MinionFollowSamples = new();

        private static void AfterCorpseUpdate(Frame __0)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _corpseTicks) == 1) Write("CorpseMarkerSystem running");
        }

        private static void AfterRaisedUpdate(Frame __0)
        {
            if (FollowStartRestore.Count > 0) RestoreFollowStartDistances(__0);
            try
            {
                if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _raisedTicks) == 1) Write("RaisedMinionSystem running");
            }
            catch (Exception e) { BreakTeleportHolds(e); }
            LogMinionFollow(__0);
        }

        private static unsafe void LogMinionFollow(Frame f)
        {
            if (!_followDiagnosticsEnabled || _followDiagnosticsFailed || _followDiagnosticLines >= FollowDiagnosticLimit) return;
            try
            {
                if (!_enabled || f == null || !f.IsVerified) return;
                long now = f.TimeSinceStart.RawValue;
                foreach (var pair in f.GetComponentIterator<RaisedMinionComponent>())
                {
                    var minion = pair.Entity;
                    bool sampled = MinionFollowSamples.TryGetValue(minion.Raw, out var last);
                    if (sampled && now - last.Time < (1L << 16)) continue;
                    var comp = EntityRefExtensions.Get<RaisedMinionComponent>(minion, f);
                    if (comp == null) continue;
                    var minionTransform = GetSimTransform(f, minion);
                    if (minionTransform == null) continue;
                    var pos = minionTransform->RawPosition;
                    MinionFollowSamples[minion.Raw] = (now, pos);

                    var ownerTransform = GetSimTransform(f, comp->Owner);
                    if (ownerTransform == null) continue;
                    var owner = ownerTransform->RawPosition;
                    double dx = (owner.X.RawValue - pos.X.RawValue) / 65536.0,
                           dy = (owner.Y.RawValue - pos.Y.RawValue) / 65536.0,
                           dz = (owner.Z.RawValue - pos.Z.RawValue) / 65536.0;
                    double horizSq = dx * dx + dz * dz;
                    double ownerDist = Math.Sqrt(horizSq + dy * dy);
                    double stop = comp->FollowStopDistance.RawValue / 65536.0;
                    if (!(ownerDist > stop + 1.0)) continue;

                    string moved = sampled ? NecromancyPolicy.DistanceRaw(last.Position.X.RawValue, last.Position.Y.RawValue,
                        last.Position.Z.RawValue, pos.X.RawValue, pos.Y.RawValue, pos.Z.RawValue).ToString("0.##") + "m" : "?";
                    Write(FormattableString.Invariant($"minion FOLLOW entity={NecromancyPolicy.EntityLabel(minion.Raw)} RaiseActionGuid=0x{comp->RaiseActionGuid.Value:X16} IsFollowing={(comp->IsFollowing.Value != 0)} moved={moved} ownerDist={ownerDist:0.##}m horiz={Math.Sqrt(horizSq):0.##}m dy={dy:0.##}m StuckDuration={comp->StuckDuration.RawValue / 65536.0:0.##}s FollowStartDistance={comp->FollowStartDistance.RawValue / 65536.0:0.##}m FollowStopDistance={stop:0.##}m MaxDistanceBeforeTeleport={comp->MaxDistanceBeforeTeleport.RawValue / 65536.0:0.##}m"));
                    if (++_followDiagnosticLines == FollowDiagnosticLimit)
                    {
                        Write($"minion follow diagnostics reached {FollowDiagnosticLimit} lines; stopped for this session");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                _followDiagnosticsFailed = true;
                Write("minion follow diagnostics disabled: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static unsafe void RestoreFollowStartDistances(Frame __0)
        {
            var pending = FollowStartRestore;
            Exception failure = null;
            for (int i = pending.Count - 1; i >= 0; --i)
            {
                var (minion, field, original) = pending[i];
                try
                {
                    if (EntityRefExtensions.Has<RaisedMinionComponent>(minion, __0))
                    {
                        var c = EntityRefExtensions.Get<RaisedMinionComponent>(minion, __0);
                        if (field == 1) c->MaxDistanceBeforeTeleport.RawValue = original;
                        else c->FollowStartDistance.RawValue = original;
                    }
                    pending.RemoveAt(i);
                }
                catch (Exception e) { failure ??= e; }
            }
            if (failure != null) BreakTeleportHolds(failure);
        }

        private static void AfterRaiseExecute(Frame __0, EntityRef __1, RaiseMinionActionData __instance, bool __result)
        {
            try
            {
                if (__instance != null && IsPrivateBossAction(__instance.Guid.Value)) LogBossExecute(__0, __1, __instance, __result);
                else if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _executes) <= 200)
                    Write("raise Execute caster=" + __1.Raw + " -> " + __result);
            }
            catch (Exception e) { BossError("Execute log", e); }
        }

        private static void AfterCanRaise(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _canRaise) <= 10)
                Write("raise CanRaiseNearby caster=" + __1.Raw + " -> " + __result);
        }

        private static void AfterHealExecute(Frame __0, EntityRef __1, bool __result)
        {
            if (__0 != null && __0.IsVerified && Interlocked.Increment(ref _heals) <= 100)
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

        private static int _searchPathHit, _resolveNavDataHit, _raisedUpdateHit, _minionSpawnedHit,
            _minionTeleportedHit, _raiseDataHit, _enemyRegenHit, _npcDiedHit;

        private static void FirstHit(ref int flag, string name)
        {
            if (Interlocked.Exchange(ref flag, 1) == 0) Write("hook FIRST HIT " + name);
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
