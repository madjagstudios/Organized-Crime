using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseDeliveryRestoreGateTests
{
    private const string Target = "oc_fishwarehouse";

    [Fact]
    public void Target_dispatch_is_deferred_once_before_and_after_loading_when_docks_are_not_ready()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);

        Assert.True(gate.ShouldSuppress(
            "during-load", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, true));
        Assert.True(gate.ShouldSuppress(
            "after-load-duplicate", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false));
        Assert.Equal(1, gate.PendingCount);
    }

    [Fact]
    public void Target_status_and_display_are_deferred_only_during_loading_when_docks_are_not_ready()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);

        Assert.True(gate.ShouldSuppress("status", Target, "delivery-1", FishWarehouseDeliveryActionKind.Status, false, true));
        Assert.True(gate.ShouldSuppress("display", Target, "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, false, true));
        Assert.False(gate.ShouldSuppress("status-after-load", Target, "delivery-2", FishWarehouseDeliveryActionKind.Status, false, false));
        Assert.False(gate.ShouldSuppress("display-after-load", Target, "delivery-3", FishWarehouseDeliveryActionKind.StatusDisplay, false, false));
        Assert.Equal(2, gate.PendingCount);
    }

    [Fact]
    public void Unrelated_destinations_and_ready_target_actions_pass_through()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);

        foreach (var actionKind in Enum.GetValues<FishWarehouseDeliveryActionKind>())
        {
            Assert.False(gate.ShouldSuppress("unrelated", "barn", "delivery-1", actionKind, false, true));
            Assert.False(gate.ShouldSuppress("ready", Target, "delivery-2", actionKind, true, true));
        }

        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void Delivery_action_identity_includes_action_kind()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);

        Assert.True(gate.ShouldSuppress("dispatch", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false));
        Assert.True(gate.ShouldSuppress("status", Target, "delivery-1", FishWarehouseDeliveryActionKind.Status, false, true));
        Assert.True(gate.ShouldSuppress("display", Target, "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, false, true));

        Assert.Equal(3, gate.PendingCount);
        Assert.Equal(3, gate.BeginReadyReplay(targetReady: true).Count);
    }

    [Fact]
    public void Failed_replay_attempt_remains_pending_without_frame_by_frame_retry()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);
        gate.ShouldSuppress("payload", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false);

        Assert.Single(gate.BeginReadyReplay(targetReady: true));
        Assert.Empty(gate.BeginReadyReplay(targetReady: true));
        Assert.True(gate.HasPending("delivery-1", FishWarehouseDeliveryActionKind.Dispatch));
    }

    [Fact]
    public void Replayed_dispatch_and_display_suppress_duplicates_but_status_transitions_pass_through()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);
        gate.ShouldSuppress("status", Target, "delivery-1", FishWarehouseDeliveryActionKind.Status, false, true);
        gate.ShouldSuppress("display", Target, "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, false, true);
        gate.ShouldSuppress("dispatch", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false);

        Assert.Equal(3, gate.BeginReadyReplay(targetReady: true).Count);
        gate.MarkReplayed("delivery-1", FishWarehouseDeliveryActionKind.Status);
        gate.MarkReplayed("delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay);
        gate.MarkReplayed("delivery-1", FishWarehouseDeliveryActionKind.Dispatch);

        Assert.False(gate.ShouldSuppress("completed-transition", Target, "delivery-1", FishWarehouseDeliveryActionKind.Status, true, false));
        Assert.True(gate.ShouldSuppress("dispatch-duplicate", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, true, false));
        Assert.True(gate.ShouldSuppress("display-duplicate", Target, "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, true, false));
    }

    [Fact]
    public void Reset_clears_every_action_ledger()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>(Target);
        foreach (var actionKind in Enum.GetValues<FishWarehouseDeliveryActionKind>())
            gate.ShouldSuppress("payload", Target, "delivery-1", actionKind, false, true);

        gate.MarkReplayed("delivery-1", FishWarehouseDeliveryActionKind.Dispatch);
        gate.Reset();

        Assert.Equal(0, gate.PendingCount);
        Assert.False(gate.HasPending("delivery-1", FishWarehouseDeliveryActionKind.Status));
        Assert.False(gate.IsReplayed("delivery-1", FishWarehouseDeliveryActionKind.Dispatch));
        Assert.True(gate.ShouldSuppress("fresh", Target, "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false));
    }
}
