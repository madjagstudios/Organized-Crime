using Il2Cpp;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class S1CartelStatusSourceBoundaryTests
{
    private static readonly string Source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "tools", "OrganizedCrime", "Runtime", "S1CartelStatusSource.cs"));

    [Fact]
    public void Native_boundary_reads_only_the_cartel_singleton_status()
    {
        Assert.Contains("Cartel.InstanceExists", Source, StringComparison.Ordinal);
        Assert.Contains("Cartel.Instance", Source, StringComparison.Ordinal);
        Assert.Contains("cartel.Status", Source, StringComparison.Ordinal);
        Assert.Contains("ECartelStatus.Defeated", Source, StringComparison.Ordinal);

        Assert.DoesNotContain("SetStatus", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChoiceCallback", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Quest_DefeatCartel", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Harmony", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_status_mapping_is_exact_and_fail_closed()
    {
        AssertMap(ECartelStatus.Unknown, Release1CartelStatus.Unknown);
        AssertMap(ECartelStatus.Hostile, Release1CartelStatus.Hostile);
        AssertMap(ECartelStatus.Truced, Release1CartelStatus.Truced);
        AssertMap(ECartelStatus.Defeated, Release1CartelStatus.Defeated);

        Assert.False(S1CartelStatusSource.TryMap((ECartelStatus)int.MaxValue, out _));
    }

    private static void AssertMap(ECartelStatus native, Release1CartelStatus expected)
    {
        Assert.True(S1CartelStatusSource.TryMap(native, out var actual));
        Assert.Equal(expected, actual);
    }
}
