using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseDeliveryRestoreOrderingTests
{
    [Theory]
    [InlineData("DispatchPrefix")]
    [InlineData("StatusPrefix")]
    [InlineData("StatusDisplayPrefix")]
    public void Client_prefix_passes_through_without_touching_native_delivery_or_gate(string prefixName)
    {
        FishWarehouseDeliveryRestorePatch.Reset();
        var authorityReads = 0;
        FishWarehouseDeliveryRestorePatch.IsHost = () => { authorityReads++; return false; };
        try
        {
            // No native constructor or valid pointer: any access to delivery
            // properties would fail in this test host, so the client must exit first.
            var delivery = (Il2CppScheduleOne.Delivery.DeliveryInstance)
                System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                    typeof(Il2CppScheduleOne.Delivery.DeliveryInstance));
            GC.SuppressFinalize(delivery); // The placeholder owns no native GC handle.
            object?[] arguments = prefixName == "StatusPrefix"
                ? new object?[] { delivery, default(Il2CppScheduleOne.Delivery.EDeliveryStatus) }
                : new object?[] { null, delivery };
            var prefix = typeof(FishWarehouseDeliveryRestorePatch).GetMethod(
                prefixName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

            Assert.True((bool)prefix.Invoke(null, arguments)!);
            Assert.Equal(1, authorityReads);
            Assert.Equal(0, FishWarehouseDeliveryRestorePatch.FlushIfReady().PendingCount);
        }
        finally
        {
            FishWarehouseDeliveryRestorePatch.Reset();
        }
    }

    [Theory]
    [InlineData("DispatchPrefix")]
    [InlineData("StatusPrefix")]
    [InlineData("StatusDisplayPrefix")]
    public void Host_prefix_keeps_the_existing_null_delivery_pass_through(string prefixName)
    {
        FishWarehouseDeliveryRestorePatch.Reset();
        var authorityReads = 0;
        FishWarehouseDeliveryRestorePatch.IsHost = () => { authorityReads++; return true; };
        try
        {
            object?[] arguments = prefixName == "StatusPrefix"
                ? new object?[] { null, default(Il2CppScheduleOne.Delivery.EDeliveryStatus) }
                : new object?[] { null, null };
            var prefix = typeof(FishWarehouseDeliveryRestorePatch).GetMethod(
                prefixName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

            Assert.True((bool)prefix.Invoke(null, arguments)!);
            Assert.Equal(1, authorityReads);
            Assert.Equal(0, FishWarehouseDeliveryRestorePatch.FlushIfReady().PendingCount);
        }
        finally
        {
            FishWarehouseDeliveryRestorePatch.Reset();
        }
    }

    [Fact]
    public void Flush_outcome_remains_available_when_the_existing_caller_discards_the_return_value()
    {
        FishWarehouseDeliveryRestorePatch.Reset();
        FishWarehouseDeliveryRestorePatch.FlushIfReady();

        Assert.NotNull(FishWarehouseDeliveryRestorePatch.LastFlushResult);
        Assert.Equal(0, FishWarehouseDeliveryRestorePatch.LastFlushResult.PendingCount);
        Assert.Null(FishWarehouseDeliveryRestorePatch.LastFlushResult.Failure);
    }

    [Fact]
    public void Failed_replay_followed_by_no_op_flush_retains_failure()
    {
        FishWarehouseDeliveryRestorePatch.Reset();
        FishWarehouseDeliveryRestorePatch.StoreFlushResult(
            new FishWarehouseDeliveryRestoreFlushResult(1, "status replay failed"));

        FishWarehouseDeliveryRestorePatch.FlushIfReady();

        Assert.Equal(1, FishWarehouseDeliveryRestorePatch.LastFlushResult.PendingCount);
        Assert.Equal("status replay failed", FishWarehouseDeliveryRestorePatch.LastFlushResult.Failure);

        FishWarehouseDeliveryRestorePatch.Reset();
        FishWarehouseDeliveryRestorePatch.FlushIfReady();

        Assert.Null(FishWarehouseDeliveryRestorePatch.LastFlushResult.Failure);
    }

    [Fact]
    public void Failed_dispatch_stops_later_actions_and_leaves_them_pending()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>("oc_fishwarehouse");
        gate.ShouldSuppress("dispatch", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false);
        gate.ShouldSuppress("status", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.Status, false, true);
        gate.ShouldSuppress("display", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, false, true);

        var ready = gate.BeginReadyReplay(targetReady: true);
        var invoked = new List<FishWarehouseDeliveryActionKind>();
        Exception? replayFailure = null;

        FishWarehouseDeliveryRestorePatch.ReplayUntilFailure(
            ready,
            pending =>
            {
                invoked.Add(pending.ActionKind);
                if (pending.ActionKind == FishWarehouseDeliveryActionKind.Dispatch)
                    throw new InvalidOperationException("dispatch failed");

                gate.MarkReplayed(pending.DeliveryId, pending.ActionKind);
            },
            (_, exception) => replayFailure = exception);

        Assert.Equal(new[] { FishWarehouseDeliveryActionKind.Dispatch }, invoked);
        Assert.NotNull(replayFailure);
        Assert.Equal(3, gate.PendingCount);
        Assert.True(gate.HasPending("delivery-1", FishWarehouseDeliveryActionKind.Status));
        Assert.True(gate.HasPending("delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay));
    }

    [Fact]
    public void Ready_replay_is_ordered_dispatch_status_then_status_display()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>("oc_fishwarehouse");
        gate.ShouldSuppress("status-display", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, false, true);
        gate.ShouldSuppress("dispatch", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.Dispatch, false, false);
        gate.ShouldSuppress("status", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.Status, false, true);

        var order = gate.BeginReadyReplay(targetReady: true)
            .Select(pending => pending.ActionKind)
            .ToArray();

        Assert.Equal(
            new[]
            {
                FishWarehouseDeliveryActionKind.Dispatch,
                FishWarehouseDeliveryActionKind.Status,
                FishWarehouseDeliveryActionKind.StatusDisplay
            },
            order);
    }

    [Fact]
    public void Nested_status_display_marks_explicit_display_satisfied()
    {
        var gate = new FishWarehouseDeliveryRestoreGate<string>("oc_fishwarehouse");
        gate.ShouldSuppress("status", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.Status, false, true);
        gate.ShouldSuppress("display", "oc_fishwarehouse", "delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay, false, true);

        var ready = gate.BeginReadyReplay(targetReady: true);
        Assert.Equal(2, ready.Count);

        gate.MarkReplayed("delivery-1", FishWarehouseDeliveryActionKind.Status);
        gate.MarkReplayed("delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay);

        Assert.True(gate.IsReplayed("delivery-1", FishWarehouseDeliveryActionKind.Status));
        Assert.True(gate.IsReplayed("delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay));
        Assert.False(gate.HasPending("delivery-1", FishWarehouseDeliveryActionKind.StatusDisplay));
    }
}
