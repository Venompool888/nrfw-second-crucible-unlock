using System;
using CrucibleUnlock;

internal static class BowgunInputPolicyTests
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL bowgun: " + name);
    }

    internal static void Run()
    {
        const long bowgun = BowgunInputPolicy.ArmamentGuid;
        Check(BowgunInputPolicy.ShouldRoute(bowgun, true, true, true), "LB with equipped bowgun routes a shot");
        Check(!BowgunInputPolicy.ShouldRoute(bowgun + 1, true, true, true), "other offhands retain native LB");
        Check(!BowgunInputPolicy.ShouldRoute(0, true, true, true), "empty offhand retains native LB");
        Check(!BowgunInputPolicy.ShouldRoute(bowgun, false, true, true), "menus cannot fire");
        Check(!BowgunInputPolicy.ShouldRoute(bowgun, true, false, true), "keyboard input is unchanged");
        Check(!BowgunInputPolicy.ShouldRoute(bowgun, true, true, false), "released LB does not inject attack");
        const int unrelated = 0x10000000 | 0x80 | 0x100;
        const int runeModifiers = 0x0f | 0x7800 | 0x20000 | 0x40000000 | 0x0f000000;
        Check(BowgunInputPolicy.ShotModifiers(unrelated | runeModifiers) == (unrelated | 2048),
            "clear conflicting rune selection bits, preserve parry/equipment/movement flags");
        Check(BowgunInputPolicy.ShotModifiers(0) == 2048, "one default offhand action");
        Check(BowgunInputPolicy.ShotModifiers(2048) == 2048, "repeated input polling is idempotent");
        const long bowgunWeapon = BowgunInputPolicy.WeaponGuid;
        Check(BowgunInputPolicy.ShouldProvideSpecialCombo(bowgunWeapon, 0, true), "bowgun offhand special slot 0 is supplied during a routed shot");
        Check(BowgunInputPolicy.ShouldProvideSpecialCombo(bowgunWeapon, BowgunInputPolicy.MaxSpecialSlot, true),
            "last bowgun offhand special slot is supplied during a routed shot");
        Check(!BowgunInputPolicy.ShouldProvideSpecialCombo(bowgunWeapon, 0, false),
            "weapon view setup and rune queries outside the routed window stay native");
        Check(!BowgunInputPolicy.ShouldProvideSpecialCombo(bowgunWeapon, BowgunInputPolicy.MaxSpecialSlot + 1, true),
            "out-of-range special slot keeps the native combo");
        Check(!BowgunInputPolicy.ShouldProvideSpecialCombo(bowgunWeapon, -1, true), "negative special slot keeps the native combo");
        Check(!BowgunInputPolicy.ShouldProvideSpecialCombo(bowgunWeapon + 1, 0, true), "other weapons keep their own special combos");
        Check(!BowgunInputPolicy.ShouldProvideSpecialCombo(0, 0, true), "empty weapon guid keeps the native combo");
        Check(BowgunInputPolicy.ShouldWriteWeaponClass(30, 0), "the shipped None class is replaced once");
        Check(!BowgunInputPolicy.ShouldWriteWeaponClass(30, 30), "an already applied class is not rewritten");
        Check(!BowgunInputPolicy.ShouldWriteWeaponClass(30, 21), "an unexpected class value is left untouched");
        Check(!BowgunInputPolicy.ShouldWriteWeaponClass(0, 0), "class writing can be disabled");
        Console.WriteLine("PASS: 20 bowgun input policy checks; no game assemblies loaded.");
    }
}
