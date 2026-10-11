namespace OrganizedCrime.Model;

internal enum Chapter3RaidPresence { Unknown, Inside, Near, Clear }
internal sealed record Chapter3RaidPlayer(bool Known, bool CurrentPropertyMatches, bool InsideBounds, double EntranceDistance);
internal sealed record Chapter3RaidAway(Chapter3RaidPresence Presence, double AwayMinutes, bool CanRaid);

internal static class Chapter3RaidOccupancy
{
    internal const double NearMetres = 50d;
    internal static bool SupportsCensus(int registryCount, int connectionCount, int lobbyCount) =>
        registryCount == 1 && connectionCount == 1 && lobbyCount == 1;
    internal static Chapter3RaidPresence Classify(bool censusKnown, IReadOnlyList<Chapter3RaidPlayer> players,
        bool hqInside = false, bool hqTransition = false)
    {
        if (!censusKnown || hqTransition || players.Count == 0 ||
            players.Any(p => !p.Known || !double.IsFinite(p.EntranceDistance) || p.EntranceDistance < 0))
            return Chapter3RaidPresence.Unknown;
        if (hqInside || players.Any(p => p.CurrentPropertyMatches || p.InsideBounds)) return Chapter3RaidPresence.Inside;
        return players.Any(p => p.EntranceDistance <= NearMetres) ? Chapter3RaidPresence.Near : Chapter3RaidPresence.Clear;
    }
}

// Sampling is conservative: a missed minute, clock skip, or failed observation
// invalidates continuity. Polling alone cannot prove native geometry or census.
internal sealed class Chapter3RaidAwayTracker
{
    internal const double RequiredMinutes = 10d;
    private readonly Dictionary<string, (double Since, double Last)> _away = new(StringComparer.Ordinal);

    internal Chapter3RaidAway Observe(string code, double? minutes, Chapter3RaidPresence presence)
    {
        if (!minutes.HasValue || !double.IsFinite(minutes.Value) || minutes < 0 || presence != Chapter3RaidPresence.Clear)
        {
            _away.Remove(code);
            return new(minutes.HasValue && double.IsFinite(minutes.Value) && minutes >= 0 ? presence : Chapter3RaidPresence.Unknown, 0, false);
        }
        var now = minutes.Value;
        if (!_away.TryGetValue(code, out var previous) || now < previous.Last || now - previous.Last > 1d)
            previous = (now, now);
        _away[code] = (previous.Since, now);
        var elapsed = now - previous.Since;
        return new(presence, elapsed, elapsed >= RequiredMinutes);
    }

    internal void Retain(IReadOnlySet<string> codes)
    {
        foreach (var code in _away.Keys.Where(code => !codes.Contains(code)).ToArray()) _away.Remove(code);
    }
    internal void Reset() => _away.Clear();
}
