using S1API.Law;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-134: the game reads behind <see cref="IChapter3ArthurVisitConditions"/>.
/// OC-74: delegates the player reads to the shared <see cref="IChapter3PlayerExposureReader"/>
/// (on foot, outdoors, pursuit level), mapping them onto the visit conditions. Clear of
/// pursuit is still a None level, which covers wanted, investigating, and lethal in
/// the one check the field contact's own mapping already proved (see
/// <c>S1ApiRelease1FieldContactRuntime</c>).
/// Every entry point is total: any fault reads as blocked, never throws.
/// </summary>
public sealed class S1ApiChapter3ArthurVisitConditions : IChapter3ArthurVisitConditions
{
    private readonly Action<string> _warn;
    private readonly IChapter3PlayerExposureReader _exposure;
    private bool _disposed;

    public S1ApiChapter3ArthurVisitConditions(Action<string> warn, IChapter3PlayerExposureReader? exposure = null)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _exposure = exposure ?? new S1ApiChapter3PlayerExposureReader(warn);
    }

    public bool TryRead(out Chapter3ArthurVisitConditions conditions)
    {
        conditions = Chapter3ArthurVisitConditions.Blocked(false, false, false, "the visit conditions were unreadable.");
        if (_disposed)
            return false;
        try
        {
            if (!_exposure.TryRead(out var exposure))
            {
                conditions = Chapter3ArthurVisitConditions.Blocked(false, false, false, exposure.Detail);
                return false;
            }
            var clear = exposure.PursuitLevel == (int)PursuitLevel.None;
            conditions = exposure.OnFoot && exposure.Outdoors && clear
                ? Chapter3ArthurVisitConditions.Clear()
                : Chapter3ArthurVisitConditions.Blocked(exposure.OnFoot, exposure.Outdoors, clear, exposure.Detail);
            return true;
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 Arthur visit conditions failed closed: {exception.GetType().Name}.");
            conditions = Chapter3ArthurVisitConditions.Blocked(false, false, false, "the visit conditions threw.");
            return false;
        }
    }

    public void Dispose() => _disposed = true;
}
