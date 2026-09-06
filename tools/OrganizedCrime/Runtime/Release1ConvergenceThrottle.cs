namespace OrganizedCrime.Runtime;

/// <summary>
/// The once per game minute gate a host mission service puts in front of its convergence pass. Owned by
/// the host, never by the decision engine: which pass is expensive enough to throttle, and which
/// sibling reconcilers in the same pass react to player actions rather than to game minutes, is a
/// property of the mission, not of the window arithmetic.
///
/// The gate is safe because every threshold a windowed condition compares is a game minute against a
/// stored game minute, so one pass per game minute observes every state the table can distinguish and a
/// second pass inside the same minute can only reach the identical decision.
///
/// A clock that cannot be read fails open, so the pass behaves exactly as it did before the gate
/// existed whenever canonical game time is unavailable. Every lifecycle boundary calls Clear so a save
/// start, a save complete, a pre load, a load complete, and any accepted player decision always
/// converge on their own pass (OC-63).
/// </summary>
public sealed class Release1ConvergenceThrottle
{
    private double? _lastGameMinute;
    private long _lastRevision = -1;

    /// <summary>
    /// True when this pass should run. Pass null for gameMinutes when canonical game time could not be
    /// read this pass.
    /// </summary>
    public bool TryBegin(double? gameMinutes, long revision)
    {
        if (gameMinutes is not { } minutes)
        {
            _lastGameMinute = null;
            _lastRevision = revision;
            return true;
        }

        var minute = Math.Floor(minutes);
        if (_lastGameMinute is not null && _lastGameMinute.Value == minute && _lastRevision == revision)
            return false;

        _lastGameMinute = minute;
        _lastRevision = revision;
        return true;
    }

    /// <summary>
    /// Clears the stamp so the next pass always begins. Deliberately leaves the last seen revision
    /// alone: a null minute already short circuits the comparison, and this is the exact behavior the
    /// five shipped boundary sites had when they assigned null to the minute field directly.
    /// </summary>
    public void Clear() => _lastGameMinute = null;
}
