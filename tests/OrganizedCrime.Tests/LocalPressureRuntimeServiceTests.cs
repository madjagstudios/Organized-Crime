using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureRuntimeServiceTests
{
    [Fact]
    public void Duplicate_preload_is_a_typed_no_op_until_a_later_genuine_preload()
    {
        var adapter = new FakeHostAdapter();
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);
        var sessionEpoch = service.SessionEpoch;

        service.OnPreLoad();
        service.OnPreLoad();

        Assert.Equal(sessionEpoch, service.SessionEpoch);
        Assert.Equal(1, service.LoadEpoch);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.Equal(0, repository.LoadCalls);

        service.OnLoadComplete();
        service.OnPreLoad();

        Assert.Equal(2, service.LoadEpoch);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, service.Phase);
    }

    [Fact]
    public void Duplicate_load_complete_hydrates_once_and_preserves_accepted_baseline()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Clock = FakeHostAdapter.CreateClock(120);
        service.OnLoadComplete();

        Assert.Equal(1, repository.LoadCalls);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.Equal(0, decayCalls);

        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(1, decayCalls);
        Assert.Equal(2, ReadState(service, "player-1")!.LastDecayEvaluation);
    }

    [Fact]
    public void First_clock_after_load_is_a_strictly_non_mutating_baseline()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(120)
        };
        var state = new LocalPressureState(10, false, 1, null, 2, "player-1", "north", "safehouse", 4);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(0, decayCalls);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void Save_folder_identity_is_canonical_when_runtime_player_code_is_not_ready()
    {
        const string canonicalIdentity = "76561190000000001";
        var adapter = new FakeHostAdapter
        {
            CanonicalHostIdentity = canonicalIdentity,
            Players = new[]
            {
                new LocalPressurePlayerSample(
                    PlayerId: "0",
                    IsHostOwned: true,
                    IsLocalPlayer: true,
                    PlayerCode: "0",
                    Region: null,
                    PropertyCode: null)
            },
            Clock = FakeHostAdapter.CreateClock(120)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);

        adapter.Players = new[] { FakeHostAdapter.SinglePlayer(canonicalIdentity) };
        service.PumpReadiness();
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.True(service.TryGetState(canonicalIdentity, out var state));
        Assert.Null(ReadState(service, "0"));
        Assert.Equal(canonicalIdentity, state!.PlayerId);
        Assert.Equal(canonicalIdentity, repository.UpdatedRecord!.PlayerId);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public void Ready_runtime_identity_mismatch_with_save_folder_identity_is_rejected()
    {
        var adapter = new FakeHostAdapter
        {
            CanonicalHostIdentity = "76561190000000001",
            Players = new[] { FakeHostAdapter.SinglePlayer("76561190000000002") },
            Clock = FakeHostAdapter.CreateClock(120)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.IdentityAmbiguous, service.LastRejectReason);
        Assert.False(service.TryGetState("76561190000000001", out _));
        Assert.False(service.TryGetState("76561190000000002", out _));
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Increasing_absolute_clock_delegates_evidence_recency_decay_once()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayInputs = new List<LocalPressureDecayInput>();
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayInputs.Add(input);
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Clock = FakeHostAdapter.CreateClock(120);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        var updated = ReadState(service, "player-1");
        Assert.Single(decayInputs);
        Assert.Equal(2, decayInputs[0].CurrentGameTimeHours);
        Assert.False(decayInputs[0].ActivePursuit);
        Assert.Equal(8, updated!.LocalHeat);
        Assert.Equal(2, updated.LastDecayEvaluation);
    }

    [Fact]
    public void Missing_pause_input_does_not_block_eligible_evidence_recency_decay()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        LocalPressureDecayInput? decayInput = null;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayInput = input;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Clock = FakeHostAdapter.CreateClock(120);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.NotNull(decayInput);
        Assert.False(decayInput!.ActivePursuit);
        Assert.Equal(8, ReadState(service, "player-1")!.LocalHeat);
        Assert.Equal(2, ReadState(service, "player-1")!.LastDecayEvaluation);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
    }

    [Fact]
    public void Evidence_recency_decay_does_not_reduce_heat_before_quiet_grace_expires()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 1, 3, 1, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Clock = FakeHostAdapter.CreateClock(120);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        var observed = ReadState(service, "player-1");
        Assert.Equal(10, observed!.LocalHeat);
        Assert.Equal(1, observed.LastDecayEvaluation);
        Assert.Equal(1, observed.Revision);
    }

    [Fact]
    public void Evidence_recency_decay_uses_profile_rate_and_stops_at_known_offender_floor()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(30, true, null, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Clock = FakeHostAdapter.CreateClock(180);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(27, ReadState(service, "player-1")!.LocalHeat);
        Assert.Equal(3, ReadState(service, "player-1")!.LastDecayEvaluation);

        adapter.Clock = FakeHostAdapter.CreateClock(360);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(LocalPressureProfile.Moderate.KnownOffenderFloor, ReadState(service, "player-1")!.LocalHeat);
        Assert.Equal(6, ReadState(service, "player-1")!.LastDecayEvaluation);

        adapter.Clock = FakeHostAdapter.CreateClock(420);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(LocalPressureProfile.Moderate.KnownOffenderFloor, ReadState(service, "player-1")!.LocalHeat);
    }

    [Fact]
    public void Guest_or_ambiguous_identity_causes_no_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[]
            {
                FakeHostAdapter.SinglePlayer() with { IsHostOwned = false },
                FakeHostAdapter.SinglePlayer("player-2")
            },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Null(ReadState(service, "player-1"));
    }

    [Fact]
    public void Rewound_clock_is_rejected_without_model_or_repository_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(120)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Clock = FakeHostAdapter.CreateClock(60);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(0, decayCalls);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(LocalPressureRuntimeRejectReason.ClockRewound, service.LastRejectReason);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void Save_while_awaiting_baseline_does_not_write()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);
    }

    [Fact]
    public void Host_authority_failure_quarantines_without_loading_or_writing()
    {
        var adapter = new FakeHostAdapter
        {
            HostAuthority = false,
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(0, repository.LoadCalls);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Invalid_clock_is_rejected_without_establishing_baseline()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(-1)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.InvalidClock, service.LastRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Duplicate_and_stale_clock_samples_do_not_call_model_or_write()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60, sequence: 10)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        adapter.Clock = FakeHostAdapter.CreateClock(60, sequence: 11);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);
        adapter.Clock = FakeHostAdapter.CreateClock(120, sequence: 9);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(0, decayCalls);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(LocalPressureRuntimeRejectReason.StaleSequence, service.LastRejectReason);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void Clock_between_preload_and_load_complete_cannot_mutate()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, service.Phase);
        Assert.Equal(0, decayCalls);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Malformed_load_quarantines_without_overwriting_sidecar()
    {
        var adapter = new FakeHostAdapter();
        var repository = new FakeRepository
        {
            LoadResult = new LocalPressureStoreLoadResult(
                false,
                LocalPressureStoreLoadStatus.Failed,
                null,
                LocalPressureStoreFailureReason.EmptyOrMalformedJson,
                "malformed")
        };
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(1, repository.LoadCalls);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Save_failure_preserves_memory_and_does_not_retry_implicitly()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }))
        {
            UpdateResult = new LocalPressureStoreUpdateResult(
                false,
                LocalPressureStoreUpdateStatus.Rejected,
                null,
                LocalPressureStoreFailureReason.SidecarReadFailed,
                "disk failure")
        };
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.SidecarSaveFailed, service.LastRejectReason);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void Dispose_during_save_does_not_write_and_is_idempotent()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        service.Dispose();
        service.Dispose();
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimePhase.Disposed, service.Phase);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(1, adapter.DisposeCalls);
    }

    [Fact]
    public void Blank_player_code_is_treated_as_not_ready_when_folder_identity_is_valid()
    {
        var adapter = new FakeHostAdapter
        {
            CanonicalHostIdentity = "76561190000000001",
            Players = new[] { FakeHostAdapter.SinglePlayer() with { PlayerCode = " " } },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.IdentityUnavailable, service.LastRejectReason);
        Assert.Equal(0, repository.UpdateCalls);

        adapter.Players = new[] { FakeHostAdapter.SinglePlayer("76561190000000001") };
        service.PumpReadiness();
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Missing_repository_path_defers_before_sidecar_read()
    {
        var adapter = new FakeHostAdapter();
        var repository = new FakeRepository();
        using var service = new LocalPressureRuntimeService(
            adapter,
            _ => null,
            LocalPressureProfile.Moderate,
            null,
            _ => { });

        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.SavePathUnavailable, service.LastRejectReason);
        Assert.Equal(0, repository.LoadCalls);
    }

    [Fact]
    public void Save_path_that_becomes_available_after_load_complete_is_retried_before_save()
    {
        var adapter = new FakeHostAdapter
        {
            ActiveSaveFolder = null,
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = new LocalPressureRuntimeService(
            adapter,
            activeSaveFolder => activeSaveFolder is null ? null : repository,
            LocalPressureProfile.Moderate,
            null,
            _ => { });

        service.OnPreLoad();
        service.OnLoadComplete();

        // A duplicate preload and an intervening callback must not erase the
        // non-quarantining retry that was requested by the missing save path.
        service.OnPreLoad();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        adapter.ActiveSaveFolder = "C:\\saves\\slot-1";
        service.PumpReadiness();
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(1, repository.LoadCalls);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
    }

    [Fact]
    public void Startup_readiness_retries_until_authority_and_player_are_ready()
    {
        var adapter = new FakeHostAdapter
        {
            HostAuthority = false,
            HostAuthorityPending = true,
            Players = Array.Empty<LocalPressurePlayerSample>(),
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, service.Phase);

        adapter.HostAuthority = true;
        adapter.HostAuthorityPending = false;
        adapter.Players = new[] { FakeHostAdapter.SinglePlayer() };
        service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);

        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal("player-1", repository.UpdatedRecord!.PlayerId);
    }

    [Fact]
    public void Empty_player_registry_remains_pending_until_one_connected_player_is_ready()
    {
        var adapter = new FakeHostAdapter
        {
            Players = Array.Empty<LocalPressurePlayerSample>(),
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);
        Assert.Equal(0, repository.UpdateCalls);

        adapter.Players = new[] { FakeHostAdapter.SinglePlayer() };
        service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Transitional_player_read_remains_pending_until_connection_is_ready()
    {
        var adapter = new FakeHostAdapter
        {
            PlayersPending = true,
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);

        adapter.PlayersPending = false;
        service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Readiness_transitions_emit_bounded_lifecycle_telemetry()
    {
        var adapter = new FakeHostAdapter
        {
            Players = Array.Empty<LocalPressurePlayerSample>(),
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        var logs = new List<string>();
        using var service = new LocalPressureRuntimeService(
            adapter,
            _ => repository,
            LocalPressureProfile.Moderate,
            null,
            logs.Add);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.Players = new[] { FakeHostAdapter.SinglePlayer() };
        service.PumpReadiness();
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Contains(logs, message => message.Contains("phase", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, message => message.Contains("Active", StringComparison.Ordinal));
        Assert.Contains(logs, message => message.Contains("hydrated", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, message => message.Contains("SaveStart", StringComparison.Ordinal));
        Assert.Contains(logs, message => message.Contains("write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Clock_adapter_exception_quarantines_without_model_or_repository_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            ThrowOnClockRead = true
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.AdapterFailure, service.LastRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void A_clock_adapter_failure_logs_the_warning_delegate_while_phase_and_hydration_receipts_log_the_receipt_delegate()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            ThrowOnClockRead = true
        };
        var repository = new FakeRepository();
        var warnings = new List<string>();
        var receipts = new List<string>();
        using var service = new LocalPressureRuntimeService(
            adapter,
            _ => repository,
            log: warnings.Add,
            receiptLog: receipts.Add);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Contains(warnings, message => message.StartsWith("Local Pressure host clock read failed:", StringComparison.Ordinal));
        Assert.Contains(receipts, message => message.StartsWith("Local Pressure phase changed:", StringComparison.Ordinal));
        Assert.Contains(receipts, message => message.StartsWith("Local Pressure repository hydrated;", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, message => message.StartsWith("Local Pressure phase changed:", StringComparison.Ordinal));
        Assert.DoesNotContain(receipts, message => message.StartsWith("Local Pressure host clock read failed:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(7193, 4, 2353)]
    [InlineData(6840, 4, 1800)]
    [InlineData(7045, 4, 2145)]
    public void Valid_hhmm_clock_samples_above_1440_activate_and_persist_quiet_state(
        long totalGameMinutes,
        int elapsedDays,
        int time24h)
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = new LocalPressureClockSample(
                totalGameMinutes,
                elapsedDays,
                time24h,
                null,
                LocalPressureClockBoundary.HostReady,
                DateTime.UtcNow)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.True(service.TryGetState("player-1", out var state));
        Assert.Equal(0, state!.LocalHeat);
        Assert.False(state.KnownOffender);
        Assert.Equal(0, state.Revision);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal("player-1", repository.UpdatedRecord!.PlayerId);
        Assert.Equal(0, repository.UpdatedRecord.LocalHeat);
        Assert.Equal(0, repository.UpdatedRecord.Revision);
    }

    [Fact]
    public void Packed_hhmm_rollover_keeps_total_game_minutes_as_the_mutation_clock()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = new LocalPressureClockSample(
                TotalGameMinutes: 1433,
                ElapsedDays: 0,
                Time24h: 2353,
                SourceSequence: null,
                Boundary: LocalPressureClockBoundary.HostReady,
                ReceivedAtUtc: DateTime.UtcNow)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayInputs = new List<LocalPressureDecayInput>();
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayInputs.Add(input);
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.PumpReadiness();

        adapter.Clock = new LocalPressureClockSample(
            TotalGameMinutes: 1447,
            ElapsedDays: 1,
            Time24h: 7,
            SourceSequence: null,
            Boundary: LocalPressureClockBoundary.Hour,
            ReceivedAtUtc: DateTime.UtcNow);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Single(decayInputs);
        Assert.Equal(1447 / 60.0, decayInputs[0].CurrentGameTimeHours);
        Assert.Equal(7, adapter.Clock.Time24h);
        Assert.Equal(1447, service.AcceptedClockTotalGameMinutes);
    }

    [Fact]
    public void Negative_total_game_minutes_are_rejected_without_establishing_baseline()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(-1, time24h: 2353)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.InvalidClock, service.LastRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Null_player_sample_is_rejected_without_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = null!,
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.IdentityUnavailable, service.LastRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Clock_boundary_during_save_does_not_evaluate_or_write_until_save_complete()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        adapter.Clock = FakeHostAdapter.CreateClock(120);
        service.OnClockBoundary(LocalPressureClockBoundary.Hour);

        Assert.Equal(LocalPressureRuntimePhase.Saving, service.Phase);
        Assert.Equal(0, decayCalls);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Duplicate_save_start_is_a_typed_no_op_and_save_complete_writes_once()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        service.OnSaveStart();

        Assert.Equal(LocalPressureRuntimePhase.Saving, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);

        service.OnSaveComplete();
        service.OnSaveComplete();

        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
    }

    [Fact]
    public void PumpReadiness_can_establish_baseline_but_must_not_drive_active_decay()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(1400)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayInputs = new List<LocalPressureDecayInput>();
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayInputs.Add(input);
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.PumpReadiness();
        var stateBeforePump = ReadState(service, "player-1");
        adapter.Clock = FakeHostAdapter.CreateClock(1500);
        service.PumpReadiness();

        Assert.Empty(decayInputs);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(1400, service.AcceptedClockTotalGameMinutes);
        Assert.Same(stateBeforePump, ReadState(service, "player-1"));
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
    }

    [Fact]
    public void PumpReadiness_waits_for_native_clock_subscriptions_before_activating()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(1400),
            ClockBoundarySubscriptionsReady = false
        };
        using var service = CreateService(adapter, new FakeRepository());

        service.OnPreLoad();
        service.OnLoadComplete();
        service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);
        Assert.Null(service.AcceptedClockTotalGameMinutes);

        adapter.ClockBoundarySubscriptionsReady = true;
        service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(1400, service.AcceptedClockTotalGameMinutes);
        Assert.Equal(2, adapter.EnsureClockBoundarySubscriptionsCalls);

        service.PumpReadiness();

        Assert.Equal(2, adapter.EnsureClockBoundarySubscriptionsCalls);
    }

    [Fact]
    public void PumpReadiness_quarantines_before_baseline_when_clock_subscription_binding_faults()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(1400),
            ClockBoundarySubscriptionsFaulted = true
        };
        using var service = CreateService(adapter, new FakeRepository());

        service.OnPreLoad();
        service.OnLoadComplete();
        service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.AdapterFailure, service.LastRejectReason);
        Assert.Null(service.AcceptedClockTotalGameMinutes);
        Assert.Equal(1, adapter.EnsureClockBoundarySubscriptionsCalls);
    }

    [Fact]
    public void Active_update_pump_is_inert_even_when_clock_advances_and_authority_is_lost()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        var acceptedClock = service.AcceptedClockTotalGameMinutes;
        var acceptedState = ReadState(service, "player-1");

        adapter.Clock = FakeHostAdapter.CreateClock(600);
        adapter.HostAuthorityPending = true;
        for (var i = 0; i < 10; i++)
            service.PumpReadiness();

        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.Equal(acceptedClock, service.AcceptedClockTotalGameMinutes);
        Assert.Same(acceptedState, ReadState(service, "player-1"));
        Assert.Equal(0, decayCalls);

        service.Dispose();

        Assert.Equal(LocalPressureRuntimePhase.Disposed, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
    }

    [Fact]
    public void Day_boundary_is_excluded_while_hour_and_sleep_end_are_native_mutation_boundaries()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.PumpReadiness();
        var baseline = ReadState(service, "player-1");

        adapter.Clock = FakeHostAdapter.CreateClock(120);
        service.OnClockBoundary(LocalPressureClockBoundary.Day);

        Assert.Equal(0, decayCalls);
        Assert.Equal(60, service.AcceptedClockTotalGameMinutes);
        Assert.Same(baseline, ReadState(service, "player-1"));

        adapter.Clock = FakeHostAdapter.CreateClock(180);
        service.OnClockBoundary(LocalPressureClockBoundary.SleepEnd);

        Assert.Equal(1, decayCalls);
        Assert.Equal(180, service.AcceptedClockTotalGameMinutes);
    }

    [Fact]
    public void Save_uses_last_known_state_without_grafting_changed_context()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() with { Region = "south", PropertyCode = "new-property" } },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", "north", "old-property", 1);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.NotNull(repository.UpdatedRecord);
        Assert.Equal("north", repository.UpdatedRecord!.Region);
        Assert.Equal("old-property", repository.UpdatedRecord.Property);
    }

    [Fact]
    public void Host_authority_read_exception_remains_adapter_failure()
    {
        var adapter = new FakeHostAdapter
        {
            ThrowOnHostAuthorityRead = true
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.AdapterFailure, service.LastRejectReason);
        Assert.Equal(0, repository.LoadCalls);
    }

    [Fact]
    public void Canonical_identity_read_exception_remains_adapter_failure()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60),
            ThrowOnCanonicalIdentityRead = true
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.AdapterFailure, service.LastRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Multiplayer_sidecar_is_retained_but_cannot_decay_or_write()
    {
        var playerOne = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 1);
        var playerTwo = new LocalPressureState(20, false, 0, null, 0, "player-2", null, null, 2);
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer(), FakeHostAdapter.SinglePlayer("player-2") },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(playerOne), LocalPressurePlayerRecord.FromState(playerTwo) }));
        var decayCalls = 0;
        using var service = CreateService(
            adapter,
            repository,
            (input, profile) =>
            {
                decayCalls++;
                return LocalPressureDecay.Evaluate(input, profile);
            });

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        Assert.Equal(LocalPressureRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(playerOne, ReadState(service, "player-1"));
        Assert.Equal(playerTwo, ReadState(service, "player-2"));
        Assert.Equal(0, decayCalls);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Single_player_save_preserves_other_loaded_sidecar_players()
    {
        var playerOne = new LocalPressureState(10, false, 0, null, null, "player-1", "north", "old-property", 1);
        var playerTwo = new LocalPressureState(20, true, 0, null, null, "player-2", "south", "other-property", 2);
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer(), FakeHostAdapter.SinglePlayer("player-2") },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(playerOne), LocalPressurePlayerRecord.FromState(playerTwo) }))
        {
            PreserveOtherPlayersOnUpdate = true
        };
        using var service = CreateService(adapter, repository);

        service.OnPreLoad();
        service.OnLoadComplete();
        adapter.Players = new[] { FakeHostAdapter.SinglePlayer() };
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        service.OnSaveStart();
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
        Assert.NotNull(repository.CurrentEnvelope);
        Assert.Equal(2, repository.CurrentEnvelope!.Players.Count);
        Assert.Contains(repository.CurrentEnvelope.Players, record => record.PlayerId == "player-2");
    }

    [Fact]
    public void Record_wipe_outside_active_is_rejected_without_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var repository = new FakeRepository();
        using var service = CreateService(adapter, repository);

        var result = service.TryApplyRecordWipe("player-1", service.SessionEpoch, service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, result.RejectReason);
        Assert.Null(ReadState(service, "player-1"));
    }

    [Fact]
    public void Record_wipe_with_a_stale_epoch_is_rejected_without_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(50, true, 1, null, 1, "player-1", null, null, 4);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        using var service = CreateService(adapter, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        var result = service.TryApplyRecordWipe("player-1", service.SessionEpoch, service.LoadEpoch - 1);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.StaleEpoch, result.RejectReason);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void Record_wipe_against_a_non_authoritative_host_is_rejected_without_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(50, true, 1, null, 1, "player-1", null, null, 4);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        using var service = CreateService(adapter, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        adapter.HostAuthority = false;

        var result = service.TryApplyRecordWipe("player-1", service.SessionEpoch, service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.NotAuthoritativeHost, result.RejectReason);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void Record_wipe_with_a_mismatched_player_id_is_rejected_without_mutation()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(50, true, 1, null, 1, "player-1", null, null, 4);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        using var service = CreateService(adapter, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        var result = service.TryApplyRecordWipe("player-2", service.SessionEpoch, service.LoadEpoch);

        Assert.False(result.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.PlayerMismatch, result.RejectReason);
        Assert.Equal(state, ReadState(service, "player-1"));
    }

    [Fact]
    public void An_accepted_record_wipe_zeroes_heat_and_clears_known_offender_and_publishes_nothing()
    {
        var adapter = new FakeHostAdapter
        {
            Players = new[] { FakeHostAdapter.SinglePlayer() },
            Clock = FakeHostAdapter.CreateClock(60)
        };
        var state = new LocalPressureState(50, true, 1, null, 1, "player-1", null, null, 4);
        var repository = new FakeRepository(new LocalPressureSaveEnvelope(
            1,
            new[] { LocalPressurePlayerRecord.FromState(state) }));
        var sink = new RecordingSink();
        using var service = new LocalPressureRuntimeService(
            adapter,
            _ => repository,
            LocalPressureProfile.Moderate,
            log: _ => { },
            tierTransitionSink: sink);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);

        var result = service.TryApplyRecordWipe("player-1", service.SessionEpoch, service.LoadEpoch);

        Assert.True(result.Accepted, result.Message);
        Assert.True(service.TryGetState("player-1", out var wiped));
        Assert.Equal(0, wiped!.LocalHeat);
        Assert.False(wiped.KnownOffender);
        Assert.Equal(5, wiped.Revision);
        Assert.Empty(sink.Notifications);
    }

    private sealed class RecordingSink : ILocalPressureTierTransitionSink
    {
        public List<LocalPressureTierTransitionNotification> Notifications { get; } = new();
        public List<(Guid SessionEpoch, long LoadEpoch)> Resets { get; } = new();

        public void Publish(LocalPressureTierTransitionNotification notification) => Notifications.Add(notification);

        public void ResetForEpoch(Guid sessionEpoch, long loadEpoch) => Resets.Add((sessionEpoch, loadEpoch));
    }

    private static LocalPressureRuntimeService CreateService(
        FakeHostAdapter adapter,
        FakeRepository repository,
        LocalPressureDecayEvaluator? decayEvaluator = null) =>
        new(
            adapter,
            _ => repository,
            LocalPressureProfile.Moderate,
            decayEvaluator,
            _ => { });

    private static LocalPressureState? ReadState(LocalPressureRuntimeService service, string playerId) =>
        service.TryGetState(playerId, out var state) ? state : null;

    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        public LocalPressureHostAuthorityReadStatus ReadHostAuthority()
        {
            if (ThrowOnHostAuthorityRead)
                throw new InvalidOperationException("host authority unavailable");

            return HostAuthorityPending
                ? LocalPressureHostAuthorityReadStatus.Pending
                : HostAuthority
                    ? LocalPressureHostAuthorityReadStatus.Ready
                    : LocalPressureHostAuthorityReadStatus.NotAuthoritative;
        }
        public bool HostAuthority { get; set; } = true;
        public bool HostAuthorityPending { get; set; }
        private string? _canonicalHostIdentity = "player-1";
        public string? CanonicalHostIdentity
        {
            get
            {
                if (ThrowOnCanonicalIdentityRead)
                    throw new InvalidOperationException("canonical identity unavailable");

                return _canonicalHostIdentity;
            }
            set => _canonicalHostIdentity = value;
        }
        public string? ActiveSaveFolder { get; set; } = "C:\\saves\\slot-1";
        public LocalPressureClockSample Clock { get; set; }
        public IReadOnlyList<LocalPressurePlayerSample> Players { get; set; } = Array.Empty<LocalPressurePlayerSample>();
        public int DisposeCalls { get; private set; }
        public bool ThrowOnClockRead { get; set; }
        public bool ThrowOnHostAuthorityRead { get; set; }
        public bool ThrowOnCanonicalIdentityRead { get; set; }
        public bool PlayersPending { get; set; }
        public bool ClockBoundarySubscriptionsReady { get; set; } = true;
        public bool ClockBoundarySubscriptionsFaulted { get; set; }
        public int EnsureClockBoundarySubscriptionsCalls { get; private set; }

        public event Action? PreLoad;
        public event Action? LoadComplete;
        public event Action? SaveStart;
        public event Action? SaveComplete;
        public event Action<LocalPressureClockBoundary>? ClockBoundary;

        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions()
        {
            EnsureClockBoundarySubscriptionsCalls++;
            if (ClockBoundarySubscriptionsFaulted)
                return LocalPressureClockBoundaryBindingStatus.Faulted;
            return ClockBoundarySubscriptionsReady
                ? LocalPressureClockBoundaryBindingStatus.Ready
                : LocalPressureClockBoundaryBindingStatus.Pending;
        }

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
            if (players is null)
                return LocalPressurePlayerReadStatus.Ready;
            return PlayersPending
                ? LocalPressurePlayerReadStatus.Pending
                : players.Count == 0
                    ? LocalPressurePlayerReadStatus.Pending
                : LocalPressurePlayerReadStatus.Ready;
        }

        public void RaisePreLoad() => PreLoad?.Invoke();
        public void RaiseLoadComplete() => LoadComplete?.Invoke();
        public void RaiseSaveStart() => SaveStart?.Invoke();
        public void RaiseSaveComplete() => SaveComplete?.Invoke();
        public void RaiseClockBoundary(LocalPressureClockBoundary boundary) => ClockBoundary?.Invoke(boundary);

        public void Dispose() => DisposeCalls++;

        public static LocalPressurePlayerSample SinglePlayer(string playerId = "player-1") =>
            new(playerId, true, true, playerId, "north", "safehouse");

        public static LocalPressureClockSample CreateClock(long totalGameMinutes) =>
            CreateClock(totalGameMinutes, null, time24h: 700);

        public static LocalPressureClockSample CreateClock(long totalGameMinutes, uint? sequence) =>
            CreateClock(totalGameMinutes, sequence, time24h: 700);

        public static LocalPressureClockSample CreateClock(long totalGameMinutes, int time24h) =>
            CreateClock(totalGameMinutes, null, time24h);

        private static LocalPressureClockSample CreateClock(long totalGameMinutes, uint? sequence, int time24h) =>
            new(totalGameMinutes, (int)(totalGameMinutes / 1440), time24h, sequence, LocalPressureClockBoundary.HostReady, DateTime.UtcNow);
    }

    private sealed class FakeRepository : ILocalPressureStateRepository
    {
        public FakeRepository(LocalPressureSaveEnvelope? envelope = null)
        {
            CurrentEnvelope = envelope ?? LocalPressureSaveEnvelope.CreateEmpty();
            LoadResult = new LocalPressureStoreLoadResult(
                true,
                envelope is null ? LocalPressureStoreLoadStatus.Empty : LocalPressureStoreLoadStatus.Loaded,
                CurrentEnvelope,
                LocalPressureStoreFailureReason.None,
                "ok");
        }

        public LocalPressureStoreLoadResult LoadResult { get; set; }
        public int LoadCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public LocalPressureStoreUpdateResult UpdateResult { get; set; } = new(
            true,
            LocalPressureStoreUpdateStatus.Updated,
            null,
            LocalPressureStoreFailureReason.None,
            "ok");
        public LocalPressurePlayerRecord? UpdatedRecord { get; private set; }
        public bool PreserveOtherPlayersOnUpdate { get; set; }
        public LocalPressureSaveEnvelope? CurrentEnvelope { get; private set; }

        public LocalPressureStoreLoadResult Load()
        {
            LoadCalls++;
            return LoadResult;
        }

        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            UpdateCalls++;
            UpdatedRecord = record;
            if (UpdateResult.Succeeded && PreserveOtherPlayersOnUpdate && CurrentEnvelope is not null)
            {
                var players = CurrentEnvelope.Players
                    .Where(existing => !string.Equals(existing.PlayerId, record.PlayerId, StringComparison.Ordinal))
                    .Append(record)
                    .ToArray();
                CurrentEnvelope = CurrentEnvelope with { Players = players };
            }

            return UpdateResult;
        }
    }
}
