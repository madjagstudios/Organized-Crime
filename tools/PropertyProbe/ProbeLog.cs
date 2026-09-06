using MelonLoader;
using MelonLoader.Utils;

namespace OrganizedCrime.PropertyProbe;

internal static class ProbeLog
{
    private static readonly object Sync = new();
    private static string? _directory;

    public static string OutputDirectory => _directory ?? string.Empty;

    public static void Initialize()
    {
        _directory = Path.Combine(
            MelonEnvironment.UserDataDirectory,
            "CriminalEmpireProbe");

        Directory.CreateDirectory(_directory);
        Info($"Output directory: {_directory}");
    }

    public static string PathFor(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_directory))
            throw new InvalidOperationException("ProbeLog.Initialize() has not been called.");

        return Path.Combine(_directory, fileName);
    }

    public static void WriteFile(string fileName, string content)
    {
        var path = PathFor(fileName);
        lock (Sync)
        {
            File.WriteAllText(path, content);
        }
        Info($"Wrote {path}");
    }

    public static void Info(string message) =>
        MelonLogger.Msg($"[OrganizedCrime.PropertyProbe] {message}");

    public static void Warn(string message) =>
        MelonLogger.Warning($"[OrganizedCrime.PropertyProbe] {message}");

    public static void Error(string message) =>
        MelonLogger.Error($"[OrganizedCrime.PropertyProbe] {message}");
}
