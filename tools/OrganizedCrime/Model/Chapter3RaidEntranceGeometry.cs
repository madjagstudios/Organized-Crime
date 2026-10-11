namespace OrganizedCrime.Model;

internal readonly record struct Chapter3RaidPoint(double X, double Y, double Z)
{
    internal bool Valid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) &&
        Math.Abs(X) <= 10000 && Math.Abs(Y) <= 10000 && Math.Abs(Z) <= 10000;
    internal double Distance(Chapter3RaidPoint p) => Math.Sqrt((X - p.X) * (X - p.X) + (Y - p.Y) * (Y - p.Y) + (Z - p.Z) * (Z - p.Z));
}
internal readonly record struct Chapter3RaidBounds(Chapter3RaidPoint Min, Chapter3RaidPoint Max)
{
    internal bool Valid => Min.Valid && Max.Valid && Max.X > Min.X && Max.Y > Min.Y && Max.Z > Min.Z &&
        Max.X - Min.X <= 1000 && Max.Y - Min.Y <= 1000 && Max.Z - Min.Z <= 1000;
    internal bool Contains(Chapter3RaidPoint p) => Valid && p.Valid && p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
    internal Chapter3RaidPoint GroundCenter => new((Min.X + Max.X) / 2, Min.Y, (Min.Z + Max.Z) / 2);
    internal double HorizontalDistance(Chapter3RaidPoint p)
    {
        var x = Math.Max(0, Math.Max(Min.X - p.X, p.X - Max.X));
        var z = Math.Max(0, Math.Max(Min.Z - p.Z, p.Z - Max.Z));
        return Math.Sqrt(x * x + z * z);
    }
}
internal enum Chapter3RaidEntranceSource { Door, Spawn, Bounds }
internal sealed record Chapter3RaidEntrance(Chapter3RaidPoint Point, Chapter3RaidEntranceSource Source);

// Pure geometry policy: native reads are candidates, never trusted anchors.
internal static class Chapter3RaidEntranceGeometry
{
    internal static Chapter3RaidBounds? FromCorners(IReadOnlyList<Chapter3RaidPoint> corners)
    {
        if (corners.Count != 8 || corners.Any(p => !p.Valid)) return null;
        var box = new Chapter3RaidBounds(new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
            new(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)));
        return box.Valid ? box : null;
    }
    internal const double BoundsAllowance = 15;
    internal static bool Usable(Chapter3RaidPoint point, IReadOnlyList<Chapter3RaidBounds> bounds) =>
        point.Valid && bounds.Any(b => b.Valid && b.HorizontalDistance(point) <= BoundsAllowance &&
            point.Y >= b.Min.Y - 5 && point.Y <= b.Max.Y + 5);

    internal static Chapter3RaidEntrance? Bind(IReadOnlyList<Chapter3RaidPoint> doors,
        Chapter3RaidPoint? spawn, IReadOnlyList<Chapter3RaidBounds> bounds)
    {
        var spawnValid = spawn is { } s && Usable(s, bounds);
        var validDoors = doors.Where(p => Usable(p, bounds) && (spawnValid || !bounds.Any(b => b.Valid &&
            p.X > b.Min.X && p.X < b.Max.X && p.Z > b.Min.Z && p.Z < b.Max.Z))).Distinct()
            .OrderBy(p => spawnValid ? p.Distance(spawn!.Value) : 0)
            .ThenBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToArray();
        // Tied door candidates are not proof of an exterior door; use spawn.
        if (validDoors.Length > 0 && (validDoors.Length == 1 || (spawnValid &&
            validDoors[1].Distance(spawn!.Value) - validDoors[0].Distance(spawn.Value) >= 0.1)))
            return new(validDoors[0], Chapter3RaidEntranceSource.Door);
        if (spawnValid) return new(spawn!.Value, Chapter3RaidEntranceSource.Spawn);
        var boxes = bounds.Where(b => b.Valid).OrderByDescending(b => (b.Max.X - b.Min.X) * (b.Max.Z - b.Min.Z))
            .ThenBy(b => b.Min.X).ThenBy(b => b.Min.Y).ThenBy(b => b.Min.Z).ToArray();
        return boxes.Length == 0 ? null : new(boxes[0].GroundCenter, Chapter3RaidEntranceSource.Bounds);
    }

    internal static Chapter3RaidPoint? Rebind(Chapter3RaidPoint? live, Chapter3RaidPoint fallback,
        IReadOnlyList<Chapter3RaidBounds> bounds) =>
        live is { } point && Usable(point, bounds) && point.Distance(fallback) <= 2 ? point :
        Usable(fallback, bounds) ? fallback : null;

    internal static Chapter3RaidPresence PropertyPresence(Chapter3RaidPoint position, bool currentPropertyMatches,
        bool insideBounds, Chapter3RaidPoint? entrance, IReadOnlyList<Chapter3RaidBounds> bounds,
        Chapter3RaidEntranceSource? source)
    {
        if (!position.Valid) return Chapter3RaidPresence.Unknown;
        if (currentPropertyMatches || insideBounds) return Chapter3RaidPresence.Inside;
        // Partial geometry cannot prove that the player is away from every box.
        if (bounds.Count == 0 || bounds.Any(b => !b.Valid)) return Chapter3RaidPresence.Unknown;
        if (bounds.Any(b => b.Contains(position))) return Chapter3RaidPresence.Inside;
        var distance = bounds.Min(b => b.HorizontalDistance(position));
        if (source == Chapter3RaidEntranceSource.Door && entrance is { } door && Usable(door, bounds))
        {
            if (distance <= BoundsAllowance) return Chapter3RaidPresence.Near;
            return Presence(position, false, false, door, bounds);
        }
        // Spawn, bounds, missing and unusable door anchors cannot narrow occupancy.
        return distance <= Chapter3RaidOccupancy.NearMetres + BoundsAllowance ?
            Chapter3RaidPresence.Near : Chapter3RaidPresence.Clear;
    }

    // Low-level radius classifier; property observations must use PropertyPresence.
    internal static Chapter3RaidPresence Presence(Chapter3RaidPoint position, bool currentPropertyMatches,
        bool insideBounds, Chapter3RaidPoint? entrance, IReadOnlyList<Chapter3RaidBounds> bounds, bool boundsFallback = false)
    {
        if (!position.Valid) return Chapter3RaidPresence.Unknown;
        // Even a missing entrance cannot hide a positively identified occupant.
        if (currentPropertyMatches || insideBounds || bounds.Any(b => b.Contains(position))) return Chapter3RaidPresence.Inside;
        var distance = !boundsFallback && entrance is { } p && Usable(p, bounds) ? position.Distance(p) :
            bounds.Any(b => b.Valid) ? bounds.Where(b => b.Valid).Min(b => b.HorizontalDistance(position)) : double.NaN;
        return Chapter3RaidOccupancy.Classify(true, new[] { new Chapter3RaidPlayer(true, false, false, distance) });
    }
}
