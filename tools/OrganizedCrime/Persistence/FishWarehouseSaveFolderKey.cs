using System.Security.Cryptography;
using System.Text;

namespace OrganizedCrime.Persistence;

public static class FishWarehouseSaveFolderKey
{
    public static string Create(string saveFolder)
    {
        if (string.IsNullOrWhiteSpace(saveFolder))
            throw new ArgumentException("Save folder was empty.", nameof(saveFolder));

        var fullPath = Path.GetFullPath(saveFolder);
        var normalized = Path.TrimEndingDirectorySeparator(fullPath).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
