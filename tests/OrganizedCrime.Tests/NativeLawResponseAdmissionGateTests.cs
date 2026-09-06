using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class NativeLawResponseAdmissionGateTests
{
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherSession = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Contracts_reject_invalid_required_values()
    {
        Assert.Throws<ArgumentException>(() => new LocalPressureTierTransitionNotification(
            Session, 3, " ", new object(), "correlation", LocalPressureTier.Noticed, LocalPressureTier.Watched, null, null));
        Assert.Throws<ArgumentException>(() => new LocalPressureTierTransitionNotification(
            Session, 3, "player", new object(), " ", LocalPressureTier.Noticed, LocalPressureTier.Watched, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeLawResponseProfile(0, true, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeLawResponseProfile(-1, true, false));
        Assert.Throws<ArgumentException>(() => new NativeLawResponseRequest(
            Session, 3, " ", new object(), "correlation", LocalPressureTier.Watched, null, null, NativeLawResponseProfile.VehicleTwoOfficerV1));
        Assert.Throws<ArgumentException>(() => new NativeLawResponseRequest(
            Session, 3, "player", new object(), " ", LocalPressureTier.Watched, null, null, NativeLawResponseProfile.VehicleTwoOfficerV1));
        Assert.Throws<ArgumentNullException>(() => new NativeLawResponseRequest(
            Session, 3, "player", null!, "correlation", LocalPressureTier.Watched, null, null, NativeLawResponseProfile.VehicleTwoOfficerV1));
        Assert.Throws<ArgumentNullException>(() => new NativeLawResponseRequest(
            Session, 3, "player", new object(), "correlation", LocalPressureTier.Watched, null, null, null!));
        Assert.Throws<ArgumentException>(() => new NativeLawResponseResult(
            NativeLawResponseResultState.Rejected, " ", "reason"));
        Assert.Throws<ArgumentException>(() => new NativeLawResponseResult(
            NativeLawResponseResultState.Rejected, "correlation", " "));
    }

    [Fact]
    public void Validated_records_preserve_positional_deconstruction()
    {
        var notification = Transition("correlation", LocalPressureTier.Noticed, LocalPressureTier.Watched);
        var (session, load, player, source, correlation, previous, current, region, property) = notification;
        Assert.Equal(Session, session);
        Assert.Equal(3, load);
        Assert.Equal("player-1", player);
        Assert.Same(notification.SourcePlayer, source);
        Assert.Equal("correlation", correlation);
        Assert.Equal(LocalPressureTier.Noticed, previous);
        Assert.Equal(LocalPressureTier.Watched, current);
        Assert.Null(region);
        Assert.Null(property);
    }

    [Fact]
    public void Validated_contract_members_do_not_expose_initializer_mutation_path()
    {
        AssertPublicShape(
            typeof(LocalPressureTierTransitionNotification),
            "SessionEpoch", "LoadEpoch", "PlayerId", "SourcePlayer", "CorrelationId",
            "PreviousTier", "CurrentTier", "Region", "PropertyCode");
        AssertPublicShape(
            typeof(NativeLawResponseProfile),
            "RequestedOfficerCount", "UseVehicle", "BeginAsSighted");
        AssertPublicShape(
            typeof(NativeLawResponseRequest),
            "SessionEpoch", "LoadEpoch", "PlayerId", "SourcePlayer", "CorrelationId",
            "TriggerTier", "Region", "PropertyCode", "Profile");
        AssertPublicShape(
            typeof(NativeLawResponseDiagnostics),
            "StationIdentity", "OfficersBefore", "OfficersConsumed", "VehiclesBefore", "VehiclesConsumed");
        AssertPublicShape(
            typeof(NativeLawResponseResult),
            "State", "CorrelationId", "Reason", "Diagnostics");
    }

    [Fact]
    public void Noticed_to_watched_is_admitted_once()
    {
        var gate = ReadyGate();
        var transition = Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched);

        var first = gate.TryBegin(transition, NativeLawResponseProfile.VehicleTwoOfficerV1);
        var duplicate = gate.TryBegin(transition, NativeLawResponseProfile.VehicleTwoOfficerV1);

        Assert.True(first.Accepted);
        Assert.NotNull(first.Request);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.InFlight, duplicate.RejectReason);
    }

    [Fact]
    public void Watched_to_critical_is_admitted()
    {
        var result = ReadyGate().TryBegin(Transition("evidence-1", LocalPressureTier.Watched, LocalPressureTier.Critical), NativeLawResponseProfile.VehicleTwoOfficerV1);
        Assert.True(result.Accepted);
    }

    [Theory]
    [InlineData(LocalPressureTier.Quiet, LocalPressureTier.Noticed)]
    [InlineData(LocalPressureTier.Watched, LocalPressureTier.Watched)]
    [InlineData(LocalPressureTier.Critical, LocalPressureTier.Watched)]
    public void Non_rising_response_edges_are_suppressed(LocalPressureTier before, LocalPressureTier after)
    {
        var result = ReadyGate().TryBegin(Transition("evidence-1", before, after), NativeLawResponseProfile.VehicleTwoOfficerV1);
        Assert.False(result.Accepted);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.UnsupportedTierEdge, result.RejectReason);
    }

    [Fact]
    public void Completion_closes_in_flight_without_automatic_retry_and_later_distinct_reentry_can_admit()
    {
        var gate = ReadyGate();
        var first = gate.TryBegin(Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched), NativeLawResponseProfile.VehicleTwoOfficerV1);
        gate.Complete(first.Request!.CorrelationId);

        var duplicate = gate.TryBegin(Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched), NativeLawResponseProfile.VehicleTwoOfficerV1);
        var later = gate.TryBegin(Transition("evidence-2", LocalPressureTier.Noticed, LocalPressureTier.Watched), NativeLawResponseProfile.VehicleTwoOfficerV1);

        Assert.Equal(NativeLawResponseAdmissionRejectReason.DuplicateCorrelation, duplicate.RejectReason);
        Assert.True(later.Accepted);
    }

    [Fact]
    public void Stale_epoch_blank_identity_and_missing_source_are_rejected()
    {
        var gate = ReadyGate();
        Assert.Equal(NativeLawResponseAdmissionRejectReason.StaleEpoch, gate.TryBegin(Transition("a", LocalPressureTier.Noticed, LocalPressureTier.Watched, OtherSession), NativeLawResponseProfile.VehicleTwoOfficerV1).RejectReason);
        Assert.Throws<ArgumentException>(() => new LocalPressureTierTransitionNotification(Session, 3, " ", new object(), "identity", LocalPressureTier.Noticed, LocalPressureTier.Watched, null, null));
        var missingSource = new LocalPressureTierTransitionNotification(Session, 3, "player-1", null, "b", LocalPressureTier.Noticed, LocalPressureTier.Watched, null, null);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.MissingSourcePlayer, gate.TryBegin(missingSource, NativeLawResponseProfile.VehicleTwoOfficerV1).RejectReason);
    }

    [Fact]
    public void Genuine_reset_clears_state_but_duplicate_same_epoch_reset_does_not()
    {
        var gate = ReadyGate();
        gate.TryBegin(Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched), NativeLawResponseProfile.VehicleTwoOfficerV1);
        gate.ResetForEpoch(Session, 3);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.InFlight, gate.TryBegin(Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched), NativeLawResponseProfile.VehicleTwoOfficerV1).RejectReason);
        gate.ResetForEpoch(Session, 4);
        Assert.True(gate.TryBegin(Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched, loadEpoch: 4), NativeLawResponseProfile.VehicleTwoOfficerV1).Accepted);
    }

    [Fact]
    public void Disposal_fails_closed()
    {
        var gate = ReadyGate();
        gate.Dispose();
        var result = gate.TryBegin(Transition("evidence-1", LocalPressureTier.Noticed, LocalPressureTier.Watched), NativeLawResponseProfile.VehicleTwoOfficerV1);
        Assert.False(result.Accepted);
        Assert.Equal(NativeLawResponseAdmissionRejectReason.Disposed, result.RejectReason);
    }

    private static NativeLawResponseAdmissionGate ReadyGate()
    {
        var gate = new NativeLawResponseAdmissionGate();
        gate.ResetForEpoch(Session, 3);
        return gate;
    }

    private static LocalPressureTierTransitionNotification Transition(string correlation, LocalPressureTier before, LocalPressureTier after, Guid? session = null, long loadEpoch = 3, object? source = null)
        => new(session ?? Session, loadEpoch, "player-1", source ?? new object(), correlation, before, after, null, null);

    private static void AssertPublicShape(Type contractType, params string[] positionalMemberNames)
    {
        var constructorParameters = Assert.Single(contractType.GetConstructors()).GetParameters();
        Assert.Equal(positionalMemberNames, constructorParameters.Select(parameter => parameter.Name));

        foreach (var memberName in positionalMemberNames)
        {
            var property = contractType.GetProperty(memberName);
            Assert.NotNull(property);
            Assert.Null(property.SetMethod);
        }
    }
}
