using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class NativeLawResponseControllerTests
{
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly object SourcePlayer = new();

    [Fact]
    public void Watched_edge_invokes_adapter_once_with_exact_profile_and_target()
    {
        var adapter = new RecordingAdapter(NativeLawResponseResultState.Accepted);
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        var request = Assert.Single(adapter.Requests);
        Assert.Equal("player-1", request.PlayerId);
        Assert.Equal("edge-1", request.CorrelationId);
        Assert.Equal(LocalPressureTier.Watched, request.TriggerTier);
        Assert.Equal(3, request.LoadEpoch);
        Assert.Equal("north", request.Region);
        Assert.Equal("safehouse", request.PropertyCode);
        Assert.Equal(2, request.Profile.RequestedOfficerCount);
        Assert.True(request.Profile.UseVehicle);
        Assert.False(request.Profile.BeginAsSighted);
        Assert.Same(SourcePlayer, request.SourcePlayer);
    }

    [Theory]
    [InlineData(NativeLawResponseResultState.Accepted)]
    [InlineData(NativeLawResponseResultState.Rejected)]
    [InlineData(NativeLawResponseResultState.UnavailableCapacity)]
    [InlineData(NativeLawResponseResultState.InconclusiveOutcome)]
    [InlineData(NativeLawResponseResultState.AdapterUnavailable)]
    [InlineData(NativeLawResponseResultState.AuthorityRejected)]
    [InlineData(NativeLawResponseResultState.TargetRejected)]
    public void Every_adapter_result_closes_in_flight_without_retry(NativeLawResponseResultState state)
    {
        var adapter = new RecordingAdapter(state);
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));
        controller.Publish(Transition("edge-2", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal("edge-1", adapter.Requests[0].CorrelationId);
        Assert.Equal("edge-2", adapter.Requests[1].CorrelationId);
    }

    [Fact]
    public void Unsupported_edge_is_suppressed_without_adapter_attempt()
    {
        var adapter = new RecordingAdapter(NativeLawResponseResultState.Accepted);
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Quiet, LocalPressureTier.Noticed));

        Assert.Empty(adapter.Requests);
        Assert.NotNull(controller.LastResult);
        Assert.Equal(NativeLawResponseResultState.Suppressed, controller.LastResult!.State);
        Assert.Equal("edge-1", controller.LastResult.CorrelationId);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.UnsupportedTierEdge.ToString(), controller.LastResult.Reason);
    }

    [Fact]
    public void Duplicate_correlation_is_suppressed_without_second_adapter_attempt()
    {
        var adapter = new RecordingAdapter(NativeLawResponseResultState.Accepted);
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));
        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.Single(adapter.Requests);
        Assert.NotNull(controller.LastResult);
        Assert.Equal(NativeLawResponseResultState.Suppressed, controller.LastResult!.State);
        Assert.Equal("edge-1", controller.LastResult.CorrelationId);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.DuplicateCorrelation.ToString(), controller.LastResult.Reason);
    }

    [Fact]
    public void In_flight_reentrant_delivery_is_suppressed_without_second_adapter_attempt()
    {
        var adapter = new ReentrantAdapter();
        using var controller = ReadyController(adapter);
        NativeLawResponseResult? reentrantResult = null;
        adapter.Reenter = () =>
        {
            controller.Publish(Transition("edge-2", LocalPressureTier.Noticed, LocalPressureTier.Watched));
            reentrantResult = controller.LastResult;
        };

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.Single(adapter.Requests);
        Assert.NotNull(reentrantResult);
        Assert.Equal(NativeLawResponseResultState.Suppressed, reentrantResult!.State);
        Assert.Equal("edge-2", reentrantResult.CorrelationId);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.InFlight.ToString(), reentrantResult.Reason);
    }

    [Fact]
    public void Throwing_adapter_is_contained_as_adapter_unavailable()
    {
        var adapter = new ThrowingThenAcceptedAdapter();
        using var controller = ReadyController(adapter);

        var exception = Record.Exception(() =>
            controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched)));

        Assert.Null(exception);
        Assert.NotNull(controller.LastResult);
        Assert.Equal(NativeLawResponseResultState.AdapterUnavailable, controller.LastResult!.State);
        Assert.Equal("edge-1", controller.LastResult.CorrelationId);

        controller.Publish(Transition("edge-2", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal("edge-2", adapter.Requests[1].CorrelationId);
        Assert.NotNull(controller.LastResult);
        Assert.Equal(NativeLawResponseResultState.Accepted, controller.LastResult!.State);
        Assert.Equal("edge-2", controller.LastResult.CorrelationId);
    }

    [Fact]
    public void Reset_for_epoch_closes_prior_in_flight_and_allows_same_correlation_on_new_epoch()
    {
        var adapter = new BlockingAdapter();
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));
        controller.ResetForEpoch(Session, 4);
        adapter.Result = Result("edge-1", NativeLawResponseResultState.Accepted);
        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched, loadEpoch: 4));

        Assert.Equal(2, adapter.Requests.Count);
        Assert.NotNull(controller.LastResult);
        Assert.Equal(NativeLawResponseResultState.Accepted, controller.LastResult!.State);
        Assert.Equal(4, adapter.Requests[1].LoadEpoch);
    }

    [Fact]
    public void Stale_epoch_fails_closed_without_adapter_attempt()
    {
        var adapter = new RecordingAdapter(NativeLawResponseResultState.Accepted);
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched, loadEpoch: 4));

        Assert.Empty(adapter.Requests);
        Assert.NotNull(controller.LastResult);
        Assert.Equal(NativeLawResponseResultState.Rejected, controller.LastResult!.State);
        Assert.Equal("edge-1", controller.LastResult.CorrelationId);
    }

    [Fact]
    public void Publish_after_disposal_is_inert_and_preserves_last_result()
    {
        var adapter = new RecordingAdapter(NativeLawResponseResultState.Accepted);
        var controller = ReadyController(adapter);
        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));
        var resultBeforeDispose = Assert.IsType<NativeLawResponseResult>(controller.LastResult);
        controller.Dispose();

        controller.Publish(Transition("edge-2", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.Single(adapter.Requests);
        Assert.Same(resultBeforeDispose, controller.LastResult);
    }

    [Fact]
    public void Later_distinct_reentry_can_attempt_after_first_completion()
    {
        var adapter = new RecordingAdapter(NativeLawResponseResultState.Accepted);
        using var controller = ReadyController(adapter);

        controller.Publish(Transition("edge-1", LocalPressureTier.Noticed, LocalPressureTier.Watched));
        controller.Publish(Transition("edge-2", LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal("edge-2", adapter.Requests[1].CorrelationId);
    }

    private static NativeLawResponseController ReadyController(INativeLawResponseAdapter adapter)
    {
        var controller = new NativeLawResponseController(
            new NativeLawResponseAdmissionGate(),
            adapter,
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            _ => { });
        controller.ResetForEpoch(Session, 3);
        return controller;
    }

    private static LocalPressureTierTransitionNotification Transition(
        string correlation,
        LocalPressureTier before,
        LocalPressureTier after,
        Guid? session = null,
        long loadEpoch = 3) =>
        new(session ?? Session, loadEpoch, "player-1", SourcePlayer, correlation, before, after, "north", "safehouse");

    private static NativeLawResponseResult Result(string correlationId, NativeLawResponseResultState state) =>
        new(state, correlationId, state.ToString());

    private sealed class RecordingAdapter : INativeLawResponseAdapter
    {
        private readonly NativeLawResponseResultState _state;

        public RecordingAdapter(NativeLawResponseResultState state) => _state = state;

        public List<NativeLawResponseRequest> Requests { get; } = new();

        public NativeLawResponseResult TryRequest(NativeLawResponseRequest request)
        {
            Requests.Add(request);
            return Result(request.CorrelationId, _state);
        }
    }

    private sealed class ThrowingThenAcceptedAdapter : INativeLawResponseAdapter
    {
        public List<NativeLawResponseRequest> Requests { get; } = new();

        public NativeLawResponseResult TryRequest(NativeLawResponseRequest request)
        {
            Requests.Add(request);
            if (Requests.Count == 1)
                throw new InvalidOperationException("adapter failed");

            return Result(request.CorrelationId, NativeLawResponseResultState.Accepted);
        }
    }

    private sealed class ReentrantAdapter : INativeLawResponseAdapter
    {
        public Action? Reenter { get; set; }
        public List<NativeLawResponseRequest> Requests { get; } = new();

        public NativeLawResponseResult TryRequest(NativeLawResponseRequest request)
        {
            Requests.Add(request);
            Reenter?.Invoke();
            return Result(request.CorrelationId, NativeLawResponseResultState.Accepted);
        }
    }

    private sealed class BlockingAdapter : INativeLawResponseAdapter
    {
        public List<NativeLawResponseRequest> Requests { get; } = new();
        public NativeLawResponseResult Result { get; set; } = Result("edge-1", NativeLawResponseResultState.Accepted);

        public NativeLawResponseResult TryRequest(NativeLawResponseRequest request)
        {
            Requests.Add(request);
            return Result;
        }
    }
}
