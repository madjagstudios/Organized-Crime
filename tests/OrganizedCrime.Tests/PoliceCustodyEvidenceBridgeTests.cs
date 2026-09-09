using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class PoliceCustodyEvidenceBridgeTests
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Prefix_arrest_state_read_failure_is_ineligible()
    {
        using var fixture = CreateFixture();

        var state = fixture.Bridge.CapturePrefix(
            fixture.Observation(false, arrestStateReadSucceeded: false));

        Assert.False(state.Eligible);
        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void Postfix_arrest_state_read_failure_does_not_mutate_or_consume_the_episode()
    {
        using var fixture = CreateFixture();
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));

        fixture.Bridge.ConfirmPostfix(
            fixture.Observation(true, arrestStateReadSucceeded: false),
            state);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
        Assert.Null(fixture.Writer.LastEvidence);
    }

    [Fact]
    public void Later_valid_false_to_true_commits_once_after_a_failed_postfix_read()
    {
        using var fixture = CreateFixture();
        var failedPostfixState = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(
            fixture.Observation(true, arrestStateReadSucceeded: false),
            failedPostfixState);

        var validState = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), validState);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
        Assert.EndsWith("/1", fixture.Writer.LastEvidence!.CorrelationId);
    }

    [Fact]
    public void False_to_true_submits_one_custody_evidence_event()
    {
        using var fixture = CreateFixture();

        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
        Assert.Equal(PlayerId, fixture.Writer.LastEvidence!.PlayerId);
        Assert.Equal("custody/v1/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/4/76561190000000001/1", fixture.Writer.LastEvidence.CorrelationId);
    }

    [Fact]
    public void Repeated_calls_while_arrested_do_not_submit_twice()
    {
        using var fixture = CreateFixture();
        var first = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), first);

        var duplicate = fixture.Bridge.CapturePrefix(fixture.Observation(true));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), duplicate);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void Later_false_baseline_rearms_a_new_custody_episode()
    {
        using var fixture = CreateFixture();
        var first = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), first);

        var second = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), second);

        Assert.Equal(2, fixture.Writer.ApplyCalls);
        Assert.EndsWith("/2", fixture.Writer.LastEvidence!.CorrelationId);
    }

    [Fact]
    public void True_at_prefix_baseline_emits_nothing()
    {
        using var fixture = CreateFixture();

        var state = fixture.Bridge.CapturePrefix(fixture.Observation(true));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void False_to_false_confirmation_emits_nothing()
    {
        using var fixture = CreateFixture();
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));

        fixture.Bridge.ConfirmPostfix(fixture.Observation(false), state);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void A_stale_epoch_between_prefix_and_postfix_emits_nothing()
    {
        using var fixture = CreateFixture();
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Writer.Snapshot = fixture.Writer.Snapshot with { LoadEpoch = 5 };

        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void Unsupported_instance_or_authority_is_rejected()
    {
        using var fixture = CreateFixture();

        var wrongInstance = fixture.Bridge.CapturePrefix(fixture.Observation(false, sourcePlayer: new object()));
        var notHost = fixture.Bridge.CapturePrefix(fixture.Observation(false, isHostOwned: false));
        var noConnection = fixture.Bridge.CapturePrefix(fixture.Observation(false, hasConnection: false));

        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), wrongInstance);
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), notHost);
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), noConnection);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void Placeholder_player_code_uses_the_snapshot_identity()
    {
        using var fixture = CreateFixture(playerCode: "0");
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false, playerCode: "0"));

        fixture.Bridge.ConfirmPostfix(fixture.Observation(true, playerCode: "0"), state);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
        Assert.Equal(PlayerId, fixture.Writer.LastEvidence!.PlayerId);
    }

    [Fact]
    public void A_ready_steam_code_mismatch_is_rejected()
    {
        using var fixture = CreateFixture(playerCode: "76561190000000002");
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false, playerCode: "76561190000000002"));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true, playerCode: "76561190000000002"), state);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void Unsafe_context_is_stripped_without_rejecting_global_evidence()
    {
        using var fixture = CreateFixture();
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false, region: "\0", propertyCode: "safehouse"));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
        Assert.Null(fixture.Writer.LastEvidence!.Region);
        Assert.Null(fixture.Writer.LastEvidence.PropertyCode);
    }

    [Fact]
    public void Writer_exception_is_contained_without_retry()
    {
        using var fixture = CreateFixture();
        fixture.Writer.ThrowOnApply = true;
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));

        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public void Disposal_makes_callbacks_inert()
    {
        var fixture = CreateFixture();
        fixture.Bridge.Dispose();
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("OnDie")]
    public void Cause_label_does_not_change_the_common_custody_transition(string cause)
    {
        using var fixture = CreateFixture();
        var state = fixture.Bridge.CapturePrefix(fixture.Observation(false));
        fixture.Bridge.ConfirmPostfix(fixture.Observation(true), state);

        Assert.Equal(1, fixture.Writer.ApplyCalls);
        Assert.Contains(cause, new[] { "ordinary", "OnDie" });
    }

    private static Fixture CreateFixture(string playerCode = "0")
    {
        var writer = new FakeWriter
        {
            Snapshot = new LocalPressureActiveEvidenceSnapshot(Session, 4, PlayerId, null)
        };
        var sourcePlayer = new object();
        writer.Snapshot = writer.Snapshot with { SupportedPlayer = sourcePlayer };
        return new Fixture(new PoliceCustodyEvidenceBridge(new CustodyEntryGate(), writer), writer, sourcePlayer, playerCode);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(PoliceCustodyEvidenceBridge bridge, FakeWriter writer, object sourcePlayer, string playerCode)
        {
            Bridge = bridge;
            Writer = writer;
            SourcePlayer = sourcePlayer;
            PlayerCode = playerCode;
        }

        public PoliceCustodyEvidenceBridge Bridge { get; }
        public FakeWriter Writer { get; }
        public object SourcePlayer { get; }
        public string PlayerCode { get; }

        public CustodyPlayerObservation Observation(
            bool isArrested,
            object? sourcePlayer = null,
            bool isHostOwned = true,
            bool hasConnection = true,
            string? playerCode = null,
            string? region = "Downtown",
            string? propertyCode = "motelroom",
            bool arrestStateReadSucceeded = true) =>
            new(
                sourcePlayer ?? SourcePlayer,
                playerCode ?? PlayerCode,
                isHostOwned,
                hasConnection,
                isArrested,
                region,
                propertyCode,
                arrestStateReadSucceeded);

        public void Dispose() => Bridge.Dispose();
    }

    private sealed class FakeWriter : ILocalPressureCustodyEvidenceWriter
    {
        public LocalPressureActiveEvidenceSnapshot Snapshot { get; set; }
        public int ApplyCalls { get; private set; }
        public CustodyEntryEvidence? LastEvidence { get; private set; }
        public bool ThrowOnApply { get; set; }

        public bool TryGetActiveEvidenceSnapshot(out LocalPressureActiveEvidenceSnapshot snapshot)
        {
            snapshot = Snapshot;
            return true;
        }

        public LocalPressureEvidenceWriteResult TryApplyCustodyEvidence(CustodyEntryEvidence evidence, Guid sessionEpoch, long loadEpoch)
        {
            ApplyCalls++;
            LastEvidence = evidence;
            if (ThrowOnApply)
                throw new InvalidOperationException("writer failed");
            return new LocalPressureEvidenceWriteResult(true, LocalPressureEvidenceWriteRejectReason.None, LocalPressureState.Quiet(evidence.PlayerId), "accepted");
        }
    }
}
