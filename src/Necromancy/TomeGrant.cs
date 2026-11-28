using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppQuantum;
using MelonLoader;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private static MelonPreferences_Entry<string> _grantedEntry;
        private static MelonPreferences_Entry<string> _targetEntry;
        private static string _targetCharacter = "";
        private static volatile bool _grantRequested, _grantDone;
        private static int _heroFrames, _compatLogs;
        private static ulong _lastRejectedHero;
        private static volatile string _grantedName = "";

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
            try { LogRuneTable(f); } catch (Exception e) { Write("rune-table ERROR " + e.GetType().Name + ": " + e.Message); }
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

        // ---- tome rune compatibility ------------------------------------------------------

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
    }
}
