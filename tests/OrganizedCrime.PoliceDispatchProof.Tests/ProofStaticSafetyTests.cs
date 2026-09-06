using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class ProofStaticSafetyTests
{
    [Fact]
    public void Harness_source_has_no_private_reflection_harmony_or_local_player_shortcuts()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(
                Path.Combine(root, "tools", "OrganizedCrime.PoliceDispatchProof"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains("bin", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains("obj", StringComparison.OrdinalIgnoreCase));

        var source = string.Join("\n", files.Select(File.ReadAllText));
        Assert.DoesNotContain("BindingFlags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetField(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Harmony", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Player.Local", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FindObjectsOfType", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_pin_matches_the_indexed_callable_surface()
    {
        Assert.Equal(
            "ScheduleOne.Map.PoliceStation::Dispatch(System.Int32,ScheduleOne.PlayerScripts.Player,ScheduleOne.Map.PoliceStation+EDispatchType,System.Boolean):System.Void",
            OrganizedCrime.PoliceDispatchProof.DispatchProofPins.DispatchSignature);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
