namespace OrganizedCrime.Model;

/// <summary>
/// One owned property with the headcount the daily pick scores.
/// </summary>
public sealed record Chapter3CheckpointScoredProperty(string Code, int Employees, int Stations);

/// <summary>
/// One native checkpoint site by display label and horizontal position. The
/// service maps labels back to locations; the planner never touches S1API.
/// </summary>
public sealed record Chapter3CheckpointNativeSite(string Label, float X, float Z);

/// <summary>
/// The planner's answer: OC spot labels plus the asserted native label, if any.
/// </summary>
public sealed record Chapter3CheckpointActiveSet(IReadOnlyList<string> OcLabels, string? NativeLabel);

/// <summary>
/// The pure checkpoint planner: busiest-property target picks and the
/// tier-driven active set. Distances are horizontal (x, z); ties break by
/// label with ordinal comparison.
/// </summary>
public static class Chapter3CheckpointPlanner
{
    /// <summary>
    /// Score = employees + production stations. The unique positive winner
    /// takes it; a tie or an all-zero result falls back to the last-visited
    /// property when owned, else the first owned code in code order. No
    /// owned property means no target.
    /// </summary>
    public static string? PickTarget(IReadOnlyList<Chapter3CheckpointScoredProperty> owned, string? lastVisitedCode)
    {
        var codes = new List<(string Code, int Score)>();
        foreach (var property in owned)
        {
            if (property is null || string.IsNullOrWhiteSpace(property.Code))
                continue;
            codes.Add((property.Code, Math.Max(0, property.Employees) + Math.Max(0, property.Stations)));
        }
        if (codes.Count == 0)
            return null;
        var best = codes.Max(entry => entry.Score);
        if (best > 0 && codes.Count(entry => entry.Score == best) == 1)
            return codes.Single(entry => entry.Score == best).Code;
        if (!string.IsNullOrWhiteSpace(lastVisitedCode) &&
            codes.Any(entry => string.Equals(entry.Code, lastVisitedCode, StringComparison.Ordinal)))
            return lastVisitedCode;
        return codes.Select(entry => entry.Code).OrderBy(code => code, StringComparer.Ordinal).First();
    }

    /// <summary>
    /// Protected or below Watched: nothing. Watched: the nearest OC spot
    /// plus the nearest native checkpoint. Critical: the 3 nearest OC spots
    /// plus that native checkpoint.
    /// </summary>
    public static Chapter3CheckpointActiveSet ActiveSet(
        float targetX,
        float targetZ,
        LocalPressureTier tier,
        bool isProtected,
        IReadOnlyList<Chapter3CheckpointSpot> spots,
        IReadOnlyList<Chapter3CheckpointNativeSite> natives)
    {
        var empty = new Chapter3CheckpointActiveSet(Array.Empty<string>(), null);
        if (isProtected || (tier != LocalPressureTier.Watched && tier != LocalPressureTier.Critical))
            return empty;
        var take = tier == LocalPressureTier.Critical ? 3 : 1;
        var labels = spots
            .Where(spot => spot is not null && !string.IsNullOrWhiteSpace(spot.Label))
            .Select(spot => (spot.Label, DistanceSquared(spot.X, spot.Z, targetX, targetZ)))
            .OrderBy(entry => entry.Item2)
            .ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Take(take)
            .Select(entry => entry.Label)
            .ToList();
        var native = natives
            .Where(site => site is not null && !string.IsNullOrWhiteSpace(site.Label))
            .Select(site => (site.Label, DistanceSquared(site.X, site.Z, targetX, targetZ)))
            .OrderBy(entry => entry.Item2)
            .ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .FirstOrDefault();
        return new Chapter3CheckpointActiveSet(labels, native.Label);
    }

    private static double DistanceSquared(float x, float z, float targetX, float targetZ)
    {
        var dx = (double)x - targetX;
        var dz = (double)z - targetZ;
        return dx * dx + dz * dz;
    }
}
