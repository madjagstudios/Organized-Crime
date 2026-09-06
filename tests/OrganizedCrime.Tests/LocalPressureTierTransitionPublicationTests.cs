using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureTierTransitionPublicationTests
{
    private const string CanonicalPlayerId = "76561190000000001";

    [Fact]
    public void Accepted_evidence_commits_state_and_correlation_before_publishing_transition()
    {
        using var fixture = ActiveFixture(initialHeat: 25, sink: new ReentrantAssertingSink());
        var result = fixture.ApplyCustody("custody-2");

        Assert.True(result.Accepted);
        Assert.Single(fixture.Sink.Notifications);
        Assert.Equal(LocalPressureTier.Noticed, fixture.Sink.Notifications[0].PreviousTier);
        Assert.Equal(LocalPressureTier.Watched, fixture.Sink.Notifications[0].CurrentTier);
        Assert.True(((ReentrantAssertingSink)fixture.Sink).StateWasCommittedWhenPublished);
        Assert.True(((ReentrantAssertingSink)fixture.Sink).DuplicateCorrelationWasRejectedWhenPublished);
    }

    [Fact]
    public void Throwing_transition_sink_cannot_escape_or_roll_back_evidence()
    {
        using var fixture = ActiveFixture(initialHeat: 25, sink: new ThrowingSink());
        var result = fixture.ApplyCustody("custody-2");

        Assert.True(result.Accepted);
        Assert.Equal(50, result.State!.LocalHeat);
        Assert.True(result.State.KnownOffender);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.DuplicateCorrelation,
            fixture.ApplyCustody("custody-2").RejectReason);
    }

    [Fact]
    public void Rejected_or_duplicate_evidence_publishes_nothing()
    {
        using var fixture = ActiveFixture(initialHeat: 25);

        var first = fixture.ApplyCustody("custody-2");
        var duplicate = fixture.ApplyCustody("custody-2");
        var stale = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture.Service, "custody-3"),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch - 1);

        Assert.True(first.Accepted);
        Assert.False(duplicate.Accepted);
        Assert.False(stale.Accepted);
        Assert.Single(fixture.Sink.Notifications);
    }

    [Fact]
    public void Quiet_to_noticed_publishes_even_without_consequence_filtering()
    {
        using var fixture = ActiveFixture(initialHeat: 0);

        var result = fixture.ApplyCustody("custody-1");

        Assert.True(result.Accepted);
        Assert.Single(fixture.Sink.Notifications);
        Assert.Equal(LocalPressureTier.Quiet, fixture.Sink.Notifications[0].PreviousTier);
        Assert.Equal(LocalPressureTier.Noticed, fixture.Sink.Notifications[0].CurrentTier);
    }

    [Fact]
    public void Hydration_decay_baseline_and_save_publish_nothing()
    {
        using var fixture = ActiveFixture(initialHeat: 25);

        Assert.Empty(fixture.Sink.Notifications);
        fixture.Adapter.Clock = FakeHostAdapter.CreateClock(660);
        fixture.Service.OnClockBoundary(LocalPressureClockBoundary.Hour);
        fixture.Service.OnSaveStart();
        fixture.Service.OnSaveComplete();

        Assert.Empty(fixture.Sink.Notifications);
    }

    [Fact]
    public void Genuine_preload_resets_to_incremented_load_epoch_once()
    {
        using var fixture = ActiveFixture(initialHeat: 25);

        fixture.Service.OnPreLoad();

        Assert.Single(fixture.Sink.Resets);
        Assert.Equal((fixture.Service.SessionEpoch, 2L), fixture.Sink.Resets[0]);
    }

    [Fact]
    public void Duplicate_preload_while_awaiting_load_does_not_reset_twice()
    {
        var sink = new RecordingSink();
        using var fixture = CreateFixture(new FakeHostAdapter
        {
            CanonicalHostIdentity = CanonicalPlayerId,
            Players = new[] { FakeHostAdapter.SinglePlayer(CanonicalPlayerId) },
            Clock = FakeHostAdapter.CreateClock(600)
        }, new FakeRepository(null), sink);

        fixture.Service.OnPreLoad();
        fixture.Service.OnPreLoad();

        Assert.Single(sink.Resets);
        Assert.Equal((fixture.Service.SessionEpoch, 1L), sink.Resets[0]);
    }

    [Fact]
    public void Throwing_reset_sink_is_isolated_and_genuine_preload_cleanup_still_completes()
    {
        using var fixture = ActiveFixture(initialHeat: 25, sink: new ThrowingResetSink());
        var accepted = fixture.ApplyCustody("custody-2");

        Assert.True(accepted.Accepted);

        var exception = Record.Exception(() => fixture.Service.OnPreLoad());

        Assert.Null(exception);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, fixture.Service.Phase);
        Assert.Equal(2, fixture.Service.LoadEpoch);
        Assert.Null(fixture.Service.AcceptedClockTotalGameMinutes);
        Assert.False(fixture.Service.TryGetState(CanonicalPlayerId, out _));
        Assert.Single(fixture.Sink.Resets);
        Assert.Contains(
            fixture.Logs,
            entry => entry.Contains("Local Pressure tier-transition reset failed: reset failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Evidence_publication_does_not_advance_the_accepted_clock_cursor()
    {
        using var fixture = ActiveFixture(initialHeat: 25);
        var acceptedClock = fixture.Service.AcceptedClockTotalGameMinutes;

        var result = fixture.ApplyCustody("custody-2");

        Assert.True(result.Accepted);
        Assert.Equal(acceptedClock, fixture.Service.AcceptedClockTotalGameMinutes);
    }

    private static Fixture ActiveFixture(int initialHeat, ILocalPressureTierTransitionSink? sink = null)
    {
        var adapter = new FakeHostAdapter
        {
            CanonicalHostIdentity = CanonicalPlayerId,
            Players = new[] { FakeHostAdapter.SinglePlayer(CanonicalPlayerId) },
            Clock = FakeHostAdapter.CreateClock(600)
        };
        var initialState = initialHeat == 0
            ? null
            : new LocalPressureState(initialHeat, false, null, null, null, CanonicalPlayerId, "north", "safehouse", 0);
        var repository = new FakeRepository(initialState is null
            ? null
            : new LocalPressureSaveEnvelope(1, new[] { LocalPressurePlayerRecord.FromState(initialState) }));
        return CreateFixture(adapter, repository, sink ?? new RecordingSink(), activate: true);
    }

    private static Fixture CreateFixture(
        FakeHostAdapter adapter,
        FakeRepository repository,
        ILocalPressureTierTransitionSink sink,
        bool activate = false)
    {
        var fixtureLog = new List<string>();
        var service = new LocalPressureRuntimeService(
            adapter,
            _ => repository,
            LocalPressureProfile.Moderate,
            evidenceEvaluator: (state, evidence, profile) =>
                LocalPressureTransitions.ApplyEvidence(state, evidence, profile, 25),
            log: fixtureLog.Add,
            tierTransitionSink: sink);

        var fixture = new Fixture(service, adapter, repository, sink, fixtureLog);
        if (sink is ReentrantAssertingSink reentrant)
            reentrant.Attach(fixture);

        if (activate)
        {
            service.OnPreLoad();
            service.OnLoadComplete();
            service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
            fixture.Sink.Resets.Clear();
        }

        return fixture;
    }

    private static CustodyEntryEvidence Evidence(LocalPressureRuntimeService service, string suffix) =>
        new(
            CanonicalPlayerId,
            $"custody/v1/{service.SessionEpoch:D}/{service.LoadEpoch}/{CanonicalPlayerId}/{suffix.Split('-')[1]}",
            "north",
            "safehouse");

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            LocalPressureRuntimeService service,
            FakeHostAdapter adapter,
            FakeRepository repository,
            ILocalPressureTierTransitionSink sink,
            List<string> logs)
        {
            Service = service;
            Adapter = adapter;
            Repository = repository;
            Sink = (RecordingSink)sink;
            Logs = logs;
        }

        public LocalPressureRuntimeService Service { get; }
        public FakeHostAdapter Adapter { get; }
        public FakeRepository Repository { get; }
        public RecordingSink Sink { get; }
        public List<string> Logs { get; }

        public LocalPressureEvidenceWriteResult ApplyCustody(string correlationSuffix) =>
            Service.TryApplyCustodyEvidence(
                Evidence(Service, correlationSuffix),
                Service.SessionEpoch,
                Service.LoadEpoch);

        public void Dispose() => Service.Dispose();
    }

    private class RecordingSink : ILocalPressureTierTransitionSink
    {
        public List<LocalPressureTierTransitionNotification> Notifications { get; } = new();
        public List<(Guid SessionEpoch, long LoadEpoch)> Resets { get; } = new();

        public virtual void Publish(LocalPressureTierTransitionNotification notification) => Notifications.Add(notification);

        public virtual void ResetForEpoch(Guid sessionEpoch, long loadEpoch) => Resets.Add((sessionEpoch, loadEpoch));
    }

    private sealed class ThrowingSink : RecordingSink
    {
        public override void Publish(LocalPressureTierTransitionNotification notification)
        {
            base.Publish(notification);
            throw new InvalidOperationException("sink failed");
        }
    }

    private sealed class ThrowingResetSink : RecordingSink
    {
        public override void ResetForEpoch(Guid sessionEpoch, long loadEpoch)
        {
            base.ResetForEpoch(sessionEpoch, loadEpoch);
            throw new InvalidOperationException("reset failed");
        }
    }

    private sealed class ReentrantAssertingSink : RecordingSink
    {
        private Fixture? _fixture;

        public bool StateWasCommittedWhenPublished { get; private set; }
        public bool DuplicateCorrelationWasRejectedWhenPublished { get; private set; }

        public void Attach(Fixture fixture) => _fixture = fixture;

        public override void Publish(LocalPressureTierTransitionNotification notification)
        {
            base.Publish(notification);
            Assert.NotNull(_fixture);
            StateWasCommittedWhenPublished = _fixture!.Service.TryGetState(CanonicalPlayerId, out var state) &&
                state is not null &&
                state.LocalHeat == 50 &&
                state.KnownOffender &&
                state.Revision == 1;
            DuplicateCorrelationWasRejectedWhenPublished =
                _fixture.Service.TryApplyCustodyEvidence(
                    new CustodyEntryEvidence(CanonicalPlayerId, notification.CorrelationId, "north", "safehouse"),
                    _fixture.Service.SessionEpoch,
                    _fixture.Service.LoadEpoch).RejectReason == LocalPressureEvidenceWriteRejectReason.DuplicateCorrelation;
        }
    }

    #pragma warning disable CS0067
    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        public string? ActiveSaveFolder { get; set; } = "C:\\Saves\\76561190000000001\\SaveGame_slot";
        public string? CanonicalHostIdentity { get; set; } = CanonicalPlayerId;
        public IReadOnlyList<LocalPressurePlayerSample> Players { get; set; } = Array.Empty<LocalPressurePlayerSample>();
        public LocalPressureClockSample Clock { get; set; }

        public event Action? PreLoad;
        public event Action? LoadComplete;
        public event Action? SaveStart;
        public event Action? SaveComplete;
        public event Action<LocalPressureClockBoundary>? ClockBoundary;

        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() =>
            LocalPressureClockBoundaryBindingStatus.Ready;

        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() =>
            LocalPressureHostAuthorityReadStatus.Ready;

        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            sample = Clock;
            return LocalPressureClockReadStatus.Ready;
        }

        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        {
            players = Players;
            return LocalPressurePlayerReadStatus.Ready;
        }

        public void Dispose() { }

        public static LocalPressurePlayerSample SinglePlayer(string playerCode) =>
            new("0", true, true, playerCode, "north", "safehouse", new object());

        public static LocalPressureClockSample CreateClock(long totalGameMinutes) =>
            new(totalGameMinutes, (int)(totalGameMinutes / 1440), (int)(totalGameMinutes % 1440), null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow);
    }
    #pragma warning restore CS0067

    private sealed class FakeRepository : ILocalPressureStateRepository
    {
        public FakeRepository(LocalPressureSaveEnvelope? envelope)
        {
            CurrentEnvelope = envelope ?? LocalPressureSaveEnvelope.CreateEmpty();
            LoadResult = new LocalPressureStoreLoadResult(
                true,
                envelope is null ? LocalPressureStoreLoadStatus.Empty : LocalPressureStoreLoadStatus.Loaded,
                CurrentEnvelope,
                LocalPressureStoreFailureReason.None,
                "ok");
        }

        public LocalPressureSaveEnvelope CurrentEnvelope { get; private set; }
        public LocalPressureStoreLoadResult LoadResult { get; }

        public LocalPressureStoreLoadResult Load() => LoadResult;

        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            CurrentEnvelope = new LocalPressureSaveEnvelope(
                1,
                CurrentEnvelope.Players
                    .Where(player => player.PlayerId != record.PlayerId)
                    .Append(record)
                    .ToArray());
            return new LocalPressureStoreUpdateResult(true, LocalPressureStoreUpdateStatus.Updated, CurrentEnvelope, LocalPressureStoreFailureReason.None, "ok");
        }
    }
}
