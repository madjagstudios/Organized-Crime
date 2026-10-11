using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-141. The native bind runs through <c>OcCheckpointBindSequence</c> with
/// the native calls as delegates, so these tests drive the real ordering
/// logic: ready check, then disable copied behaviours, then map, then
/// SetCheckpoint, then Enable. Two source-shape tests pin the production
/// wiring the delegates cannot see: the bind writes neither AssignedNPCs
/// nor the Checkpoint field, and removal unbinds before despawning.
/// </summary>
[Collection("OC-168 checkpoint swap map")]
public sealed class OcCheckpointBindSequenceTests
{
    public OcCheckpointBindSequenceTests()
    {
        OcCheckpointSwapState.Clear();
    }

    [Fact]
    public void TryBind_runs_ready_disable_map_set_enable_in_order()
    {
        var order = new List<string>();

        var status = OcCheckpointBindSequence.TryBind(
            checkpointReady: () => { order.Add("ready"); return true; },
            disableCopied: () => { order.Add("disable"); return "schedule off"; },
            mapClone: () => order.Add("map"),
            setCheckpoint: () => order.Add("set"),
            enable: () => { order.Add("enable"); return true; },
            out var detail);

        Assert.Equal(OcOfficerWorldStatus.Ok, status);
        Assert.Equal(new[] { "ready", "disable", "map", "set", "enable" }, order);
        Assert.Equal("bound (schedule off; clone mapped; checkpoint set; enabled)", detail);
    }

    [Fact]
    public void TryBind_aborts_before_any_step_when_not_ready()
    {
        var order = new List<string>();

        var status = OcCheckpointBindSequence.TryBind(
            checkpointReady: () => { order.Add("ready"); return false; },
            disableCopied: () => { order.Add("disable"); return "must not run"; },
            mapClone: () => order.Add("map"),
            setCheckpoint: () => order.Add("set"),
            enable: () => { order.Add("enable"); return true; },
            out var detail);

        Assert.Equal(OcOfficerWorldStatus.Failed, status);
        Assert.Equal(new[] { "ready" }, order);
        Assert.Equal("checkpoint not ready; bind aborted", detail);
    }

    [Fact]
    public void TryBind_reports_failed_when_enable_reads_back_false()
    {
        var status = OcCheckpointBindSequence.TryBind(
            checkpointReady: () => true,
            disableCopied: () => "schedule off",
            mapClone: () => { },
            setCheckpoint: () => { },
            enable: () => false,
            out var detail);

        Assert.Equal(OcOfficerWorldStatus.Failed, status);
        Assert.Equal("bind failed (schedule off; clone mapped; checkpoint set; not enabled)", detail);
    }

    [Fact]
    public void TryBind_disables_copied_behaviours_on_every_call()
    {
        var disables = 0;
        OcOfficerWorldStatus Bind() => OcCheckpointBindSequence.TryBind(
            checkpointReady: () => true,
            disableCopied: () => { disables++; return "schedule off"; },
            mapClone: () => { },
            setCheckpoint: () => { },
            enable: () => true,
            out _);

        Assert.Equal(OcOfficerWorldStatus.Ok, Bind());
        Assert.Equal(OcOfficerWorldStatus.Ok, Bind());

        Assert.Equal(2, disables);
    }

    [Fact]
    public void TryBind_map_step_writes_the_swap_map_when_wired_as_production()
    {
        var target = new object();

        var status = OcCheckpointBindSequence.TryBind(
            checkpointReady: () => true,
            disableCopied: () => "schedule off",
            mapClone: () => OcCheckpointSwapState.MapClone(999, target),
            setCheckpoint: () => { },
            enable: () => true,
            out _);

        Assert.Equal(OcOfficerWorldStatus.Ok, status);
        Assert.True(OcCheckpointSwapState.TryGetCheckpoint(999, out var current));
        Assert.Same(target, current);
    }

    [Fact]
    public void TryUnbind_disables_before_unmapping()
    {
        var target = new object();
        OcCheckpointSwapState.MapClone(999, target);
        var order = new List<string>();

        var detail = OcCheckpointBindSequence.TryUnbind(
            999,
            disableCheckpoint: () =>
            {
                order.Add("disable");
                Assert.True(OcCheckpointSwapState.TryGetCheckpoint(999, out _));
            });

        Assert.Equal(new[] { "disable" }, order);
        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(999, out _));
        Assert.Equal("checkpoint disabled; swap entry cleared", detail);
    }

    [Fact]
    public void TryUnbind_unmaps_even_when_disable_throws()
    {
        OcCheckpointSwapState.MapClone(999, new object());

        var detail = OcCheckpointBindSequence.TryUnbind(
            999,
            disableCheckpoint: () => throw new InvalidOperationException("native gone"));

        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(999, out _));
        Assert.Equal("checkpoint disable failed (InvalidOperationException); swap entry cleared", detail);
    }

    [Fact]
    public void Bind_writes_neither_assigned_npcs_nor_the_checkpoint_field()
    {
        var body = ReadWorldMethod("public OcOfficerWorldStatus TryBindToCheckpoint");

        Assert.DoesNotContain("AssignedNPCs", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Checkpoint =", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_unbinds_before_despawning()
    {
        var body = ReadWorldMethod("public OcOfficerWorldStatus TryRemoveClone");
        var unbind = body.IndexOf("UnbindCloneForRemove(", StringComparison.Ordinal);
        var despawn = body.IndexOf("Despawn(", StringComparison.Ordinal);

        Assert.True(unbind >= 0, "TryRemoveClone never unbinds the clone.");
        Assert.True(despawn >= 0, "TryRemoveClone never despawns; the order check is meaningless.");
        Assert.True(unbind < despawn, "TryRemoveClone must unbind (disable + unmap) before despawning.");
        Assert.Contains("TryUnbind(", ReadWorldMethod("internal static string UnbindCloneForRemove"), StringComparison.Ordinal);
    }

    private static string ReadWorldMethod(string signature)
    {
        var file = signature.Contains("TryRemoveClone", StringComparison.Ordinal)
            ? "OcOfficerSpawner.cs" : "OcOfficerDutyBinder.cs";
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Police", file));
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The world has no {signature}.");
        var end = source.IndexOf("\n    public ", start + signature.Length, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
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
        throw new InvalidOperationException("Could not find the repository root from the test output.");
    }
}
