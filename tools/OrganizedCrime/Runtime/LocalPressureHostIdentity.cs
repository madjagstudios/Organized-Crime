namespace OrganizedCrime.Runtime;

public static class LocalPressureHostIdentity
{
    public static bool TryGetCanonicalHostIdentity(string? activeSaveFolder, out string? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
            return false;

        var segments = activeSaveFolder
            .Trim()
            .TrimEnd('\\', '/')
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
            return false;

        var saveFolder = segments[^1];
        var accountFolder = segments[^2];
        var savesFolder = segments[^3];
        if (!saveFolder.StartsWith("SaveGame_", StringComparison.OrdinalIgnoreCase) ||
            saveFolder.Length == "SaveGame_".Length ||
            !string.Equals(savesFolder, "Saves", StringComparison.OrdinalIgnoreCase) ||
            !IsSteamIdentity(accountFolder))
            return false;

        identity = accountFolder;
        return true;
    }

    public static bool IsPlaceholderPlayerCode(string? playerCode)
    {
        var normalized = playerCode?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "0", StringComparison.Ordinal);
    }

    public static bool IsCanonicalPlayerIdentity(string? identity) =>
        !string.IsNullOrWhiteSpace(identity) && IsSteamIdentity(identity.Trim());

    private static bool IsSteamIdentity(string identity)
    {
        if (identity.Length != 17)
            return false;
        foreach (var character in identity)
            if (character < '0' || character > '9')
                return false;
        return true;
    }
}
