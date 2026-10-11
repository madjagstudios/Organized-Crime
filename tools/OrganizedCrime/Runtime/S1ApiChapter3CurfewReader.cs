using S1API.Law;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74: the game read behind <see cref="IChapter3CurfewReader"/>, over S1API's own
/// curfew singleton, which null-guards the native manager itself. Total: false when
/// unreadable, never throws.
/// </summary>
public sealed class S1ApiChapter3CurfewReader : IChapter3CurfewReader
{
    private readonly Action<string> _warn;
    private bool _disposed;

    public S1ApiChapter3CurfewReader(Action<string> warn)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public bool TryReadActive(out bool active)
    {
        active = false;
        if (_disposed)
            return false;
        try
        {
            active = CurfewManager.IsCurrentlyActive;
            return true;
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 curfew read failed closed: {exception.GetType().Name}.");
            active = false;
            return false;
        }
    }

    public void Dispose() => _disposed = true;
}
