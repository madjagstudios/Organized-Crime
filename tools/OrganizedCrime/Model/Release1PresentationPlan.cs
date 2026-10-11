namespace OrganizedCrime.Model;

public enum Release1DesiredEntryState
{
    Inactive,
    Active,
    Complete
}

public sealed record Release1WorldPoint(float X, float Y, float Z);

public sealed record Release1DesiredEntry(string Text, Release1DesiredEntryState State, Release1WorldPoint? Marker)
{
    // Runtime projections use ownership scopes without adding a public seam.
    internal string? Scope { get; init; }
}

public sealed record Release1DesiredQuest(string Key, string Title, IReadOnlyList<Release1DesiredEntry> Entries);
