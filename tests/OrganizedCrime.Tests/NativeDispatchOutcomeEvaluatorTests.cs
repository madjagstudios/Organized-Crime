using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class NativeDispatchOutcomeEvaluatorTests
{
    [Fact]
    public void Exact_two_officer_and_one_vehicle_delta_is_accepted()
    {
        var result = NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station", new[] { "a", "b", "c" }, 2),
            Snapshot("station", new[] { "c" }, 1),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-1");

        Assert.Equal(NativeLawResponseResultState.Accepted, result.State);
        Assert.Equal(2, result.Diagnostics!.OfficersConsumed);
        Assert.Equal(1, result.Diagnostics.VehiclesConsumed);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public void Unexpected_delta_is_inconclusive_and_never_accepted(int officersConsumed, int vehiclesConsumed)
    {
        var result = EvaluateDelta(officersConsumed, vehiclesConsumed);
        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, result.State);
    }

    [Fact]
    public void Different_station_identity_is_inconclusive()
    {
        var result = NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station-a", new[] { "a", "b" }, 1),
            Snapshot("station-b", Array.Empty<string>(), 0),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-1");

        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, result.State);
    }

    [Fact]
    public void Duplicate_or_blank_officer_identity_is_inconclusive()
    {
        var duplicate = NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station", new[] { "a", "a", "b" }, 2),
            Snapshot("station", new[] { "b" }, 1),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-1");
        var blank = NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station", new[] { "a", " ", "b" }, 2),
            Snapshot("station", new[] { "b" }, 1),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-2");

        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, duplicate.State);
        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, blank.State);
    }

    [Fact]
    public void Missing_observations_are_inconclusive()
    {
        var missingBefore = NativeDispatchOutcomeEvaluator.Evaluate(
            null!,
            Snapshot("station", Array.Empty<string>(), 0),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-1");
        var missingAfter = NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station", new[] { "a", "b" }, 1),
            null!,
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-2");
        var missingOfficerList = NativeDispatchOutcomeEvaluator.Evaluate(
            new NativeDispatchStationSnapshot("station", null!, 1),
            Snapshot("station", Array.Empty<string>(), 0),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-3");

        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, missingBefore.State);
        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, missingAfter.State);
        Assert.Equal(NativeLawResponseResultState.InconclusiveOutcome, missingOfficerList.State);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void Preflight_insufficient_capacity_is_unavailable_capacity(int officerCount, int vehicleCount)
    {
        var result = NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station", Enumerable.Range(0, officerCount).Select(index => $"officer-{index}").ToArray(), vehicleCount),
            Snapshot("station", Array.Empty<string>(), 0),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-1");

        Assert.Equal(NativeLawResponseResultState.UnavailableCapacity, result.State);
        Assert.Equal(officerCount, result.Diagnostics!.OfficersBefore);
        Assert.Equal(vehicleCount, result.Diagnostics.VehiclesBefore);
    }

    private static NativeLawResponseResult EvaluateDelta(int officersConsumed, int vehiclesConsumed)
    {
        var beforeOfficers = new[] { "a", "b", "c", "d" };
        var afterOfficers = beforeOfficers.Skip(officersConsumed).ToArray();
        return NativeDispatchOutcomeEvaluator.Evaluate(
            Snapshot("station", beforeOfficers, 2),
            Snapshot("station", afterOfficers, 2 - vehiclesConsumed),
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            "edge-1");
    }

    private static NativeDispatchStationSnapshot Snapshot(
        string stationIdentity,
        IReadOnlyList<string> officerIdentities,
        int availableVehicleCount) =>
        new(stationIdentity, officerIdentities, availableVehicleCount);
}
