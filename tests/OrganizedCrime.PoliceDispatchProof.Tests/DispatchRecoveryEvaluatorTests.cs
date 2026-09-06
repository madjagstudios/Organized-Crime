using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class DispatchRecoveryEvaluatorTests
{
    [Fact]
    public void Untouched_second_station_cannot_satisfy_dispatched_station_recovery()
    {
        var evidence = Evidence(
            before: Station("dispatched", new[] { "a", "b" }, 1),
            immediate: Station("dispatched", new[] { "a" }, 0),
            recovery: new[]
            {
                Station("dispatched", new[] { "a" }, 0),
                Station("untouched", new[] { "x", "y" }, 1)
            });

        var result = DispatchRecoveryEvaluator.Evaluate(evidence);

        Assert.True(result.RequiredObservationsAvailable);
        Assert.False(result.RecoveryAdequate);
    }

    [Fact]
    public void Same_counts_with_different_officer_identities_do_not_prove_recovery()
    {
        var evidence = Evidence(
            before: Station("dispatched", new[] { "a", "b" }, 1),
            immediate: Station("dispatched", new[] { "a" }, 0),
            recovery: new[] { Station("dispatched", new[] { "a", "c" }, 1) });

        var result = DispatchRecoveryEvaluator.Evaluate(evidence);

        Assert.True(result.RequiredObservationsAvailable);
        Assert.False(result.RecoveryAdequate);
    }

    [Fact]
    public void Exact_consumed_identities_and_restored_vehicle_capacity_prove_ordinary_recovery()
    {
        var evidence = Evidence(
            before: Station("dispatched", new[] { "a", "b" }, 1),
            immediate: Station("dispatched", Array.Empty<string>(), 0),
            recovery: new[] { Station("dispatched", new[] { "a", "b" }, 1) });

        var result = DispatchRecoveryEvaluator.Evaluate(evidence);

        Assert.True(result.RequiredObservationsAvailable);
        Assert.True(result.RecoveryAdequate);
        Assert.Equal(new[] { "a", "b" }, result.ConsumedOfficerIdentities);
        Assert.Equal(2, evidence.ConsumedOfficerCount.Value);
    }

    [Fact]
    public void Unavailable_recovery_observation_is_not_zero_or_recovered()
    {
        var evidence = Evidence(
            before: Station("dispatched", new[] { "a", "b" }, 1),
            immediate: Station("dispatched", Array.Empty<string>(), 0),
            recovery: Array.Empty<StationSnapshot>());

        var result = DispatchRecoveryEvaluator.Evaluate(evidence);

        Assert.False(result.RequiredObservationsAvailable);
        Assert.False(result.RecoveryAdequate);
    }

    private static DispatchResponseEvidence Evidence(
        StationSnapshot before,
        StationSnapshot immediate,
        IReadOnlyList<StationSnapshot> recovery) => new(
        ResponseNumber: 1,
        ExpectedTargetIdentity: "canonical",
        ObservedTargetIdentity: "canonical",
        DispatchCallCompleted: true,
        TargetAttributionAvailable: true,
        VehicleResponseObserved: true,
        RecoveryObservationAvailable: false,
        RecoveryAdequate: false,
        DeathObservationAvailable: true,
        NaturalDeathObserved: false,
        CapacityShortfallObserved: false,
        DuplicateInvocationObserved: false,
        UnrelatedPlayerEffectObserved: false,
        PersistentExceptionObserved: false,
        BeforeStations: new[] { before },
        AfterStations: new[] { immediate },
        BeforeResponders: Array.Empty<OfficerSnapshot>(),
        AfterResponders: Array.Empty<OfficerSnapshot>(),
        BeforePlayers: Array.Empty<PlayerSnapshot>(),
        AfterPlayers: Array.Empty<PlayerSnapshot>())
    {
        DispatchedStationIdentity = "dispatched",
        RecoveryStations = recovery
    };

    private static StationSnapshot Station(string identity, IReadOnlyList<string> officers, int vehicles) => new(
        identity,
        ProofObservation<IReadOnlyList<string>>.Available(officers),
        ProofObservation<int>.Available(vehicles),
        ProofObservation<IReadOnlyList<string>>.Available(new[] { "vehicle" }),
        ProofObservation<IReadOnlyList<string>>.Unavailable("private deployedVehicles wrapper unavailable"),
        ProofObservation<float>.Available(1f));
}
