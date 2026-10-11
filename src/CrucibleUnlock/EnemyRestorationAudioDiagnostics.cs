using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppMoon.Forsaken;
using UnityEngine;

namespace CrucibleUnlock
{
    // Observes the native item callback after it returns. This never posts a
    // sound, changes a view, or alters the callback's result. Values indicate
    // that references are connected; they cannot establish audible playback.
    internal static class EnemyRestorationAudioDiagnostics
    {
        private const int ProfileCount = 10;
        private static int[] _reported = new int[ProfileCount];
        private static int _reportedError;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ObserveEquipment(HumanoidEnemyView owner, GameObject clone,
            EnemyItemDataBaseAsset item, bool mainHand, EnemyRestorationCatalog catalog)
        {
            if (!mainHand || !EnemyRestorationRuntime.CurrentViewsReady ||
                ReferenceEquals(owner, null) || ReferenceEquals(clone, null) ||
                ReferenceEquals(item, null) || ReferenceEquals(catalog, null))
                return;
            try
            {
                if (owner.Pointer == IntPtr.Zero || clone.Pointer == IntPtr.Zero || item.Pointer == IntPtr.Zero ||
                    !EnemyRestorationOwnerScope.TryGetProfile(owner, catalog, out var profile) ||
                    profile == null || profile.Index < 0 || profile.Index >= ProfileCount ||
                    catalog.Profiles == null || catalog.Profiles.Length != ProfileCount ||
                    !ReferenceEquals(catalog.Profiles[profile.Index], profile))
                    return;

                long itemGuid = item.AssetGuid.Value;
                bool privateWeapon = profile.BlockedActions != null && profile.BlockedActions.Length != 0;
                if (itemGuid != profile.WeaponGuid &&
                    (!privateWeapon || itemGuid != profile.PrivateWeaponGuid))
                    return;

                // A postfix can observe an unrelated or incomplete item callback.
                // Read only the clone that native Equip retained as the main hand.
                var cloneWeapon = clone.GetComponent(Il2CppType.Of<EnemyWeaponView>())?.TryCast<EnemyWeaponView>();
                var mainWeapon = owner.MainWeaponView;
                if (ReferenceEquals(cloneWeapon, null) || ReferenceEquals(mainWeapon, null) ||
                    cloneWeapon.Pointer == IntPtr.Zero || mainWeapon.Pointer != cloneWeapon.Pointer)
                    return;

                long boundWeaponGuid = cloneWeapon.WeaponData.Id.Value;
                // Private wrappers retain the shipped weapon prefab; its
                // native WeaponData binding must still name that source.
                if (boundWeaponGuid != profile.WeaponGuid) return;
                var companion = cloneWeapon.AudioCompanion;
                var weaponHost = companion?.WeaponSoundHost;
                var audioOwner = companion?.Owner;
                var characterHost = owner.PrimarySoundHost;
                var attackEvent = companion?.AttackEvent;
                bool ownerMatch = !ReferenceEquals(audioOwner, null) &&
                    audioOwner.Pointer != IntPtr.Zero && audioOwner.Pointer == owner.Pointer;

                var reported = Volatile.Read(ref _reported);
                if (Interlocked.CompareExchange(ref reported[profile.Index], 1, 0) != 0) return;
                TrialRepairLog.Write("[enemy-audio] equipment profile=" + profile.Key +
                    "; itemGuid=" + itemGuid + "; weaponGuid=" + boundWeaponGuid +
                    "; actor=" + Hex(owner.Pointer) + "; clone=" + Hex(clone.Pointer) +
                    "; mainWeapon=" + Hex(mainWeapon.Pointer) +
                    "; companion=" + Hex(companion?.Pointer ?? IntPtr.Zero) +
                    "; weaponHost=" + Hex(weaponHost?.Pointer ?? IntPtr.Zero) +
                    "; characterHost=" + Hex(characterHost?.Pointer ?? IntPtr.Zero) +
                    "; audioOwner=" + Hex(audioOwner?.Pointer ?? IntPtr.Zero) +
                    "; ownerMatch=" + ownerMatch +
                    "; attackEvent=" + Hex(attackEvent?.Pointer ?? IntPtr.Zero) +
                    "; presence-only=true");
            }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref _reportedError, 1) != 0) return;
                try { TrialRepairLog.Write("[enemy-audio] observation failed: " + error); }
                catch { /* Diagnostics cannot change the native callback. */ }
            }
        }

        // Called during resolver reset; no Unity or IL2CPP object is read here.
        internal static void StopWithoutUnityReads()
        {
            Volatile.Write(ref _reported, new int[ProfileCount]);
            Interlocked.Exchange(ref _reportedError, 0);
        }

        private static string Hex(IntPtr pointer) => "0x" + pointer.ToInt64().ToString("X");
    }
}
