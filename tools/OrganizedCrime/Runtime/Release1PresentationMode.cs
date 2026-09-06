namespace OrganizedCrime.Runtime;

/// <summary>
/// Which presentation path the mod composes: the legacy IMGUI decision-prompt host, or the
/// native (S1API) presentation projector added in this feature. No Unity or S1API references,
/// so this file and its parser are test-linkable; the MelonPreferences-backed reader lives in
/// <see cref="Release1PresentationModePreference"/> instead.
/// </summary>
public enum Release1PresentationMode
{
    ImguiFallback,
    Native
}

/// <summary>Pure, deterministic parse of a stored preference string into a presentation mode.</summary>
public static class Release1PresentationModeParser
{
    public static Release1PresentationMode Parse(string? value) =>
        string.Equals(value, "ImguiFallback", StringComparison.OrdinalIgnoreCase)
            ? Release1PresentationMode.ImguiFallback
            : Release1PresentationMode.Native;
}
