using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

internal interface IChapter3RaidPollingWorld
{
    IReadOnlyList<Chapter3RaidSite> ReadForPoll(Chapter3RaidExclusions exclusions, double? minutes);
}

// Cache raw stock, never occupancy or job exclusions. Explicit reads stay live.
internal sealed class Chapter3RaidScanCache
{
    internal static T ScanGuids<T>(Dictionary<string, HashSet<Guid>> cache, string code, Func<HashSet<Guid>, T> scan)
    {
        var seen = new HashSet<Guid>();
        var result = scan(seen);
        cache[code] = seen;
        return result;
    }

    private sealed record Entry(long Identity, double Minute, Chapter3RaidSite Stock);
    private readonly Dictionary<string, Entry> _stock = new(StringComparer.Ordinal);
    internal void Reset() => _stock.Clear();
    internal Chapter3RaidSite Read(string code, long identity, double? minutes, bool force,
        Func<Chapter3RaidSite> scan, Chapter3RaidPresence presence, Chapter3RaidExclusions exclusions)
    {
        if (minutes is not { } now || !Chapter3Sweep.ValidMinutes(now))
        {
            _stock.Remove(code);
            // Preview may explicitly scan with an unavailable clock; occupancy stays blocked.
            if (!force) return new(code, code, false, Array.Empty<Chapter3RaidSlot>(), presence);
            return Filter(scan(), presence, exclusions);
        }
        if (force || !_stock.TryGetValue(code, out var entry) || entry.Identity != identity ||
            now < entry.Minute || Math.Floor(now / 60d) != Math.Floor(entry.Minute / 60d))
        {
            Chapter3RaidSite stock;
            try { stock = scan(); }
            catch { stock = new(code, code, false, Array.Empty<Chapter3RaidSlot>(), presence); }
            entry = new(identity, now, stock);
            _stock[code] = entry;
        }
        return Filter(entry.Stock, presence, exclusions);
    }
    private static Chapter3RaidSite Filter(Chapter3RaidSite site, Chapter3RaidPresence presence, Chapter3RaidExclusions exclusions) =>
        new(site.Code, site.Name, site.StockKnown,
            site.Slots.Where(slot => Chapter3RaidEligibility.Allows(slot, exclusions)).ToArray(), presence);
    internal void Retain(IReadOnlySet<string> codes)
    {
        foreach (var code in _stock.Keys.Where(code => !codes.Contains(code)).ToArray()) _stock.Remove(code);
    }
}

// A GUID can be reused after a storage is removed; the native pointer also matters.
internal sealed class Chapter3RaidWrapperCache<T> where T : class
{
    private readonly Dictionary<Guid, (long Pointer, T Wrapper)> _wrappers = new();
    internal T Get(Guid guid, long pointer, Func<T> create)
    {
        if (_wrappers.TryGetValue(guid, out var entry) && entry.Pointer == pointer) return entry.Wrapper;
        _wrappers.Remove(guid);
        var wrapper = create() ?? throw new InvalidOperationException("API storage unavailable.");
        _wrappers.Add(guid, (pointer, wrapper));
        return wrapper;
    }
    internal void Retain(IReadOnlySet<Guid> guids)
    {
        foreach (var guid in _wrappers.Keys.Where(guid => !guids.Contains(guid)).ToArray()) _wrappers.Remove(guid);
    }
    internal void Reset() => _wrappers.Clear();
}
