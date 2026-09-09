using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureCustodyEvidenceWriterTests
{
    private const string CanonicalPlayerId = "76561190000000001";

    [Fact]
    public void Active_host_intake_applies_arrest_once_and_publishes_in_memory_state()
    {
        using var fixture = ActiveFixture();

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.True(result.Accepted, result.Message);
        Assert.Equal(25, result.State!.LocalHeat);
        Assert.True(result.State.KnownOffender);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(1, fixture.EvidenceApplyCalls);
        Assert.Equal(fixture.InitialClockMinutes, fixture.Service.AcceptedClockTotalGameMinutes);
        Assert.Equal(result.State, ReadState(fixture.Service, CanonicalPlayerId));
    }

    [Fact]
    public void Duplicate_correlation_is_rejected_before_a_second_model_call()
    {
        using var fixture = ActiveFixture();
        var correlation = Evidence(fixture, 1);

        var first = fixture.Service.TryApplyCustodyEvidence(correlation, fixture.Service.SessionEpoch, fixture.Service.LoadEpoch);
        var duplicate = fixture.Service.TryApplyCustodyEvidence(correlation, fixture.Service.SessionEpoch, fixture.Service.LoadEpoch);

        Assert.True(first.Accepted, first.Message);
        Assert.False(duplicate.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.DuplicateCorrelation, duplicate.RejectReason);
        Assert.Equal(1, fixture.EvidenceApplyCalls);
        Assert.Equal(1, ReadState(fixture.Service, CanonicalPlayerId)!.Revision);
    }

    [Fact]
    public void Placeholder_player_code_is_accepted_only_through_the_valid_save_identity()
    {
        using var fixture = ActiveFixture(playerCode: "0");

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, fixture.Service.Phase);
        fixture.Adapter.Players = new[] { FakeHostAdapter.SinglePlayer(CanonicalPlayerId) };
        fixture.Service.PumpReadiness();

        var snapshotResult = fixture.Service.TryGetActiveEvidenceSnapshot(out var snapshot);
        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            snapshot.SessionEpoch,
            snapshot.LoadEpoch);

        Assert.True(snapshotResult);
        Assert.Equal(CanonicalPlayerId, snapshot.PlayerId);
        Assert.True(result.Accepted, result.Message);
        Assert.True(fixture.Service.TryGetState(CanonicalPlayerId, out _));
        Assert.False(fixture.Service.TryGetState("0", out _));
    }

    [Fact]
    public void Evidence_clock_read_does_not_advance_decay_cursor_or_consume_elapsed_decay()
    {
        var initial = new LocalPressureState(30, false, 9, null, 9, CanonicalPlayerId, "north", "old", 4);
        LocalPressureDecayInput? decayInput = null;
        using var fixture = ActiveFixture(
            initialState: initial,
            clockMinutes: 600,
            decayEvaluator: (input, profile) =>
            {
                decayInput = input;
                return LocalPressureDecay.Evaluate(input, profile);
            });
        var before = fixture.Service.AcceptedClockTotalGameMinutes;

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.True(result.Accepted, result.Message);
        Assert.Equal(before, fixture.Service.AcceptedClockTotalGameMinutes);
        Assert.Equal(9, result.State!.LastDecayEvaluation);

        fixture.Adapter.Clock = FakeHostAdapter.CreateClock(660);
        fixture.Service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(9, decayInput!.State.LastDecayEvaluation);
        Assert.Equal(11, decayInput.CurrentGameTimeHours);
    }

    [Fact]
    public void Invalid_context_is_stripped_but_safe_global_heat_still_applies()
    {
        using var fixture = ActiveFixture();

        var result = fixture.Service.TryApplyCustodyEvidence(
            new CustodyEntryEvidence(CanonicalPlayerId, Correlation(fixture, 1), "\0", "safehouse"),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.True(result.Accepted, result.Message);
        Assert.Null(result.State!.Region);
        Assert.Null(result.State.PropertyCode);
        Assert.Equal(25, result.State.LocalHeat);
    }

    [Fact]
    public void Older_occurrence_context_cannot_roll_back_the_last_accepted_context()
    {
        var initial = new LocalPressureState(10, false, 20, null, 20, CanonicalPlayerId, "north", "old", 4);
        using var fixture = ActiveFixture(initialState: initial, clockMinutes: 600);

        var result = fixture.Service.TryApplyCustodyEvidence(
            new CustodyEntryEvidence(CanonicalPlayerId, Correlation(fixture, 1), "south", "new"),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.True(result.Accepted, result.Message);
        Assert.Equal("north", result.State!.Region);
        Assert.Equal("old", result.State.PropertyCode);
    }

    [Fact]
    public void Stale_epoch_and_all_non_active_phases_cause_zero_mutation()
    {
        using var fixture = ActiveFixture();
        var stale = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch - 1);
        Assert.False(stale.Accepted);

        fixture.Service.OnSaveStart();
        var saving = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 2),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(saving.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, saving.RejectReason);
        Assert.Null(ReadState(fixture.Service, CanonicalPlayerId));
        Assert.Equal(0, fixture.EvidenceApplyCalls);
    }

    [Fact]
    public void Host_clock_failure_rejects_before_mutation()
    {
        using var fixture = ActiveFixture();
        fixture.Adapter.ThrowOnClockRead = true;

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.ClockUnavailable, result.RejectReason);
        Assert.Equal(0, fixture.EvidenceApplyCalls);
        Assert.Null(ReadState(fixture.Service, CanonicalPlayerId));
    }

    [Fact]
    public void Faulted_host_authority_returns_adapter_failure_without_evidence_mutation()
    {
        using var fixture = ActiveFixture();
        fixture.Adapter.HostAuthorityFaulted = true;

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.AdapterFailure, result.RejectReason);
        Assert.Equal(0, fixture.EvidenceApplyCalls);
        Assert.Null(ReadState(fixture.Service, CanonicalPlayerId));
    }

    [Fact]
    public void Inactive_runtime_rejects_evidence_before_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            CanonicalHostIdentity = CanonicalPlayerId,
            Players = new[] { FakeHostAdapter.SinglePlayer("0") },
            Clock = FakeHostAdapter.CreateClock(600)
        };
        var repository = new FakeRepository(null);
        using var service = new LocalPressureRuntimeService(adapter, _ => repository);

        var result = service.TryApplyCustodyEvidence(
            new CustodyEntryEvidence(CanonicalPlayerId, "custody/v1/invalid/1/player/1", null, null),
            service.SessionEpoch,
            service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, result.RejectReason);
        Assert.Null(ReadState(service, CanonicalPlayerId));
    }

    [Fact]
    public void Missing_correlation_rejects_before_mutation()
    {
        using var fixture = ActiveFixture();

        var result = fixture.Service.TryApplyCustodyEvidence(
            new CustodyEntryEvidence(CanonicalPlayerId, "", null, null),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.MissingCorrelation, result.RejectReason);
        Assert.Equal(0, fixture.EvidenceApplyCalls);
    }

    [Fact]
    public void Custody_evidence_does_not_depend_on_active_pursuit_input()
    {
        using var fixture = ActiveFixture();
        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.True(result.Accepted, result.Message);
        Assert.Equal(1, fixture.EvidenceApplyCalls);
    }

    [Fact]
    public void Unsafe_identity_rejects_before_mutation()
    {
        using var fixture = ActiveFixture();
        fixture.Adapter.CanonicalHostIdentity = null;

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.MissingIdentity, result.RejectReason);
        Assert.Equal(0, fixture.EvidenceApplyCalls);
        Assert.Null(ReadState(fixture.Service, CanonicalPlayerId));
    }

    [Fact]
    public void Multiplayer_quarantine_rejects_evidence_before_mutation()
    {
        using var fixture = ActiveFixture();
        fixture.Adapter.Players = new[]
        {
            FakeHostAdapter.SinglePlayer("0"),
            FakeHostAdapter.SinglePlayer("0")
        };

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.UnsupportedMultiplayer, result.RejectReason);
        Assert.Equal(LocalPressureRuntimePhase.Quarantined, fixture.Service.Phase);
        Assert.Equal(0, fixture.EvidenceApplyCalls);
    }

    [Fact]
    public void Ready_steam_identity_mismatch_rejects_evidence_before_mutation()
    {
        using var fixture = ActiveFixture();
        fixture.Adapter.Players = new[] { FakeHostAdapter.SinglePlayer("76561190000000002") };

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.PlayerMismatch, result.RejectReason);
        Assert.Equal(0, fixture.EvidenceApplyCalls);
    }

    [Fact]
    public void Save_complete_persists_the_accepted_state_through_oc30_repository()
    {
        using var fixture = ActiveFixture();
        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);
        Assert.True(result.Accepted, result.Message);

        fixture.Service.OnSaveStart();
        fixture.Service.OnSaveComplete();

        Assert.Equal(1, fixture.Repository.UpdateCalls);
        Assert.Equal(CanonicalPlayerId, fixture.Repository.UpdatedRecord!.PlayerId);
        Assert.Equal(25, fixture.Repository.UpdatedRecord.LocalHeat);
    }

    [Fact]
    public void A_new_load_epoch_does_not_deduplicate_the_prior_epoch_correlation()
    {
        using var fixture = ActiveFixture();
        var firstCorrelation = Correlation(fixture, 1);
        var first = fixture.Service.TryApplyCustodyEvidence(
            new CustodyEntryEvidence(CanonicalPlayerId, firstCorrelation, null, null),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);
        Assert.True(first.Accepted, first.Message);

        var oldEpoch = fixture.Service.LoadEpoch;
        fixture.Service.OnPreLoad();
        fixture.Service.OnLoadComplete();
        fixture.Adapter.Clock = FakeHostAdapter.CreateClock(600);
        fixture.Service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        var oldSubmission = fixture.Service.TryApplyCustodyEvidence(
            new CustodyEntryEvidence(CanonicalPlayerId, firstCorrelation, null, null),
            fixture.Service.SessionEpoch,
            oldEpoch);
        var newSubmission = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(oldSubmission.Accepted);
        Assert.True(newSubmission.Accepted, newSubmission.Message);
        Assert.Equal(2, fixture.EvidenceApplyCalls);
    }

    [Fact]
    public void Evidence_transition_exception_is_contained_without_state_or_correlation_publication()
    {
        using var fixture = ActiveFixture(
            evidenceEvaluator: (_, _, _) => throw new InvalidOperationException("transition failed"));

        var result = fixture.Service.TryApplyCustodyEvidence(
            Evidence(fixture, 1),
            fixture.Service.SessionEpoch,
            fixture.Service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.AdapterFailure, result.RejectReason);
        Assert.Null(ReadState(fixture.Service, CanonicalPlayerId));
        Assert.Equal(1, fixture.EvidenceApplyCalls);
    }

    private static LocalPressureState? ReadState(LocalPressureRuntimeService service, string playerId) =>
        service.TryGetState(playerId, out var state) ? state : null;

    private static CustodyEntryEvidence Evidence(Fixture fixture, long episode) =>
        new(CanonicalPlayerId, Correlation(fixture, episode), null, null);

    private static string Correlation(Fixture fixture, long episode) =>
        $"custody/v1/{fixture.Service.SessionEpoch:D}/{fixture.Service.LoadEpoch}/{CanonicalPlayerId}/{episode}";

    private static Fixture ActiveFixture(
        LocalPressureState? initialState = null,
        string playerCode = CanonicalPlayerId,
        long clockMinutes = 600,
        LocalPressureDecayEvaluator? decayEvaluator = null,
        LocalPressureEvidenceEvaluator? evidenceEvaluator = null)
    {
        var adapter = new FakeHostAdapter
        {
            CanonicalHostIdentity = CanonicalPlayerId,
            Players = new[] { FakeHostAdapter.SinglePlayer(playerCode) },
            Clock = FakeHostAdapter.CreateClock(clockMinutes)
        };
        var repository = new FakeRepository(initialState is null
            ? null
            : new LocalPressureSaveEnvelope(1, new[] { LocalPressurePlayerRecord.FromState(initialState) }));
        var applyCalls = 0;
        var service = new LocalPressureRuntimeService(
            adapter,
            _ => repository,
            LocalPressureProfile.Moderate,
            decayEvaluator,
            _ => { },
            (state, evidence, profile) =>
            {
                applyCalls++;
                return evidenceEvaluator is null
                    ? LocalPressureTransitions.ApplyEvidence(state, evidence, profile)
                    : evidenceEvaluator(state, evidence, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        return new Fixture(service, adapter, repository, () => applyCalls, clockMinutes);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Func<int> _evidenceApplyCalls;

        public Fixture(
            LocalPressureRuntimeService service,
            FakeHostAdapter adapter,
            FakeRepository repository,
            Func<int> evidenceApplyCalls,
            long initialClockMinutes)
        {
            Service = service;
            Adapter = adapter;
            Repository = repository;
            _evidenceApplyCalls = evidenceApplyCalls;
            InitialClockMinutes = initialClockMinutes;
        }

        public LocalPressureRuntimeService Service { get; }
        public FakeHostAdapter Adapter { get; }
        public FakeRepository Repository { get; }
        public int EvidenceApplyCalls => _evidenceApplyCalls();
        public long InitialClockMinutes { get; }

        public void Dispose() => Service.Dispose();
    }

    #pragma warning disable CS0067
    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        public bool IsAuthoritativeHost { get; set; } = true;
        public string? ActiveSaveFolder { get; set; } = "C:\\Saves\\76561190000000001\\SaveGame_slot";
        public string? CanonicalHostIdentity { get; set; } = CanonicalPlayerId;
        public IReadOnlyList<LocalPressurePlayerSample> Players { get; set; } = Array.Empty<LocalPressurePlayerSample>();
        public LocalPressureClockSample Clock { get; set; }
        public bool ThrowOnClockRead { get; set; }
        public bool HostAuthorityFaulted { get; set; }

        public event Action? PreLoad;
        public event Action? LoadComplete;
        public event Action? SaveStart;
        public event Action? SaveComplete;
        public event Action<LocalPressureClockBoundary>? ClockBoundary;

        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() =>
            LocalPressureClockBoundaryBindingStatus.Ready;

        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() =>
            HostAuthorityFaulted
                ? LocalPressureHostAuthorityReadStatus.Faulted
                : IsAuthoritativeHost
                ? LocalPressureHostAuthorityReadStatus.Ready
                : LocalPressureHostAuthorityReadStatus.NotAuthoritative;

        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            if (ThrowOnClockRead)
                throw new InvalidOperationException("clock unavailable");
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
            LoadResult = new LocalPressureStoreLoadResult(true, envelope is null ? LocalPressureStoreLoadStatus.Empty : LocalPressureStoreLoadStatus.Loaded, CurrentEnvelope, LocalPressureStoreFailureReason.None, "ok");
        }

        public LocalPressureSaveEnvelope CurrentEnvelope { get; private set; }
        public LocalPressureStoreLoadResult LoadResult { get; }
        public int UpdateCalls { get; private set; }
        public LocalPressurePlayerRecord? UpdatedRecord { get; private set; }

        public LocalPressureStoreLoadResult Load() => LoadResult;

        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            UpdateCalls++;
            UpdatedRecord = record;
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
