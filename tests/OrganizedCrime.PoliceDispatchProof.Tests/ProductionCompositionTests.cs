using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class ProductionCompositionTests
{
    [Fact]
    public void Proof_artifact_is_not_composed_into_production_runtime()
    {
        var root = FindRepositoryRoot();
        var productionProject = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "OrganizedCrime.csproj"));
        var productionMod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.DoesNotContain("PoliceDispatchProof", productionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("PoliceDispatchProof", productionMod, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "tools", "OrganizedCrime.PoliceDispatchProof", "OrganizedCrime.PoliceDispatchProof.csproj")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
