using System.Reflection;

namespace OrganizedCrime.PropertyProbe.Runtime;

internal sealed record OptionalApiCapability(
    string AssemblyName,
    bool IsLoaded,
    string? Version);

internal static class S1ApiCapabilityDetector
{
    public static IReadOnlyList<OptionalApiCapability> Detect()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();

        return new[]
        {
            DetectAssembly(loaded, "S1API"),
            DetectAssembly(loaded, "S1MAPI")
        };
    }

    private static OptionalApiCapability DetectAssembly(
        IEnumerable<Assembly> assemblies,
        string expectedName)
    {
        var assembly = assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, expectedName, StringComparison.OrdinalIgnoreCase));

        return assembly is null
            ? new OptionalApiCapability(expectedName, false, null)
            : new OptionalApiCapability(
                expectedName,
                true,
                assembly.GetName().Version?.ToString());
    }
}
