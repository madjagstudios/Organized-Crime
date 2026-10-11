using OrganizedCrime.Core.Diagnostics;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74 amendment: the game read and write behind <see cref="IChapter3PursuitHold"/>.
/// Reads the local player's S1API crime data (level, sighted timer, search time); the write
/// drops to the native interop because S1API 3.2.1 exposes TimeSinceSighted get-only. The
/// native path is the static Player.Local singleton's CrimeData component reference, a plain
/// property hold, never a scene lookup. Total: false when the player or the crime data
/// cannot be read, never throws.
/// </summary>
public sealed class S1ApiChapter3PursuitHold : IChapter3PursuitHold
{
    private readonly Action<string> _warn;
    // Review fix: the hold runs every tick, so a persistent fault would warn every frame.
    // The adapter warns once per instance; the service keeps its own once-per-session line.
    private const string FaultKey = "pursuit-hold";
    private readonly OnceLog _reportedFaults;
    private bool _disposed;

    public S1ApiChapter3PursuitHold(Action<string> warn)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _reportedFaults = new OnceLog(_warn);
    }

    public bool TryRead(out int pursuitLevel, out float timeSinceSighted, out float searchTime)
    {
        pursuitLevel = Chapter3PursuitLevels.None;
        timeSinceSighted = 0f;
        searchTime = 0f;
        if (_disposed)
            return false;
        try
        {
            var crime = S1API.Entities.Player.Local?.CrimeData;
            if (crime is null)
                return false;
            pursuitLevel = (int)crime.CurrentPursuitLevel;
            timeSinceSighted = crime.TimeSinceSighted;
            searchTime = crime.GetSearchTime();
            return true;
        }
        catch (Exception exception)
        {
            WarnOnce($"Chapter 3 pursuit hold read failed closed: {exception.GetType().Name}.");
            pursuitLevel = Chapter3PursuitLevels.None;
            timeSinceSighted = 0f;
            searchTime = 0f;
            return false;
        }
    }

    public bool TryWriteTimeSinceSighted(float value)
    {
        if (_disposed)
            return false;
        try
        {
            var crime = Il2CppScheduleOne.PlayerScripts.Player.Local?.CrimeData;
            if (crime is null)
                return false;
            crime.TimeSinceSighted = value;
            return true;
        }
        catch (Exception exception)
        {
            WarnOnce($"Chapter 3 pursuit hold write failed closed: {exception.GetType().Name}.");
            return false;
        }
    }

    private void WarnOnce(string message)
    {
        _reportedFaults.Report(FaultKey, message);
    }

    public void Dispose() => _disposed = true;
}
