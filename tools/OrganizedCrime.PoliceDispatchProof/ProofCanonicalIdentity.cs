namespace OrganizedCrime.PoliceDispatchProof;

public static class ProofCanonicalIdentity
{
    public static bool TryGet(string? activeSaveFolder, out string? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
            return false;

        var segments = activeSaveFolder.Trim().TrimEnd('\\', '/').Split(
            new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
            return false;

        var saveFolder = segments[^1];
        var accountFolder = segments[^2];
        var savesFolder = segments[^3];
        if (!saveFolder.StartsWith("SaveGame_", StringComparison.OrdinalIgnoreCase) ||
            saveFolder.Length == "SaveGame_".Length ||
            !string.Equals(savesFolder, "Saves", StringComparison.OrdinalIgnoreCase) ||
            accountFolder.Length != 17 ||
            accountFolder.Any(character => character < '0' || character > '9'))
            return false;

        identity = accountFolder;
        return true;
    }
}
