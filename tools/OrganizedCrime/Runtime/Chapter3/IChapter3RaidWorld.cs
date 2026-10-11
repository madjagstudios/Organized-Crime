using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// Deliberately read-only. D7b's mutation journal must use a separate seam.
internal interface IChapter3RaidWorld
{
    bool IsHost { get; }
    IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions);
    void Reset() { }
}

// A load boundary re-arms diagnostics; ordinary cache/save resets do not.
internal interface IChapter3RaidLoadWorld
{
    void OnPreLoad();
}

internal interface IChapter3RaidQaTarget
{
    IReadOnlyList<string> RaidPreview(string? code);
}
