namespace OrganizedCrime.Core.Scheduling;

/// <summary>
/// Wall-clock next-at throttle: a pass goes through when the clock has
/// reached the armed moment, then re-arms itself one interval later. A fresh
/// gate passes at any <paramref name="nowSeconds"/> at or after zero and
/// throttles negative readings, mirroring the zeroed stamp field it
/// replaces. Stores and adds in float, matching the original
/// <c>now + interval</c> expression order, so every boundary rounds exactly
/// like the stamp field did. Not thread-safe: callers already hold their own
/// locks.
/// </summary>
public sealed class IntervalGate
{
    private readonly float _intervalSeconds;
    private float _nextPassAt;

    public IntervalGate(float intervalSeconds)
    {
        if (!float.IsFinite(intervalSeconds) || intervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "The interval must be a finite positive number of seconds.");
        _intervalSeconds = intervalSeconds;
        _nextPassAt = 0f;
    }

    /// <summary>
    /// True when this pass should run: <paramref name="nowSeconds"/> has
    /// reached the armed moment. A passing call re-arms the gate to
    /// <paramref name="nowSeconds"/> plus the interval; a throttled call
    /// leaves the armed moment alone.
    /// </summary>
    public bool TryPass(float nowSeconds)
    {
        if (nowSeconds < _nextPassAt) return false;
        _nextPassAt = nowSeconds + _intervalSeconds;
        return true;
    }

    /// <summary>
    /// Restores the fresh state: the next pass is allowed at any now at or
    /// after zero.
    /// </summary>
    public void Reset() => _nextPassAt = 0f;

    /// <summary>
    /// Re-arms the gate so the next pass is allowed at
    /// <paramref name="nowSeconds"/>: an earlier reading throttles, that
    /// reading passes and re-arms one interval later.
    /// </summary>
    public void Reset(float nowSeconds) => _nextPassAt = nowSeconds;
}
