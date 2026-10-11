using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-141 composition: the production checkpoint world fails gracefully
/// without a game, and the composition root wires the real service exactly as
/// the service tests drive it (roster Update first, then checkpoints).
/// </summary>
[Collection("OC-151 composition scope")]
public sealed class Chapter3CheckpointCompositionTests
{
    [Fact]
    public void Production_world_constructs_without_touching_native()
    {
        var world = new Il2CppChapter3CheckpointWorld(_ => { });

        Assert.False(world.IsHost);
    }

    [Fact]
    public void Production_world_ops_fail_gracefully_without_a_game()
    {
        var world = new Il2CppChapter3CheckpointWorld(_ => { });

        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TryReadOwnedPropertyPositions(out _));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TryReadLastVisitedPropertyCode(out _));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TryReadNativeCheckpointPositions(out _));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TrySpawnCheckpoint("outside-barn", 0f, 0f, 0f, 0f, out _));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TryDespawnCheckpoint("nope"));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TrySetNativeCheckpoint("Docks", true));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TryReadPlayerCameraPose(out _));
        Assert.Equal(Chapter3CheckpointWorldStatus.NotHost, world.TryReadStandPoints("nope", out _));
        Assert.Equal(Chapter3CheckpointWorldStatus.InvalidHandle, world.TryReadSpawnedNative("nope", out _));
    }

    [Fact]
    public void Composition_builds_the_service_with_the_substituted_world_and_links_it()
    {
        // OC-151: the construction moved into Chapter3Module behind the engine seam;
        // the production adapter still news up the Il2Cpp world with the same args.
        using var scope = new CompositionScope();

        Assert.NotNull(scope.Root.Checkpoints);
        Assert.Same(scope.Root.Checkpoints, scope.Root.Chapter3!.Checkpoints);
        var trace = scope.Engine.Trace;
        Assert.True(trace.IndexOf("factory:CheckpointWorld") < trace.IndexOf("step:Chapter3.Checkpoints"),
            "the checkpoint world factory must run before the service construction.");

        var melon = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));
        Assert.Contains("new Il2CppChapter3CheckpointWorld(warn: warn, log: log)", melon, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_drives_the_roster_before_the_checkpoints()
    {
        // OC-151: the slots drive their own services; the roster-before-checkpoints order
        // itself lives in LifecycleFanoutTests.Update_runs_slots_in_order.
        using var scope = new CompositionScope();

        CompositionAssertions.AssertRosterAndCheckpointsSlotsDriveTheirServices(scope);
    }

    [Fact]
    public void Composition_drives_the_service_update_and_lifecycle()
    {
        // OC-151: participation moved into the lifecycle tables and the teardown trace.
        using var scope = new CompositionScope();
        var tables = LifecycleTables.ForRoot(scope.Root);

        CompositionAssertions.AssertPhaseTables(tables);
        CompositionAssertions.AssertUpdateSlotsWired(tables.Update);
        CompositionAssertions.AssertForRootCheckpointLifecycleNeedles();
        scope.Root.Teardown();
        Assert.Contains("teardown:Checkpoints.Dispose", scope.Engine.Trace);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "Mod.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root.");
    }
}
