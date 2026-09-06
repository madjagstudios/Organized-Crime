using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class DispatchProofClassifierTests
{
    [Fact]
    public void Classifies_two_complete_recovered_responses_as_option_a()
    {
        var classification = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1), Evidence(2) }));

        Assert.Equal(DispatchProofDecision.PassOptionA, classification.Decision);
    }

    [Fact]
    public void Routes_capacity_shortfall_to_option_b()
    {
        var classification = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1) with { CapacityShortfallObserved = true }, Evidence(2) }));

        Assert.Equal(DispatchProofDecision.NeedsOptionB, classification.Decision);
    }

    [Fact]
    public void Stops_on_wrong_target_or_client_mutation()
    {
        var wrongTarget = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1) with { ObservedTargetIdentity = "other" }, Evidence(2) }));
        var clientMutation = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1), Evidence(2) },
            UnsupportedPlayerObserved: true));

        Assert.Equal(DispatchProofDecision.Stop, wrongTarget.Decision);
        Assert.Equal(DispatchProofDecision.Stop, clientMutation.Decision);
    }

    [Fact]
    public void Preserves_unavailable_observation_as_inconclusive()
    {
        var classification = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1) with { RecoveryObservationAvailable = false }, Evidence(2) }));

        Assert.Equal(DispatchProofDecision.Inconclusive, classification.Decision);
    }

    [Fact]
    public void Stops_when_two_rows_do_not_represent_the_two_bounded_responses()
    {
        var classification = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1), Evidence(1) }));

        Assert.Equal(DispatchProofDecision.Stop, classification.Decision);
    }

    [Fact]
    public void Classifies_available_first_response_timeout_as_option_b_without_a_second_response()
    {
        var classification = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1) with { RecoveryObservationAvailable = true, RecoveryAdequate = false } }));

        Assert.Equal(DispatchProofDecision.NeedsOptionB, classification.Decision);
    }

    [Fact]
    public void Classifies_unavailable_first_response_timeout_as_inconclusive()
    {
        var classification = DispatchProofClassifier.Classify(new(
            "canonical",
            new[] { Evidence(1) with { RecoveryObservationAvailable = false, RecoveryAdequate = false } }));

        Assert.Equal(DispatchProofDecision.Inconclusive, classification.Decision);
    }

    [Fact]
    public void Uses_dispatched_station_consumption_instead_of_global_remaining_pool_count()
    {
        var response = Evidence(1) with
        {
            DispatchedStationIdentity = "dispatched",
            BeforeStations = new[]
            {
                Station("dispatched", new[] { "a", "b" }, 1),
                Station("other", new[] { "x" }, 1)
            },
            AfterStations = new[]
            {
                Station("dispatched", new[] { "a" }, 0),
                Station("other", Array.Empty<string>(), 0)
            }
        };

        var classification = DispatchProofClassifier.Classify(new("canonical", new[] { response, Evidence(2) }));

        Assert.Equal(DispatchProofDecision.NeedsOptionB, classification.Decision);
    }

    private static DispatchResponseEvidence Evidence(int responseNumber) => new(
        ResponseNumber: responseNumber,
        ExpectedTargetIdentity: "canonical",
        ObservedTargetIdentity: "canonical",
        DispatchCallCompleted: true,
        TargetAttributionAvailable: true,
        VehicleResponseObserved: true,
        RecoveryObservationAvailable: true,
        RecoveryAdequate: true,
        DeathObservationAvailable: true,
        NaturalDeathObserved: false,
        CapacityShortfallObserved: false,
        DuplicateInvocationObserved: false,
        UnrelatedPlayerEffectObserved: false,
        PersistentExceptionObserved: false,
        BeforeStations: new[]
        {
            new StationSnapshot(
                "station",
                ProofObservation<IReadOnlyList<string>>.Available(new[] { "officer-1", "officer-2" }),
                ProofObservation<int>.Available(1),
                ProofObservation<IReadOnlyList<string>>.Available(new[] { "vehicle" }),
                ProofObservation<IReadOnlyList<string>>.Unavailable("deployedVehicles wrapper unavailable"),
                ProofObservation<float>.Available(0f))
        },
        AfterStations: new[]
        {
            new StationSnapshot(
                "station",
                ProofObservation<IReadOnlyList<string>>.Available(Array.Empty<string>()),
                ProofObservation<int>.Available(0),
                ProofObservation<IReadOnlyList<string>>.Available(new[] { "vehicle" }),
                ProofObservation<IReadOnlyList<string>>.Unavailable("deployedVehicles wrapper unavailable"),
                ProofObservation<float>.Available(1f))
        },
        BeforeResponders: Array.Empty<OfficerSnapshot>(),
        AfterResponders: Array.Empty<OfficerSnapshot>(),
        BeforePlayers: Array.Empty<PlayerSnapshot>(),
        AfterPlayers: Array.Empty<PlayerSnapshot>())
    {
        DispatchedStationIdentity = "station"
    };

    private static StationSnapshot Station(string identity, IReadOnlyList<string> officers, int vehicles) => new(
        identity,
        ProofObservation<IReadOnlyList<string>>.Available(officers),
        ProofObservation<int>.Available(vehicles),
        ProofObservation<IReadOnlyList<string>>.Available(new[] { "vehicle" }),
        ProofObservation<IReadOnlyList<string>>.Unavailable("private deployedVehicles wrapper unavailable"),
        ProofObservation<float>.Available(1f));
}
