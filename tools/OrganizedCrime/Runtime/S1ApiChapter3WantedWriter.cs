using S1API.Entities;
using S1API.Law;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74: the game write behind <see cref="IChapter3WantedWriter"/>. Sets the local player
/// to Arresting through S1API's law manager and reads the level back. Arresting is the only
/// level this seam can write; anything else needs a new seam. Total: false when the player
/// is missing or the set faults, never throws.
/// </summary>
public sealed class S1ApiChapter3WantedWriter : IChapter3WantedWriter
{
    private readonly Action<string> _warn;
    private bool _disposed;

    public S1ApiChapter3WantedWriter(Action<string> warn)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public bool TrySetArresting(out int readBackLevel)
    {
        readBackLevel = (int)PursuitLevel.None;
        if (_disposed)
            return false;
        try
        {
            var player = Player.Local;
            if (player is null || player.CrimeData is null)
                return false;
            LawManager.SetWantedLevel(player, PursuitLevel.Arresting);
            readBackLevel = (int)player.CrimeData.CurrentPursuitLevel;
            return true;
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 wanted set failed closed: {exception.GetType().Name}.");
            readBackLevel = (int)PursuitLevel.None;
            return false;
        }
    }

    public void Dispose() => _disposed = true;
}
