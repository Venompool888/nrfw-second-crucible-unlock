namespace CrucibleUnlock
{
    internal static class BowgunInputPolicy
    {
        internal const long ArmamentGuid = 3819488842592029946L;
        internal const long WeaponGuid = 1600292959367603929L;
        internal const long ActionGuid = 3675416429875070644L;
        internal const long ProjectileGuid = 3144285392061863817L;
        internal const int OffhandAttack1 = 2048;

        // HeroSpecialType 0..3 are the four offhand special slots; the offhand controller asks
        // WeaponStaticData.GetSpecialStart for one of them (native evidence:
        // research/bowgun-input-20261008/offhand-native.txt lines 8210/8239/8258/8277 use r9d = 3/2/1/0).
        internal const int MaxSpecialSlot = 3;

        internal static bool ShouldRoute(long armament, bool gameplay, bool gamepad, bool lb)
            => armament == ArmamentGuid && gameplay && gamepad && lb;

        internal static int ShotModifiers(int original)
            => (original & ~(0x0f | 0x7800 | 0x20000 | 0x40000000 | 0x0f000000)) | OffhandAttack1;

        // Only the reinforced bowgun, only its four special slots, and only while an LB shot
        // routed by this plugin is still in flight. The window matters: native GetSpecialCombo is
        // also called by unrelated paths (weapon view setup, rune selection, mainhand resolution),
        // and 0.9.10 crashed the runtime when it answered those calls too.
        internal static bool ShouldProvideSpecialCombo(long weaponGuid, int heroSpecialType, bool routedWindowActive)
            => routedWindowActive && weaponGuid == WeaponGuid && heroSpecialType >= 0 && heroSpecialType <= MaxSpecialSlot;

        // WeaponStaticData.Class lives at native offset 0x128 and feeds HeroView.WeaponClassParameter.
        // The reinforced bowgun ships with WeaponClass.None(0) while every real bow of the game
        // carries Greatbow(21); the interop type exposes no setter, so the field is written directly.
        internal const int ClassFieldOffset = 0x128;

        // Only ever write on top of the shipped value: if the field already holds anything else the
        // layout assumption is wrong and the weapon object must not be touched.
        internal static bool ShouldWriteWeaponClass(int configured, int current)
            => configured > 0 && current == 0;
    }
}
