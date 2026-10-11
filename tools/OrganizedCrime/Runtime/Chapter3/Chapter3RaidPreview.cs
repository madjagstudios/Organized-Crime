using System.Globalization;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// Main-thread reads only, owned by the coordinator. No saved state or locks.
internal sealed class Chapter3RaidPreview
{
    private readonly IChapter3RaidWorld _world;
    private readonly Func<double?> _minutes;
    private readonly Func<Chapter3State?> _state;
    private readonly Chapter3RaidAwayTracker _tracker = new();
    private readonly Dictionary<string, Chapter3RaidAway> _occupancy = new(StringComparer.Ordinal);

    internal Chapter3RaidPreview(IChapter3RaidWorld world, Func<double?> minutes, Func<Chapter3State?> state)
    {
        _world = world;
        _minutes = minutes;
        _state = state;
    }

    internal void Reset()
    {
        _tracker.Reset(); _occupancy.Clear();
        try { _world.Reset(); } catch { }
    }
    internal void Poll() => Read(poll: true);

    private IReadOnlyList<Chapter3RaidSite>? Read(bool poll = false)
    {
        try
        {
            if (!_world.IsHost) { Reset(); return null; }
            var minutes = _minutes();
            var exclusions = Chapter3RaidExclusions.From(_state());
            var sites = poll && _world is IChapter3RaidPollingWorld polling
                ? polling.ReadForPoll(exclusions, minutes) : _world.Read(exclusions);
            var codes = sites.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
            if (codes.Count != sites.Count) { Reset(); return null; }
            _tracker.Retain(codes);
            _occupancy.Clear();
            foreach (var site in sites)
                _occupancy[site.Code] = _tracker.Observe(site.Code, minutes, site.Presence);
            return sites;
        }
        catch { Reset(); return null; }
    }

    internal IReadOnlyList<string> Preview(string? code)
    {
        try
        {
            if (!_world.IsHost) { Reset(); return new[] { "raid preview refused: host only." }; }
            var sites = Read();
            if (sites is null) return new[] { "raid preview: storage/census unavailable; unknown, blocked." };
            var lines = new List<string> { "raid preview (read only; nothing removed):" };
            foreach (var site in sites.OrderBy(s => s.Code, StringComparer.Ordinal))
            {
                var away = _occupancy[site.Code];
                var units = site.StockKnown ? site.TotalUnits.ToString(CultureInfo.InvariantCulture) : "unknown";
                lines.Add($"site={site.Code} ({site.Name}) eligible units={units}; occupancy={away.Presence.ToString().ToLowerInvariant()}; away minutes={away.AwayMinutes.ToString("0.##", CultureInfo.InvariantCulture)}; clear={away.CanRaid}");
            }
            var picked = code is null ? Chapter3RaidPlanner.Pick(sites) : sites.SingleOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (picked is null)
            {
                lines.Add(code is null ? "picked=none; no eligible product." : $"raid preview: unknown site '{code}'.");
                return lines;
            }
            lines.Add($"picked={picked.Code}; occupancy gate={(_occupancy[picked.Code].CanRaid ? "clear" : "blocked")}");
            var plan = Chapter3RaidPlanner.Plan(picked);
            if (plan is null) { lines.Add("plan=none; no removable whole package or stock unknown; no cooldown."); return lines; }
            lines.Add($"total units={plan.TotalUnits}; budget={plan.Budget}; planned units={plan.PlannedUnits}");
            foreach (var take in plan.Takes)
                lines.Add($"storage={take.Identity.StorageGuid:D}; slot={take.Identity.SlotIndex}; fingerprint={take.Fingerprint}; pre={take.PreQuantity}; packages={take.Packages}; units={take.Units}");
            return lines;
        }
        catch
        {
            Reset();
            return new[] { "raid preview: storage/census unavailable; unknown, blocked." };
        }
    }
}
