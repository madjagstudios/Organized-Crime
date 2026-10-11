using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Source-regression tests for the OC-94 Schedule I 0.4.7 compatibility fixes:
/// game members that moved to S1API or to private fields, reached through the generated interop.
/// </summary>
public sealed class OC094CompatibilitySourceTests
{
    [Fact]
    public void Sleep_seam_binds_through_S1API_TimeManager()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "LocalPressureHostLifecycleAdapter.cs"));

        Assert.DoesNotContain(".onSleepEnd", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsSleepInProgress", source, StringComparison.Ordinal);
        Assert.Contains("S1API.GameTime.TimeManager", source, StringComparison.Ordinal);
        Assert.Contains("S1TimeManager.OnSleepEnd", source, StringComparison.Ordinal);
        Assert.Contains("S1TimeManager.SleepInProgress", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_dock_host_assigns_the_parent_lot_through_the_private_047_field()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "FishWarehouseLoadingDockHost.cs"));

        Assert.DoesNotContain(".ParentLot", source, StringComparison.Ordinal);
        Assert.Contains("._parentParentLot", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Garage_crossing_host_reaches_agents_through_the_private_047_field()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "FishWarehouseGarageCrossingHost.cs"));

        Assert.DoesNotContain("Movement.Agent", source, StringComparison.Ordinal);
        Assert.Contains("Movement._agent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_adapter_reaches_agents_through_the_private_047_field()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "UnityNativeNavigationAdapter.cs"));

        Assert.DoesNotContain("Movement.Agent", source, StringComparison.Ordinal);
        Assert.Contains("Movement._agent", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git", "HEAD")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Organized Crime repository root could not be located.");
    }
}
