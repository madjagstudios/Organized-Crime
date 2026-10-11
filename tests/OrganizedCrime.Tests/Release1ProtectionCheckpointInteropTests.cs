using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-118. Pins the S1API checkpoint surface the production checkpoint world calls, against the
/// referenced S1API build. Behavior without a game is untestable (the S1API calls null-safe
/// no-op), so member shapes are the contract: a drifted wrapper fails here, not in game.
/// </summary>
public sealed class Release1ProtectionCheckpointInteropTests
{
    [Fact]
    public void S1api_checkpoint_surface_matches_the_adapter()
    {
        var manager = typeof(S1API.Law.CheckpointManager);
        Assert.True(manager.IsAbstract && manager.IsSealed);

        var disableAll = manager.GetMethod("DisableAllCheckpoints", Type.EmptyTypes);
        Assert.NotNull(disableAll);
        Assert.Equal(typeof(void), disableAll!.ReturnType);

        var isEnabled = manager.GetMethod("IsCheckpointEnabled", new[] { typeof(S1API.Law.CheckpointLocation) });
        Assert.NotNull(isEnabled);
        Assert.Equal(typeof(bool), isEnabled!.ReturnType);

        var setEnabled = manager.GetMethod(
            "SetCheckpointEnabled",
            new[] { typeof(S1API.Law.CheckpointLocation), typeof(bool), typeof(int) });
        Assert.NotNull(setEnabled);
        Assert.Equal(typeof(void), setEnabled!.ReturnType);

        var location = typeof(S1API.Law.CheckpointLocation);
        Assert.True(location.IsEnum);
        Assert.Equal(
            new[] { "Western", "Docks", "NorthResidential", "WestResidential" },
            Enum.GetNames(location));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues(location).Cast<int>().ToArray());
    }
}
