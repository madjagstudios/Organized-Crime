using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class CustodyEntryGateTests
{
    private static readonly Guid Session = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Commits_once_when_authoritative_custody_changes_false_to_true()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);

        var result = gate.TryCommit(
            Occurrence(),
            "76561190000000001",
            Session,
            11,
            isArrestedAfterCall: true);

        Assert.True(result.Accepted);
        Assert.Equal(
            "custody/v1/11111111-2222-3333-4444-555555555555/11/76561190000000001/1",
            result.CorrelationId);
        Assert.Equal(Occurrence(), result.Occurrence);
    }

    [Fact]
    public void Duplicate_postfix_and_repeated_call_while_latched_emit_once()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);

        var first = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true);
        var duplicate = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true);

        Assert.True(first.Accepted);
        Assert.False(duplicate.Accepted);
        Assert.Equal(CustodyEntryRejectReason.AlreadyCommitted, duplicate.RejectReason);
    }

    [Fact]
    public void Later_false_baseline_rearms_and_assigns_a_new_episode_correlation()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);
        Assert.True(gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true).Accepted);

        gate.ObserveNotArrested("76561190000000001", Session, 11);
        var second = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true);

        Assert.True(second.Accepted);
        Assert.Equal(
            "custody/v1/11111111-2222-3333-4444-555555555555/11/76561190000000001/2",
            second.CorrelationId);
    }

    [Fact]
    public void Missing_postfix_confirmation_does_not_latch_the_episode()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);

        var result = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, false);
        var laterConfirmation = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true);

        Assert.False(result.Accepted);
        Assert.Equal(CustodyEntryRejectReason.ConfirmationMissing, result.RejectReason);
        Assert.True(laterConfirmation.Accepted);
    }

    [Fact]
    public void True_at_baseline_without_a_false_observation_emits_nothing()
    {
        var gate = CreateGate();

        var result = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true);

        Assert.False(result.Accepted);
        Assert.Equal(CustodyEntryRejectReason.ExistingCustody, result.RejectReason);
    }

    [Fact]
    public void Throwing_native_method_with_no_postfix_leaves_the_latch_armed()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);

        var resultAfterException = gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true);

        Assert.True(resultAfterException.Accepted);
    }

    [Fact]
    public void Missing_mismatched_and_stale_identity_or_epoch_never_commit()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);

        var missing = gate.TryCommit(Occurrence(playerId: ""), "76561190000000001", Session, 11, true);
        var mismatch = gate.TryCommit(Occurrence(), "76561197984645370", Session, 11, true);
        var stale = gate.TryCommit(Occurrence(), "76561190000000001", Session, 10, true);

        Assert.Equal(CustodyEntryRejectReason.MissingIdentity, missing.RejectReason);
        Assert.Equal(CustodyEntryRejectReason.PlayerMismatch, mismatch.RejectReason);
        Assert.Equal(CustodyEntryRejectReason.StaleEpoch, stale.RejectReason);
    }

    [Fact]
    public void Reset_for_epoch_discards_the_previous_latch_and_counter()
    {
        var gate = CreateGate();
        gate.ObserveNotArrested("76561190000000001", Session, 11);
        Assert.True(gate.TryCommit(Occurrence(), "76561190000000001", Session, 11, true).Accepted);

        gate.ResetForEpoch(Session, 12);
        gate.ObserveNotArrested("76561190000000001", Session, 12);
        var result = gate.TryCommit(Occurrence(12), "76561190000000001", Session, 12, true);

        Assert.True(result.Accepted);
        Assert.EndsWith("/12/76561190000000001/1", result.CorrelationId);
    }

    private static CustodyEntryGate CreateGate()
    {
        var gate = new CustodyEntryGate();
        gate.ResetForEpoch(Session, 11);
        return gate;
    }

    private static CustodyEntryOccurrence Occurrence(long loadEpoch = 11, string playerId = "76561190000000001") =>
        new(playerId, Session, loadEpoch, "Downtown", "motelroom");
}
