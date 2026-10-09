namespace BowgunRepair;

internal enum ControlStyle { None, Gamepad, KeyboardMouse }

internal static class BlockInputPolicy
{
    internal static bool ShouldRoute(long armament, bool gameplay, bool localBound, bool block, ControlStyle style) =>
        armament == RepairPolicy.Armament && gameplay && localBound && block &&
        style is ControlStyle.Gamepad or ControlStyle.KeyboardMouse;
    internal static bool ShouldHideHudBlock(bool localBowgunScope, bool blockResult, bool blockAction, ControlStyle style) =>
        localBowgunScope && blockResult && blockAction && style is ControlStyle.Gamepad or ControlStyle.KeyboardMouse;
}
