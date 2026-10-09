#nullable enable
namespace BowgunRepair;

internal static class RepairPolicy
{
    internal const long Action = 3675416429875070644;
    internal const long Armament = 3819488842592029946;
    internal const string CastGuid = "7e06a949-58fc-4ca8-96c1-2d007370f212";
    internal static bool CanMute(long action, long armament, bool local, int record, string? guid, float start, float end) =>
        action == Action && armament == Armament && local && record == 2099 && guid == CastGuid &&
        float.IsFinite(start) && float.IsFinite(end) && Math.Abs(start-.1f) < .0001f && Math.Abs(end-1.0333334f) < .0001f;
}

internal static class NativeAudioPolicy
{
    internal const long WeaponData = 1600292959367603929;
    internal const string RaiseGuid = "a24e0644-5be6-438b-beff-72a7f2ae745a";
    internal const string FireGuid = "7a450780-2dfd-4dad-8cf5-22321acc9e8f";
    internal const uint RaiseId = 2929132925, FireId = 1750879647;
    internal const string OldRaiseGuid = "93c922f4-46f6-485a-bf2a-4696c2a6bcd8";
    internal static bool NeedsCompanion(string? prefab, long weapon, bool hasCompanion) =>
        !hasCompanion && (weapon == 0 || weapon == WeaponData) &&
        prefab is "reinforcedBowgunView" or "reinforcedBowgunView(Clone)";
    internal static bool CanReplaceRaise(string? guid, int record, float start, float end) =>
        guid == OldRaiseGuid && record == 1441 && float.IsFinite(start) && float.IsFinite(end) &&
        Math.Abs(start-.0166667f) < .0001f && Math.Abs(end-.6f) < .0001f;
}

internal sealed class StartupPolicy
{
    private ulong _hero, _item;
    private long _view, _next;
    internal int Attempts { get; private set; }
    internal bool Decide(long now, ulong hero, ulong item, long view, bool loaded, bool targetPending, bool anyPending, bool shooting)
    {
        if (hero == 0 || item == 0 || view == 0) return false;
        if (_hero != hero || _item != item || _view != view)
        { _hero = hero; _item = item; _view = view; Attempts = 0; _next = now+1000; }
        // Any pending work includes unrelated items/utility views. Only the
        // exact action's request can justify waiting for this dependency.
        return !loaded && !targetPending && !shooting && Attempts < 3 && now >= _next;
    }
    internal void Requested(long now) { ++Attempts; _next = now+5000; }
    internal void Reset() { _hero = _item = 0; _view = _next = 0; Attempts = 0; }
}

internal sealed class ReleasePolicy
{
    private ulong _hero, _item;
    private bool _armed;
    private readonly HashSet<ulong> _existing = new();
    internal void Begin(ulong hero, ulong item, IEnumerable<ulong> existing)
    {
        _hero = hero; _item = item; _armed = hero != 0 && item != 0;
        _existing.Clear();
        foreach (var entity in existing) _existing.Add(entity);
    }
    internal bool Claim(ulong entity, ulong owner, ulong weapon, bool released)
    {
        if (!_armed || entity == 0 || !released || owner != _hero || weapon != _item || _existing.Contains(entity)) return false;
        _armed = false;
        return true;
    }
    internal void Cancel() { _armed = false; }
}
