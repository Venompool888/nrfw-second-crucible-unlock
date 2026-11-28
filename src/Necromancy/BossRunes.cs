using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Il2Cpp;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Playmode;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using MelonLoader;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private const long RaiseMinionActionGuid = 22455697660660456;
        private static BossRuneRule[] _bossRules = Array.Empty<BossRuneRule>();
        private static HashSet<long> _bossActions = new();
        private static readonly HashSet<IntPtr> BossStorages = new();
        private static volatile BossSession _bossSession;
        private static bool _bossEnabled, _bossEnglish;
        private static MelonPreferences_Entry<string> _bossGrantedEntry;
        private static volatile string _bossGranted;
        private static int _bossGrantDirty, _bossErrors;
        private static string _bossIconDirectory;
        private static string _bossTomeCharacter = "";

        private sealed class BossSession
        {
            internal IntPtr Storage, Manager;
            internal readonly Dictionary<long, BossRune> Runes = new();
            internal readonly Dictionary<(ulong Entity, long Action), EntityRef> Spawns = new();
            internal int HeroFrames;
            internal bool ReadLogged, ViewContextWaitLogged, TomeAttempted;
        }

        private sealed class BossRune
        {
            internal BossRuneRule Rule;
            internal HeroRuneData Item;
            internal RaiseMinionActionData Action;
            internal volatile bool ViewReady;
            internal bool GrantAttempted;
            internal int ExecuteLogs, CountLogs, ActionViewLogs, SpawnLogs;
        }

        private static AssetGuid BossGuid(long guid) => new AssetGuid { Value = guid };
        private static string BossId(long guid) => "0x" + guid.ToString("X16");
        private static string BossLabel(BossRuneRule rule) => "boss-rune " + rule.Key + " I=" + BossId(rule.ItemGuid);
        private static void BossError(string stage, Exception e)
        {
            if (Interlocked.Increment(ref _bossErrors) <= 10) Write("boss-runes " + stage + " ERROR " + e.GetType().Name + ": " + e.Message);
        }

        private static void InstallBossRunes(MelonPreferences_Category category)
        {
            var harmony = new HarmonyLib.Harmony("NRFW.Necromancy.BossRunes");
            try
            {
                _bossGrantedEntry = category.CreateEntry("granted_boss_runes", "",
                    description: "One-time Boss rune grants, comma-separated character:key. Remove a pair to allow another grant.");
                _bossGranted = _bossGrantedEntry.Value;
                string language = category.CreateEntry("text_language", "zh", description: "Boss rune text: zh or en.").Value;
                _bossEnglish = string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase);
                bool enabled = category.CreateEntry("boss_runes_enabled", true, description: "Register and grant custom Boss runes.").Value;
                _bossTomeCharacter = (category.CreateEntry("boss_tome_character", "",
                    description: "Character (case-insensitive) that once gets a new Gold Tome of Healing with every Boss rune socketed. Recorded in granted_boss_runes as character:@tome.").Value ?? "").Trim();
                string directory = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
                _bossIconDirectory = Path.Combine(directory, BossRunePolicy.IconDirectory);
                _bossRules = LoadBossRules(Path.Combine(directory, BossRunePolicy.FileName)).OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
                if (!enabled) return;
                _bossActions = new HashSet<long>(_bossRules.Where(r => r.Enabled).Select(r => r.ActionGuid));
                Patch(harmony, typeof(InitializeContentSystem), "OnInit", new[] { typeof(Frame) }, nameof(AfterBossContentInit));
                PatchPrefix(harmony, typeof(InitializeQuantumAssetDatabase), "InitializeResourceManager", new[] { typeof(ViewFrame) },
                    nameof(BeforeBossResourceManagerReset), typeof(void));
                PatchPrefix(harmony, typeof(RaiseMinionActionData), "CountActiveMinions", new[] { typeof(Frame), typeof(EntityRef) },
                    nameof(BeforeBossCount), typeof(int));
                PatchPrefix(harmony, typeof(PlayerSerialization), "LoadHeroFromPlayerSerializedData",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(CharacterContents) }, nameof(BeforeBossHeroLoad), typeof(bool));
                Patch(harmony, typeof(HeroItemDataAsset), "GetName", Type.EmptyTypes, nameof(AfterBossName), typeof(string));
                Patch(harmony, typeof(HeroItemDataAsset), "GetDescription", Type.EmptyTypes, nameof(AfterBossDescription), typeof(string));
                Patch(harmony, typeof(Il2CppMoon.Forsaken.EntityView), "GetActionView", new[] { typeof(ActionData), typeof(bool).MakeByRefType() },
                    nameof(AfterBossActionView), typeof(ActionView));
                _bossEnabled = true;
                Write("boss-runes hooks installed; enabled rules=" + _bossActions.Count);
#if !BOSS_RUNE_PNG
                Write("boss-runes PNG disabled: UnityEngine.ImageConversionModule reference missing");
#endif
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Write("boss-runes install FAILED " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static BossRuneRule[] LoadBossRules(string path)
        {
            try
            {
                if (!File.Exists(path)) { File.WriteAllText(path, BossRunePolicy.DefaultJson); Write("boss-runes wrote " + BossRunePolicy.FileName); }
                var rules = BossRunePolicy.Parse(File.ReadAllText(path), out var errors, out bool fallback);
                foreach (string error in errors) Write("boss-runes " + (fallback ? "file INVALID; using defaults: " : "rule REJECTED ") + error);
                return rules;
            }
            catch (Exception e)
            {
                Write("boss-runes file INVALID; using defaults: " + e.GetType().Name + ": " + e.Message);
                return BossRunePolicy.Parse(BossRunePolicy.DefaultJson, out _, out _);
            }
        }

        private static void AfterBossContentInit(Frame __0)
        {
            try
            {
                if (!_enabled || !_bossEnabled || __0 == null) return;
                var storage = __0.InstanceDataStorage ?? throw new InvalidOperationException("InstanceDataStorage missing");
                var manager = __0.Context.ResourceManager ?? throw new InvalidOperationException("frame ResourceManager missing");
                if (!BossStorages.Add(storage.Pointer)) return;
                var session = new BossSession { Storage = storage.Pointer, Manager = manager.Pointer };
                foreach (var rule in _bossRules)
                {
                    if (!rule.Enabled) continue;
                    try { session.Runes.Add(rule.ItemGuid, RegisterBossRune(__0, storage, rule)); }
                    catch (Exception e) { Write(BossLabel(rule) + " SIM NOT READY: " + e.GetType().Name + ": " + e.Message); }
                }
                _bossSession = session;
            }
            catch (Exception e) { BossError("content init", e); }
        }

        private static BossRune RegisterBossRune(Frame f, InstanceDataStorageSystem storage, BossRuneRule rule)
        {
            string label = BossLabel(rule);
            var i = BossGuid(rule.ItemGuid);
            var a = BossGuid(rule.ActionGuid);
            var l = BossGuid(rule.ItemDataGuid);
            var npc = f.FindAsset<AssetObject>(BossGuid(rule.Npc))?.TryCast<NpcData>();
            if (npc == null || npc.Guid.Value != rule.Npc) throw new InvalidOperationException("not a loadable NpcData: " + rule.Npc);
            Write(label + " NPC resolved=" + rule.Npc);

            if (f.DynamicAssetDB.FindAsset(a) != null || f.DynamicAssetDB.FindAsset(i) != null)
                throw new InvalidOperationException("dynamic asset GUID collision A=" + BossId(a.Value));
            if (storage.m_storageRuneData.m_lookup.ContainsKey(i.Value)) throw new InvalidOperationException("RuneData GUID collision");
            if (storage.m_storageItemData.m_lookup.ContainsKey(l.Value)) throw new InvalidOperationException("ItemData GUID collision L=" + BossId(l.Value));
            if (storage.m_storageCommodityData.m_lookup.ContainsKey(i.Value)) throw new InvalidOperationException("CommodityData GUID collision");
            if (storage.m_storageItemAttachments.m_lookup.ContainsKey(i.Value)) throw new InvalidOperationException("ItemAttachments GUID collision");

            var originalGuid = BossGuid(NecromancyPolicy.RaiseMinionRune);
            var original = storage.Resolve(new ItemAttachmentsId { InternalId = originalGuid.Value }).Get(f);
            if (original == null || original.Guid.Value != originalGuid.Value || original.OldItemId.Id.Value != originalGuid.Value ||
                original.RuneData.IsDefault || original.ItemData.IsDefault || original.CommodityData.IsDefault ||
                !original.UpgradesData.IsDefault || !original.WeaponData.IsDefault || !original.EquipmentData.IsDefault ||
                !original.ArmorData.IsDefault || !original.CraftableData.IsDefault || original.ItemData.Get(f)?.OldItemData.Id.Value != originalGuid.Value)
                throw new InvalidOperationException("shipped raiseMinion attachment shape mismatch");
            var originalRune = original.RuneData.Get(f);
            var originalCommodity = original.CommodityData.Get(f);
            if (!MatchesBossRune(originalRune, originalGuid.Value, RaiseMinionActionGuid) || originalCommodity?.Guid.Value != originalGuid.Value)
                throw new InvalidOperationException("shipped raiseMinion RuneData/CommodityData mismatch");
            Write(label + " template attachments verified; commodity stack=" + originalCommodity.MaxItemsInStack);

            string path = "NRFW/Necromancy/BossRunes/" + rule.Key;
            var action = CloneBossAsset(f, Asset<RaiseMinionActionData>(f, RaiseMinionActionGuid), path + "/Action");
            var item = CloneBossAsset(f, Asset<HeroRuneData>(f, originalGuid.Value), path + "/Item");
            var actions = item.Actions;
            if (actions == null || actions.Length == 0 || actions[0].Id.Value != RaiseMinionActionGuid)
                throw new InvalidOperationException("shipped raiseMinion Actions[0] mismatch");
            actions[0] = new AssetRefActionData { Id = a };
            item.Actions = actions;
            action.MinionNpcData = new AssetRefNpcData { Id = BossGuid(rule.Npc) };
            action.UseCorpseNpcType = false;
            action.MaxActiveMinions = rule.MaxActive;
            var oldItem = new AssetRefHeroItemData { Id = i };
            var rune = new RuneData
            {
                Guid = i, Path = path + "/Rune", OldData = new AssetRefHeroRuneData { Id = i },
                Actions = actions, CompatibleClasses = item.CompatibleClasses, IsUtility = item.IsUtility,
            };
            var itemData = new ItemData { Guid = l, OldItemData = oldItem };
            var commodity = new CommodityData
            {
                Guid = i, MaxItemsInStack = originalCommodity.MaxItemsInStack, SalePrice = originalCommodity.SalePrice,
                ItemValue = originalCommodity.ItemValue, CanBeSold = originalCommodity.CanBeSold,
                CanBeDestroyed = originalCommodity.CanBeDestroyed, FilterType = originalCommodity.FilterType,
            };
            var attachments = new ItemAttachments { Guid = i, OldItemId = oldItem };

            f.AddAsset(action, a);
            if (f.DynamicAssetDB.FindAsset(a)?.Pointer != action.Pointer || action.Guid.Value != a.Value ||
                action.UseCorpseNpcType || action.MinionNpcData.Id.Value != rule.Npc || action.MaxActiveMinions != rule.MaxActive)
                throw new InvalidOperationException("Action AddAsset readback failed");
            Write(label + " AddAsset action verified A=" + BossId(a.Value));
            f.AddAsset(item, i);
            if (f.DynamicAssetDB.FindAsset(i)?.Pointer != item.Pointer || item.Guid.Value != i.Value || item.Actions[0].Id.Value != a.Value)
                throw new InvalidOperationException("HeroRuneData AddAsset readback failed");
            Write(label + " AddAsset item verified");

            attachments.RuneData = storage.Add(rune);
            if (storage.Resolve(new RuneDataId { InternalId = i.Value }).Get(f)?.Pointer != rune.Pointer ||
                attachments.RuneData.Get(f)?.Pointer != rune.Pointer) throw new InvalidOperationException("RuneData Add readback failed");
            Write(label + " Add RuneData verified index=" + attachments.RuneData.m_index);
            attachments.ItemData = storage.Add(itemData);
            var itemRead = storage.Resolve(new ItemDataId { InternalId = l.Value }).Get(f);
            if (itemRead?.Pointer != itemData.Pointer || itemRead.Guid.Value != l.Value || itemRead.OldItemData.Id.Value != i.Value ||
                attachments.ItemData.Get(f)?.Pointer != itemData.Pointer) throw new InvalidOperationException("ItemData Add readback failed");
            Write(label + " Add ItemData verified L=" + BossId(l.Value));
            attachments.CommodityData = storage.Add(commodity);
            var commodityRead = storage.Resolve(new CommodityDataId { InternalId = i.Value }).Get(f);
            if (commodityRead?.Pointer != commodity.Pointer || commodityRead.Guid.Value != i.Value ||
                attachments.CommodityData.Get(f)?.Pointer != commodity.Pointer) throw new InvalidOperationException("CommodityData Add readback failed");
            Write(label + " Add CommodityData verified");
            var attached = storage.Add(attachments).Get(f);
            if (attached?.Pointer != attachments.Pointer || attached.Guid.Value != i.Value || attached.OldItemId.Id.Value != i.Value ||
                storage.Resolve(new ItemAttachmentsId { InternalId = i.Value }).Get(f)?.Pointer != attachments.Pointer)
                throw new InvalidOperationException("ItemAttachments Add readback failed");
            Write(label + " Add ItemAttachments verified");
            var runeRef = RunesAPI.GetRune(f, oldItem);
            if (runeRef.IsDefault || runeRef.m_index != attachments.RuneData.m_index || !MatchesBossRune(runeRef.Get(f), i.Value, a.Value))
                throw new InvalidOperationException("GetRune readback failed: expected I/I/A");
            Write(label + " SIM READY GetRune index=" + runeRef.m_index + " Guid/OldData/Actions[0] match I/I/A");
            return new BossRune { Rule = rule, Item = item, Action = action };
        }

        private static T CloneBossAsset<T>(Frame f, T original, string path) where T : AssetObject
        {
            if (original == null) throw new InvalidOperationException("template missing: " + typeof(T).Name);
            var serializer = f.Context.AssetSerializer;
            var copies = IAssetSerializerExtensions.AssetsFromByteArray(serializer, IAssetSerializerExtensions.AssetToByteArray(serializer, original));
            var copy = copies?.Length == 1 ? copies[0]?.TryCast<T>() : null;
            if (copy == null || copy.Pointer == original.Pointer) throw new InvalidOperationException("deep clone failed: " + typeof(T).Name);
            copy.Path = path;
            copy.Guid = default;
            return copy;
        }

        private static bool MatchesBossRune(RuneData rune, long i, long a) => rune != null && rune.Guid.Value == i &&
            rune.OldData.Id.Value == i && rune.Actions?.Length > 0 && rune.Actions[0].Id.Value == a;

        private static bool IsPrivateBossAction(long guid) => _bossEnabled && _bossActions.Contains(guid);
        private static BossSession BossSessionFor(Frame f)
        {
            var session = _bossSession;
            return session != null && f?.InstanceDataStorage?.Pointer == session.Storage ? session : null;
        }
        private static bool IsReadyBossItem(Frame f, long guid) => _bossEnabled && BossSessionFor(f)?.Runes.ContainsKey(guid) == true;
        private static BossRune BossAction(BossSession session, long action) => session != null &&
            session.Runes.TryGetValue(action - 1, out var rune) && rune.Rule.ActionGuid == action ? rune : null;

        private static RuneDataRef ReadBossRune(Frame f, BossRune rune)
        {
            var i = BossGuid(rune.Rule.ItemGuid);
            var a = BossGuid(rune.Rule.ActionGuid);
            if (f.DynamicAssetDB.FindAsset(i)?.Pointer != rune.Item.Pointer || f.DynamicAssetDB.FindAsset(a)?.Pointer != rune.Action.Pointer)
                throw new InvalidOperationException("frame asset identity changed");
            var r = RunesAPI.GetRune(f, new AssetRefHeroItemData { Id = i });
            if (r.IsDefault || !MatchesBossRune(r.Get(f), i.Value, a.Value))
                throw new InvalidOperationException("GetRune readback failed in current frame");
            return r;
        }

        private static void BeforeBossHeroLoad(Frame __0)
        {
            try
            {
                if (!_enabled || !_bossEnabled) return;
                var session = BossSessionFor(__0);
                if (session?.ReadLogged == true) return;
                if (session != null) session.ReadLogged = true;
                else if (Interlocked.Increment(ref _bossReadWithoutSession) > 1) return;
                var states = new List<string>();
                foreach (var rule in _bossRules)
                {
                    string state = rule.Enabled ? "not-ready" : "disabled";
                    if (session?.Runes.TryGetValue(rule.ItemGuid, out var rune) == true)
                    {
                        try { ReadBossRune(__0, rune); state = rune.ViewReady ? "sim+view-ready" : "sim-ready"; }
                        catch (Exception e) { state += " (" + e.Message + ")"; }
                    }
                    states.Add(rule.Key + "=" + BossId(rule.ItemGuid) + ":" + state);
                }
                Write("boss-runes READ frame=" + __0?.Pointer + " storage=" + __0?.InstanceDataStorage?.Pointer + "; " + string.Join(", ", states));
            }
            catch (Exception e) { BossError("READ", e); }
        }

        private static int _bossReadWithoutSession;

        private static unsafe bool BeforeBossCount(Frame __0, EntityRef __1, RaiseMinionActionData __instance, ref int __result)
        {
            try
            {
                if (!_enabled || !_bossEnabled || __instance == null || __0 == null) return true;
                long guid = __instance.Guid.Value;
                var rune = BossAction(BossSessionFor(__0), guid);
                if (rune == null || rune.Action.Pointer != __instance.Pointer) return true;
                int count = 0;
                foreach (var pair in __0.GetComponentIterator<RaisedMinionComponent>())
                {
                    var entity = pair.Entity;
                    if (!entity.IsValid || !__0.Exists(entity)) continue;
                    var minion = EntityRefExtensions.Get<RaisedMinionComponent>(entity, __0);
                    if (minion == null || minion->Owner.Raw != __1.Raw || minion->RaiseActionGuid.Value != guid ||
                        EntityRefExtensions.Has<PendingDestructionComponent>(entity, __0) || !EntityRefExtensions.Has<HealthComponent>(entity, __0)) continue;
                    var health = EntityRefExtensions.Get<HealthComponent>(entity, __0);
                    if (health != null && (health->m_infiniteEnabled.Value != 0 || health->m_amount.RawValue > 0)) ++count;
                }
                __result = count;
                if (count >= rune.Rule.MaxActive && __0.IsVerified && Interlocked.Increment(ref rune.CountLogs) <= 3)
                    Write(BossLabel(rune.Rule) + " CountActiveMinions BLOCK count/max=" + count + "/" + rune.Rule.MaxActive);
                return false;
            }
            catch (Exception e) { BossError("CountActiveMinions", e); return true; }
        }

        private static void LogBossExecute(Frame f, EntityRef caster, RaiseMinionActionData action, bool result)
        {
            if (!_bossEnabled || f == null || !f.IsVerified || action == null || !IsPrivateBossAction(action.Guid.Value)) return;
            var rune = BossAction(BossSessionFor(f), action.Guid.Value);
            if (rune != null && Interlocked.Increment(ref rune.ExecuteLogs) <= 6)
                Write(BossLabel(rune.Rule) + " Execute A=" + BossId(rune.Rule.ActionGuid) + " caster=" + caster.Raw + " -> " + result);
        }

        private static void TrackBossSpawn(EntityRef entity, AssetGuid action)
        {
            if (!IsPrivateBossAction(action.Value)) return;
            var session = _bossSession;
            var rune = BossAction(session, action.Value);
            if (rune != null && rune.SpawnLogs < 3 && session.Spawns.Count < 64)
                session.Spawns[(entity.Raw, action.Value)] = entity;
        }

        private static unsafe void LogBossSpawns(Frame f, BossSession session)
        {
            if (session.Spawns.Count == 0) return;
            try
            {
                foreach (var spawn in session.Spawns)
                {
                    var rune = BossAction(session, spawn.Key.Action);
                    if (rune == null || rune.SpawnLogs >= 3 || !EntityRefExtensions.Has<RaisedMinionComponent>(spawn.Value, f)) continue;
                    var minion = EntityRefExtensions.Get<RaisedMinionComponent>(spawn.Value, f);
                    if (minion == null || minion->RaiseActionGuid.Value != spawn.Key.Action) continue;
                    ++rune.SpawnLogs;
                    Write(BossLabel(rune.Rule) + " RaisedMinionSpawned A=" + BossId(spawn.Key.Action) + " npc=" + MinionNpcGuid(f, spawn.Value));
                }
            }
            finally { session.Spawns.Clear(); }
        }

        private static void TickBossRunes(Frame f)
        {
            try
            {
                var session = BossSessionFor(f);
                if (session == null) return;
                LogBossSpawns(f, session);
                bool tomePending = !session.TomeAttempted && !string.IsNullOrWhiteSpace(_bossTomeCharacter) &&
                    session.Runes.Count > 0 && session.Runes.Values.All(r => r.ViewReady);
                bool loosePending = !string.IsNullOrWhiteSpace(_targetCharacter) && session.Runes.Values.Any(r => r.ViewReady && !r.GrantAttempted);
                if (!tomePending && !loosePending) return;
                var hero = new EntityRef { Raw = unchecked((ulong)Interlocked.Read(ref _heroRaw)) };
                if (!hero.IsValid || !InventoryAPI.GetInventoryEntity(f, hero).IsValid) { session.HeroFrames = 0; return; }
                if (session.HeroFrames < NecromancyPolicy.GrantSettleFrames && ++session.HeroFrames < NecromancyPolicy.GrantSettleFrames) return;
                string name = HeroName(f, hero);
                if (tomePending && NecromancyPolicy.IsTargetCharacter(_bossTomeCharacter, name)) GrantBossTome(f, hero, name, session);
                if (!loosePending || !NecromancyPolicy.IsTargetCharacter(_targetCharacter, name)) return;
                foreach (var rune in session.Runes.Values)
                {
                    if (!rune.ViewReady || rune.GrantAttempted) continue;
                    rune.GrantAttempted = true;
                    var rule = rune.Rule;
                    if (BossRunePolicy.HasGrant(_bossGranted, name, rule.Key)) continue;
                    try
                    {
                        var runeRef = ReadBossRune(f, rune);
                        var id = new AssetRefHeroItemData { Id = BossGuid(rule.ItemGuid) };
                        int count = InventoryAPI.GetItemMatchingCount(f, hero, id, true);
                        if (count > 0 || HasSocketedBossRune(f, hero, runeRef))
                            Write(BossLabel(rule) + " GRANT skip: already owned by '" + name + "'");
                        else
                        {
                            InventoryAPI.PreviewAwardItemImmediate(f, hero, id, 1, NewInventoryItemSource.Loot);
                            Write(BossLabel(rule) + " GRANT '" + name + "'; loose count 0 -> " + InventoryAPI.GetItemMatchingCount(f, hero, id, true));
                        }
                        _bossGranted = BossRunePolicy.AddGrant(_bossGranted, name, rule.Key);
                        Interlocked.Exchange(ref _bossGrantDirty, 1);
                    }
                    catch (Exception e) { Write(BossLabel(rule) + " GRANT FAILED " + e.GetType().Name + ": " + e.Message); }
                }
            }
            catch (Exception e) { BossError("tick", e); }
        }

        // One-time: a new Gold Tome of Healing with every ready Boss rune written into its slots.
        private static void GrantBossTome(Frame f, EntityRef hero, string name, BossSession session)
        {
            session.TomeAttempted = true;
            if (BossRunePolicy.HasGrant(_bossGranted, name, BossRunePolicy.TomeGrantKey)) return;
            try
            {
                var runes = session.Runes.Values.OrderBy(r => r.Rule.Slot).ToArray();
                var keys = runes.Select(r => r.Rule.Key).ToArray();
                var indices = runes.Select(r => ReadBossRune(f, r).m_index).ToArray();
                var tomeData = Asset<HeroItemData>(f, NecromancyPolicy.TomeOfHealing) ?? throw new InvalidOperationException("Tome of Healing data missing");
                var tome = ItemsAPI.Create(f, tomeData, 1, ItemRarity.Gold, NewInventoryItemSource.Loot);
                bool added = tome.IsValid && InventoryAPI.DirectlyAddItemToInventoryOrDropAsBag(f, hero, tome, NewInventoryItemSource.Loot, false);
                Write("boss-runes TOME created for '" + name + "' entity=" + tome.Raw + " added=" + added);
                if (!added) return;
                int placed = WriteTomeRunes(f, hero, tome, keys, indices);
                Write("boss-runes TOME '" + name + "' runes placed " + placed + "/" + runes.Length);
                _bossGranted = BossRunePolicy.AddGrant(_bossGranted, name, BossRunePolicy.TomeGrantKey);
                Interlocked.Exchange(ref _bossGrantDirty, 1);
            }
            catch (Exception e) { Write("boss-runes TOME FAILED " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool HasSocketedBossRune(Frame f, EntityRef hero, RuneDataRef rune)
        {
            bool HasRune(EntityRef item) => item.IsValid && EntityRefExtensions.Has<WeaponComponent>(item, f) &&
                RunesAPI.Weapon.TryFindRune(f, item, rune, out _);
            foreach (var entry in InventoryAPI.GetInventoryItems(f, hero, false, true))
                if (HasRune(entry.Entity)) return true;
            foreach (var slot in new[] { EquipmentSlot.LeftHand1, EquipmentSlot.LeftHand2, EquipmentSlot.LeftHand3,
                         EquipmentSlot.RightHand1, EquipmentSlot.RightHand2, EquipmentSlot.RightHand3 })
                if (HasRune(EquipmentAPI.GetItemEntity(f, slot, hero))) return true;
            return false;
        }

        private static void SaveBossGrants()
        {
            try
            {
                if (Interlocked.Exchange(ref _bossGrantDirty, 0) == 0) return;
                _bossGrantedEntry.Value = _bossGranted;
                MelonPreferences.Save();
                Write("granted_boss_runes = \"" + _bossGrantedEntry.Value + "\" saved");
            }
            catch (Exception e) { BossError("grant save", e); }
        }
    }
}
