#nullable enable
namespace BowgunRepair;

internal readonly record struct ShotBinding(ulong Hero, ulong Item, long HeroView, long WeaponView, long ActionView)
{
    internal bool Matches(ShotBinding current) => Hero != 0 && Item != 0 && HeroView != 0 &&
        WeaponView != 0 && ActionView != 0 && this == current;
}

internal static class RestorePolicy
{
    internal static bool CanRestore(bool explicitBoundary, bool currentAction, bool playingOrPaused) =>
        explicitBoundary || (!currentAction && !playingOrPaused);
}

internal interface IRestorableField
{
    bool Pending { get; }
    bool Restore();
    void Forget();
}

// Own only our replacement, not the field itself. Engine-facing accessors are
// supplied by the adapter; tests exercise the same ownership/rollback code.
internal sealed class OwnedField<T> : IRestorableField
{
    private readonly Func<T> _get;
    private readonly Action<T> _set;
    private readonly Func<T,T,bool> _same;
    private readonly T _original, _replacement;
    public bool Pending { get; private set; }
    internal OwnedField(Func<T> get, Action<T> set, T replacement, Func<T,T,bool>? same = null)
    { _get=get; _set=set; _same=same ?? EqualityComparer<T>.Default.Equals; _original=get(); _replacement=replacement; }
    internal void Apply() { Pending=true; _set(_replacement); }
    public bool Restore()
    {
        if (!Pending) return false;
        var current = _get();
        bool collision = !_same(current,_replacement) && !_same(current,_original);
        if (_same(current,_replacement)) _set(_original);
        Pending=false;
        return collision;
    }
    public void Forget() => Pending=false;
}
