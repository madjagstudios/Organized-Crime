namespace OrganizedCrime.Runtime;

public static class Release1StoryHostIdentity
{
    public static Release1StoryHostContextReadStatus Resolve(string? activeSaveFolder, string? playerCode, out string? canonicalIdentity)
    {
        canonicalIdentity = null;
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
            return Release1StoryHostContextReadStatus.Pending;
        if (!LocalPressureHostIdentity.TryGetCanonicalHostIdentity(activeSaveFolder, out var pathIdentity) || pathIdentity is null)
            return Release1StoryHostContextReadStatus.AmbiguousIdentity;

        var normalizedCode = playerCode?.Trim();
        if (!LocalPressureHostIdentity.IsCanonicalPlayerIdentity(normalizedCode))
            return Release1StoryHostContextReadStatus.Pending;
        if (!string.Equals(pathIdentity, normalizedCode, StringComparison.Ordinal))
            return Release1StoryHostContextReadStatus.AmbiguousIdentity;

        canonicalIdentity = pathIdentity;
        return Release1StoryHostContextReadStatus.Ready;
    }
}
