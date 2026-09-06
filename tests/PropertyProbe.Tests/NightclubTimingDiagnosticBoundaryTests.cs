using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class NightclubTimingDiagnosticBoundaryTests
{
    [Fact]
    public void Runtime_harness_is_observational_and_does_not_install_Harmony()
    {
        var source = ReadRepositoryFile("tools", "PropertyProbe", "Probes", "NightclubTimingDiagnosticProbe.cs");

        Assert.DoesNotContain("HarmonyLib", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Patch(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartInteract(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetOwned", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Teleport", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveManager.Save", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinator_exposes_only_the_documented_F5_owner_trigger_for_this_harness()
    {
        var source = ReadRepositoryFile("tools", "PropertyProbe", "ProbeCoordinator.cs");

        Assert.Contains("Input.GetKeyDown(KeyCode.F5)", source, StringComparison.Ordinal);
        Assert.Contains("_nightclubTimingDiagnosticProbe", source, StringComparison.Ordinal);

        var probeSource = ReadRepositoryFile("tools", "PropertyProbe", "Probes", "NightclubTimingDiagnosticProbe.cs");
        Assert.Contains("ReadAuthority", probeSource, StringComparison.Ordinal);
        Assert.Contains("TryAdmitTrigger", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("F17", probeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_writes_only_diagnostic_artifacts_through_ProbeLog()
    {
        var source = ReadRepositoryFile("tools", "PropertyProbe", "Probes", "NightclubTimingDiagnosticProbe.cs");

        Assert.Contains("nightclub-door-timing", source, StringComparison.Ordinal);
        Assert.Contains("ProbeLog.WriteFile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Move", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}
