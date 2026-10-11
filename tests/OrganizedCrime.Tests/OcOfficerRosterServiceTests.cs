using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcOfficerRosterServiceTests
{
    private const string Session = "svc";

    private static OcOfficerRosterService Create(FakeOfficerWorld world, List<string>? log = null) =>
        new(world, Session, log is null ? null : new Action<string>(log.Add));

    [Fact]
    public void Ctor_rejects_a_null_world()
    {
        Assert.Throws<ArgumentNullException>(() => new OcOfficerRosterService(null!));
    }

    [Fact]
    public void Request_is_refused_off_host_and_after_dispose()
    {
        var world = new FakeOfficerWorld { Host = false };
        var service = Create(world);

        Assert.Equal(OcOfficerRequestStatus.RefusedNotHost, service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle));
        Assert.Equal(-1, handle);

        world.Host = true;
        Assert.Equal(OcOfficerRequestStatus.Accepted, service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out handle));
        service.Dispose();
        Assert.Equal(OcOfficerRequestStatus.RefusedDisposed, service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _));
    }

    [Fact]
    public void Update_spawns_at_most_one_slot_per_frame()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("a", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.TryRequest("b", new OcOfficerPosition(1f, 0f, 0f), out _);

        service.Update();

        Assert.Equal(1, world.SpawnCalls);
        Assert.Equal(1, service.Snapshot().Count(item => item.State == OcOfficerSlotState.Active));
        Assert.Equal(1, service.Snapshot().Count(item => item.State == OcOfficerSlotState.Requested));

        service.Update();

        Assert.Equal(2, service.Snapshot().Count(item => item.State == OcOfficerSlotState.Active));
    }

    [Fact]
    public void Update_ignores_everything_off_host_and_while_suspended()
    {
        var world = new FakeOfficerWorld { Host = false };
        var service = Create(world);
        world.Host = true;
        service.TryRequest("a", new OcOfficerPosition(0f, 0f, 0f), out _);
        world.Host = false;

        service.Update();

        Assert.Equal(0, world.SpawnCalls);

        world.Host = true;
        service.OnSaveStart();
        service.Update();

        Assert.Equal(0, world.SpawnCalls);
    }

    [Fact]
    public void Failed_spawn_retries_across_frames_then_fails_with_one_log_line()
    {
        var world = new FakeOfficerWorld { SpawnResult = OcOfficerWorldStatus.Failed };
        var log = new List<string>();
        var service = Create(world, log);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);

        for (var attempt = 0; attempt < OcOfficerRoster.MaxAttempts; attempt++)
        {
            for (var frame = 0; frame < OcOfficerRoster.AttemptBackoffUpdates; frame++)
                service.Update();
            if (attempt < OcOfficerRoster.MaxAttempts - 1)
                Assert.Equal(OcOfficerSlotState.Requested, service.Snapshot()[0].State);
        }

        var snapshot = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Failed, snapshot.State);
        Assert.Equal(OcOfficerRoster.MaxAttempts, snapshot.Attempts);
        Assert.Equal(OcOfficerRoster.MaxAttempts, world.SpawnCalls);
        Assert.Equal(1, log.Count(line => line.Contains("after 3 attempts", StringComparison.Ordinal)));
    }

    [Fact]
    public void Template_failure_consumes_no_attempt()
    {
        var world = new FakeOfficerWorld { EnsureResult = OcOfficerWorldStatus.NotReady };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);

        for (var frame = 0; frame < OcOfficerRoster.AttemptBackoffUpdates * 2; frame++)
            service.Update();

        var snapshot = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, snapshot.State);
        Assert.Equal(0, snapshot.Attempts);
        Assert.Equal(0, world.SpawnCalls);
    }

    [Fact]
    public void Knocked_out_clone_marks_then_respawns_after_120_minutes()
    {
        var world = new FakeOfficerWorld { Minutes = 1000d };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();
        var firstId = Assert.Single(service.Snapshot()).CloneId;

        world.KnockedOut = true;
        service.Update();

        var knocked = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.KnockedOut, knocked.State);
        Assert.Equal(1000d, knocked.KnockedOutGameMinutes);

        world.Minutes = 1000d + OcOfficerRoster.KnockoutRespawnMinutes - 1d;
        service.Update();
        Assert.Equal(OcOfficerSlotState.KnockedOut, Assert.Single(service.Snapshot()).State);
        Assert.Equal(0, world.RemoveCalls);

        world.Minutes = 1000d + OcOfficerRoster.KnockoutRespawnMinutes;
        service.Update();

        Assert.Equal(1, world.RemoveCalls);
        var requeued = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, requeued.State);
        Assert.NotEqual(firstId, requeued.CloneId);

        world.KnockedOut = false;
        service.Update();
        Assert.Equal(OcOfficerSlotState.Active, Assert.Single(service.Snapshot()).State);
    }

    [Fact]
    public void Released_knocked_out_slot_never_respawns()
    {
        var world = new FakeOfficerWorld { Minutes = 50d };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();
        world.KnockedOut = true;
        service.Update();

        Assert.True(service.Release(handle));
        Assert.Equal(1, world.RemoveCalls);

        world.Minutes = 5000d;
        for (var frame = 0; frame < 5; frame++)
            service.Update();

        Assert.Equal(0, service.Count);
        Assert.Equal(1, world.RemoveCalls);
    }

    [Fact]
    public void Release_of_an_unknown_handle_fails_without_touching_the_world()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);

        Assert.False(service.Release(4));
        Assert.Equal(0, world.RemoveCalls);
    }

    [Fact]
    public void Failed_removal_is_retried_as_an_orphan()
    {
        var world = new FakeOfficerWorld { RemoveResult = OcOfficerWorldStatus.Failed };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();

        Assert.True(service.Release(handle));
        Assert.Equal(0, service.Count);

        world.RemoveResult = OcOfficerWorldStatus.Ok;
        service.Update();

        Assert.Equal(2, world.RemoveCalls);
        var removalsAfterDrain = world.RemoveCalls;
        service.Update();
        Assert.Equal(removalsAfterDrain, world.RemoveCalls);
    }

    [Fact]
    public void Save_start_despawns_and_save_complete_respawns()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.Update();
        var id = Assert.Single(service.Snapshot()).CloneId;

        service.OnSaveStart();

        Assert.Equal(1, world.RemoveCalls);
        var suspended = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, suspended.State);
        Assert.Equal(id, suspended.CloneId);

        service.OnSaveComplete();
        service.Update();

        Assert.Equal(OcOfficerSlotState.Active, Assert.Single(service.Snapshot()).State);
    }

    [Fact]
    public void Pre_load_clears_everything_and_destroys_the_template()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.Update();

        service.OnPreLoad();

        Assert.Equal(0, service.Count);
        Assert.Equal(1, world.RemoveCalls);
        Assert.Equal(1, world.DestroyTemplateCalls);
    }

    [Fact]
    public void Dispose_clears_everything_once()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.Update();

        service.Dispose();
        service.Dispose();

        Assert.Equal(0, service.Count);
        Assert.Equal(1, world.RemoveCalls);
        Assert.Equal(1, world.DestroyTemplateCalls);
    }

    [Fact]
    public void Throwing_world_never_escapes_the_service()
    {
        var world = new FakeOfficerWorld { Throw = true };
        var service = Create(world);

        Assert.Equal(OcOfficerRequestStatus.RefusedNotHost, service.TryRequest("o", new OcOfficerPosition(0f, 0f, 0f), out _));
        service.Update();
        service.OnSaveStart();
        service.OnSaveComplete();
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.False(service.Release(0));
        Assert.Equal(OcOfficerBindStatus.InvalidCheckpoint, service.TryBindToCheckpoint(0, null));
        var status = service.QaStatus();
        Assert.NotEmpty(status);
        service.Dispose();
        Assert.Equal(OcOfficerBindStatus.Disposed, service.TryBindToCheckpoint(0, new object()));
    }

    [Fact]
    public void Bind_passes_live_clones_to_the_world()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        var checkpoint = new object();

        Assert.Equal(OcOfficerBindStatus.SlotNotActive, service.TryBindToCheckpoint(handle, checkpoint));
        Assert.Equal(OcOfficerBindStatus.InvalidCheckpoint, service.TryBindToCheckpoint(handle, null));

        service.Update();

        Assert.Equal(OcOfficerBindStatus.Bound, service.TryBindToCheckpoint(handle, checkpoint));
        Assert.Equal(checkpoint, world.BoundCheckpoint);
        Assert.Equal(1, world.BoundCloneSerial);

        world.BindResult = OcOfficerWorldStatus.Failed;
        Assert.Equal(OcOfficerBindStatus.WorldFailed, service.TryBindToCheckpoint(handle, checkpoint));

        world.Host = false;
        Assert.Equal(OcOfficerBindStatus.NotHost, service.TryBindToCheckpoint(handle, checkpoint));

        service.Dispose();
        Assert.Equal(OcOfficerBindStatus.Disposed, service.TryBindToCheckpoint(handle, checkpoint));
    }

    [Fact]
    public void Bind_forwards_the_stand_point_to_the_world()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();
        var checkpoint = new object();
        var stand = new OcOfficerPosition(3f, 0.5f, 4f, 90f);

        Assert.Equal(OcOfficerBindStatus.Bound, service.TryBindToCheckpoint(handle, checkpoint, stand));
        Assert.Equal(stand, world.BindStandPoint);
    }

    [Fact]
    public void Clone_position_read_returns_the_live_clone_position()
    {
        var world = new FakeOfficerWorld { ClonePosition = new OcOfficerPosition(1f, 2f, 3f) };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        Assert.False(service.TryReadClonePosition(handle, out _));

        service.Update();

        Assert.True(service.TryReadClonePosition(handle, out var position));
        Assert.Equal(new OcOfficerPosition(1f, 2f, 3f), position);
    }

    [Fact]
    public void Behaviour_state_read_returns_the_active_name_and_checkpoint_flag()
    {
        var world = new FakeOfficerWorld { BehaviourName = "FootPatrol", CheckpointActive = false };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        Assert.False(service.TryReadBehaviourState(handle, out _, out _));

        service.Update();

        Assert.True(service.TryReadBehaviourState(handle, out var name, out var active));
        Assert.Equal("FootPatrol", name);
        Assert.False(active);
    }

    [Fact]
    public void Qa_status_lists_slots_template_and_native_counts()
    {
        var world = new FakeOfficerWorld { Minutes = 2000d };
        var service = Create(world);
        service.TryRequest("qa:0", new OcOfficerPosition(3f, 0f, 4f), out _);
        service.Update();

        var lines = service.QaStatus();

        Assert.Contains(lines, line => line.Contains("1/8", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("PrefabId", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("NPCRegistry", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("qa:0", StringComparison.Ordinal) && line.Contains("5.0 m", StringComparison.Ordinal));
    }

    [Fact]
    public void Qa_spawn_refuses_out_of_range_and_plants_officers_ahead()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);

        Assert.False(service.QaSpawn(0).Ok);
        Assert.False(service.QaSpawn(9).Ok);
        Assert.Equal(0, service.Count);

        var result = service.QaSpawn(2);

        Assert.True(result.Ok);
        Assert.Equal(2, service.Count);
        var snapshots = service.Snapshot();
        Assert.Equal("qa:0", snapshots[0].OwnerKey);
        Assert.Equal("qa:1", snapshots[1].OwnerKey);
        // The fake player stands at the origin facing north: 3 m ahead, spread sideways.
        Assert.Equal(3f, snapshots[0].Position.Z);
        Assert.Equal(3f, snapshots[1].Position.Z);
        Assert.True(snapshots[0].Position.X < 0f);
        Assert.True(snapshots[1].Position.X > 0f);
    }

    [Fact]
    public void Qa_spawn_refuses_without_a_player_pose()
    {
        var world = new FakeOfficerWorld { PoseResult = OcOfficerWorldStatus.NotReady };
        var service = Create(world);

        Assert.False(service.QaSpawn(1).Ok);
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public void Qa_clear_releases_only_qa_slots()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("checkpoint:outside-barn:0", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.QaSpawn(2);

        var result = service.QaClear();

        Assert.True(result.Ok);
        Assert.Equal(1, service.Count);
        Assert.Equal("checkpoint:outside-barn:0", Assert.Single(service.Snapshot()).OwnerKey);
    }

    [Fact]
    public void Save_boundaries_log_the_despawn_and_respawn_counts()
    {
        var world = new FakeOfficerWorld();
        var log = new List<string>();
        var service = Create(world, log);
        service.TryRequest("a", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.TryRequest("b", new OcOfficerPosition(1f, 0f, 0f), out _);
        service.Update();
        service.Update();

        service.OnSaveStart();
        Assert.Contains(log, line => line.Contains("despawned 2 for save", StringComparison.Ordinal));

        service.OnSaveComplete();
        Assert.DoesNotContain(log, line => line.Contains("respawned", StringComparison.Ordinal));
        service.Update();
        Assert.DoesNotContain(log, line => line.Contains("respawned", StringComparison.Ordinal));
        service.Update();
        Assert.Contains(log, line => line.Contains("respawned 2 after save", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_respawn_receipt_reports_elapsed_time_only_after_one_spawn_per_update_finishes()
    {
        var world = new FakeOfficerWorld();
        var log = new List<string>();
        var now = 100d;
        using var service = new OcOfficerRosterService(world, "save-test", log.Add, () => now);
        for (var i = 0; i < 3; i++)
            service.TryRequest($"owner-{i}", new OcOfficerPosition(i, 0f, 0f), out _);
        for (var i = 0; i < 3; i++) service.Update();
        service.OnSaveStart();
        service.OnSaveComplete();
        service.OnSaveComplete(); // repeated notification must not restart elapsed time
        for (var i = 0; i < 3; i++)
        {
            now += 16d;
            service.Update();
            Assert.Equal(4 + i, world.SpawnCalls);
            if (i < 2) Assert.DoesNotContain(log, line => line.Contains("respawned", StringComparison.Ordinal));
        }
        Assert.Contains("OC officers: respawned 3 after save in 48.0 ms (requested=3, failed=0, released=0).", log);
        service.Update();
        Assert.Single(log, line => line.Contains("respawned", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_respawn_receipt_excludes_released_slots_and_is_cancelled_by_load()
    {
        var world = new FakeOfficerWorld();
        var log = new List<string>();
        using var service = new OcOfficerRosterService(world, "save-test", log.Add, () => 0d);
        service.TryRequest("a", new(0f, 0f, 0f), out var released);
        service.TryRequest("b", new(1f, 0f, 0f), out _);
        service.Update();
        service.Update();
        service.OnSaveStart();
        service.OnSaveComplete();
        service.Release(released);
        service.Update();
        Assert.Contains("OC officers: respawned 1 after save in 0.0 ms (requested=2, failed=0, released=1).", log);
        log.Clear();
        service.OnSaveStart();
        service.OnSaveComplete();
        service.OnPreLoad();
        service.Update();
        Assert.DoesNotContain(log, line => line.Contains("respawned", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_respawn_waits_for_template_and_reports_terminal_failure_as_failed()
    {
        var world = new FakeOfficerWorld();
        var log = new List<string>();
        using var service = new OcOfficerRosterService(world, "save-test", log.Add, () => 0d);
        service.TryRequest("a", new(0f, 0f, 0f), out _);
        service.Update();
        service.OnSaveStart();
        service.OnSaveComplete();
        world.EnsureResult = OcOfficerWorldStatus.NotReady;
        service.Update();
        Assert.DoesNotContain(log, line => line.Contains("respawned", StringComparison.Ordinal));
        world.EnsureResult = OcOfficerWorldStatus.Ok;
        world.SpawnResult = OcOfficerWorldStatus.Failed;
        for (var i = 0; i <= OcOfficerRoster.MaxAttempts * OcOfficerRoster.AttemptBackoffUpdates; i++) service.Update();
        Assert.Contains("OC officers: respawned 0 after save in 0.0 ms (requested=1, failed=1, released=0).", log);
        Assert.Single(log, line => line.Contains("respawned", StringComparison.Ordinal));
    }

    [Fact]
    public void Pre_load_without_a_server_logs_one_summary_and_skips_removal()
    {
        var world = new FakeOfficerWorld();
        var log = new List<string>();
        var service = Create(world, log);
        service.TryRequest("a", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.TryRequest("b", new OcOfficerPosition(1f, 0f, 0f), out _);
        service.Update();
        service.Update();
        world.Host = false;

        service.OnPreLoad();

        Assert.Equal(0, service.Count);
        Assert.Equal(0, world.RemoveCalls);
        Assert.Empty(log.Where(line => line.Contains("removal failed", StringComparison.Ordinal)));
        Assert.Single(log, line => line.Contains("no server", StringComparison.Ordinal));
    }

    [Fact]
    public void Pre_load_with_a_server_still_logs_failed_removals_per_clone()
    {
        var world = new FakeOfficerWorld { RemoveResult = OcOfficerWorldStatus.Failed };
        var log = new List<string>();
        var service = Create(world, log);
        service.TryRequest("a", new OcOfficerPosition(0f, 0f, 0f), out _);
        service.TryRequest("b", new OcOfficerPosition(1f, 0f, 0f), out _);
        service.Update();
        service.Update();

        service.OnPreLoad();

        Assert.Equal(2, world.RemoveCalls);
        Assert.Equal(2, log.Count(line => line.Contains("removal failed", StringComparison.Ordinal)));
    }

    private sealed class FakeOfficerWorld : IOcOfficerWorld
    {
        private int _serial;

        public bool Host { get; set; } = true;
        public bool Throw { get; set; }
        public double Minutes { get; set; }
        public bool KnockedOut { get; set; }
        public OcOfficerWorldStatus EnsureResult { get; set; } = OcOfficerWorldStatus.Ok;
        public OcOfficerWorldStatus SpawnResult { get; set; } = OcOfficerWorldStatus.Ok;
        public OcOfficerWorldStatus RemoveResult { get; set; } = OcOfficerWorldStatus.Ok;
        public OcOfficerWorldStatus BindResult { get; set; } = OcOfficerWorldStatus.Ok;
        public OcOfficerWorldStatus PoseResult { get; set; } = OcOfficerWorldStatus.Ok;
        public int SpawnCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int DestroyTemplateCalls { get; private set; }
        public object? BoundCheckpoint { get; private set; }
        public OcOfficerPosition? BindStandPoint { get; private set; }
        public string BehaviourName { get; set; } = "Checkpoint";
        public bool CheckpointActive { get; set; } = true;
        public bool BoundActive { get; set; } = true;
        public bool BoundEnabled { get; set; } = true;
        public OcCheckpointKind BoundKind { get; set; } = OcCheckpointKind.Ours;
        public OcOfficerPosition? BoundStandPoint { get; set; }
        public string DiagActive { get; set; } = "Follow Schedule";
        public List<OcOfficerBehaviourInfo> DiagStack { get; } = new();
        public OcOfficerCheckpointDetail DiagCheckpoint { get; set; } = new(true, false, "Western", OcCheckpointKind.Ours);
        public OcOfficerPosition ClonePosition { get; set; } = new OcOfficerPosition(3f, 0f, 4f);
        public int BoundCloneSerial { get; private set; } = -1;

        public bool IsHost
        {
            get
            {
                if (Throw) throw new InvalidOperationException("fake host read threw");
                return Host;
            }
        }

        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes)
        {
            if (Throw) throw new InvalidOperationException("fake clock threw");
            minutes = Minutes;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake ensure threw");
            status = new OcOfficerTemplateStatus(Present: true, Registered: EnsureResult == OcOfficerWorldStatus.Ok, PrefabId: 120, TemplateId: templateId);
            detail = "fake template";
            return EnsureResult;
        }

        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake spawn threw");
            SpawnCalls++;
            _serial++;
            clone = new OcOfficerCloneRef(new object(), 1000L + _serial, _serial);
            detail = SpawnResult == OcOfficerWorldStatus.Ok ? "fake spawned" : "fake spawn failed";
            return SpawnResult;
        }

        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut)
        {
            if (Throw) throw new InvalidOperationException("fake knockout read threw");
            knockedOut = KnockedOut;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position)
        {
            if (Throw) throw new InvalidOperationException("fake position read threw");
            position = ClonePosition;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
        {
            if (Throw) throw new InvalidOperationException("fake behaviour read threw");
            activeBehaviour = BehaviourName;
            checkpointActive = CheckpointActive;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state)
        {
            if (Throw) throw new InvalidOperationException("fake bound read threw");
            state = new OcOfficerBoundState(BoundActive, BoundEnabled, BoundKind, BoundStandPoint);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics)
        {
            if (Throw) throw new InvalidOperationException("fake diagnostics read threw");
            diagnostics = new OcOfficerBehaviourDiagnostics(DiagStack, DiagActive, DiagCheckpoint);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake bind threw");
            BoundCheckpoint = nativeCheckpoint;
            BindStandPoint = standPoint;
            BoundCloneSerial = clone.Serial;
            detail = "fake bound";
            return BindResult;
        }

        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake remove threw");
            RemoveCalls++;
            detail = "fake removed";
            return RemoveResult;
        }

        public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake template destroy threw");
            DestroyTemplateCalls++;
            detail = "fake template destroyed";
            return OcOfficerWorldStatus.Ok;
        }

        public int ClearSwapMapCalls { get; private set; }

        public OcOfficerWorldStatus TryClearSwapMap(out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake swap clear threw");
            ClearSwapMapCalls++;
            detail = "fake swap map cleared";
            return OcOfficerWorldStatus.Ok;
        }

        public int UnmapCalls { get; private set; }
        public OcOfficerCloneRef LastUnmapped { get; private set; }
        public bool NativeLeaked { get; set; }
        public string? LeakLabel { get; private set; }

        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake swap unmap threw");
            UnmapCalls++;
            LastUnmapped = clone;
            detail = "fake swap entry cleared";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail)
        {
            if (Throw) throw new InvalidOperationException("fake leak read threw");
            LeakLabel = nativeLabel;
            leaked = NativeLeaked;
            detail = leaked ? "fake leak" : "fake no leak";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
        {
            if (Throw) throw new InvalidOperationException("fake template read threw");
            status = new OcOfficerTemplateStatus(Present: true, Registered: true, PrefabId: 120, TemplateId: "fake-template");
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts)
        {
            if (Throw) throw new InvalidOperationException("fake counts read threw");
            counts = new OcOfficerNativeCounts(Officers: 10, Registry: 110);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees)
        {
            if (Throw) throw new InvalidOperationException("fake pose read threw");
            position = new OcOfficerPosition(0f, 0f, 0f);
            yawDegrees = 0f;
            return PoseResult;
        }
    }
    [Fact]
    public void Template_not_ready_spends_no_attempts_over_many_frames()
    {
        var world = new FakeOfficerWorld { EnsureResult = OcOfficerWorldStatus.NotReady };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        for (var i = 0; i < 100; i++)
            service.Update();
        var slot = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, slot.State);
        Assert.Equal(0, slot.Attempts);
    }

    [Fact]
    public void Template_fault_rebuilds_without_counting_the_attempt()
    {
        var world = new FakeOfficerWorld { SpawnResult = OcOfficerWorldStatus.TemplateFault, Minutes = 500d };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        for (var i = 0; i < 3; i++)
            service.Update();
        var slot = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, slot.State);
        Assert.Equal(0, slot.Attempts);
        Assert.Equal(3, world.DestroyTemplateCalls);

        world.SpawnResult = OcOfficerWorldStatus.Ok;
        service.Update();
        Assert.Equal(OcOfficerSlotState.Active, Assert.Single(service.Snapshot()).State);
    }

    [Fact]
    public void Template_rebuild_cap_logs_once_then_counts_attempts()
    {
        var world = new FakeOfficerWorld { SpawnResult = OcOfficerWorldStatus.TemplateFault, Minutes = 500d };
        var log = new List<string>();
        var service = Create(world, log);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        for (var i = 0; i < 70; i++)
            service.Update();
        var slot = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Failed, slot.State);
        Assert.Equal(OcOfficerRoster.MaxAttempts, slot.Attempts);
        Assert.Equal(3, world.DestroyTemplateCalls);
        Assert.Equal(1, log.Count(line => line.Contains("rebuild cap", StringComparison.Ordinal)));
    }

    [Fact]
    public void Failed_slot_retries_after_30_game_minutes()
    {
        var world = new FakeOfficerWorld { SpawnResult = OcOfficerWorldStatus.Failed, Minutes = 500d };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out _);
        for (var i = 0; i < 70; i++)
            service.Update();
        Assert.Equal(OcOfficerSlotState.Failed, Assert.Single(service.Snapshot()).State);
        var before = Assert.Single(service.Snapshot()).CloneId;

        world.Minutes = 500d + OcOfficerRoster.FailedRetryMinutes - 1d;
        service.Update();
        Assert.Equal(OcOfficerSlotState.Failed, Assert.Single(service.Snapshot()).State);

        world.Minutes = 500d + OcOfficerRoster.FailedRetryMinutes;
        service.Update();
        var retried = Assert.Single(service.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, retried.State);
        Assert.Equal(0, retried.Attempts);
        Assert.NotEqual(before, retried.CloneId);
    }

    [Fact]
    public void Bound_state_read_returns_checkpoint_detail_for_an_active_clone()
    {
        var stand = new OcOfficerPosition(1f, 2f, 3f);
        var world = new FakeOfficerWorld
        {
            BoundActive = true,
            BoundEnabled = true,
            BoundKind = OcCheckpointKind.Ours,
            BoundStandPoint = stand,
        };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        Assert.False(service.TryReadBoundState(handle, new object(), out _));

        service.Update();

        Assert.True(service.TryReadBoundState(handle, new object(), out var state));
        Assert.True(state.CheckpointActive);
        Assert.True(state.CheckpointEnabled);
        Assert.Equal(OcCheckpointKind.Ours, state.CheckpointKind);
        Assert.Equal(stand, state.StandPoint);
    }

    [Fact]
    public void Bound_state_read_fails_closed_on_dispose_and_a_throwing_world()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();
        Assert.True(service.TryReadBoundState(handle, new object(), out _));

        world.Throw = true;
        Assert.False(service.TryReadBoundState(handle, new object(), out _));

        world.Throw = false;
        service.Dispose();
        Assert.False(service.TryReadBoundState(handle, new object(), out _));
    }

    [Fact]
    public void Behaviour_diagnostics_read_returns_the_stack_for_an_active_clone()
    {
        var world = new FakeOfficerWorld
        {
            DiagActive = "Follow Schedule",
            DiagCheckpoint = new OcOfficerCheckpointDetail(true, false, "Western", OcCheckpointKind.Ours),
        };
        world.DiagStack.Add(new OcOfficerBehaviourInfo("Follow Schedule", 50, true, true));
        world.DiagStack.Add(new OcOfficerBehaviourInfo("Checkpoint", 40, true, false));
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        Assert.False(service.TryReadBehaviourDiagnostics(handle, new object(), out _));

        service.Update();

        Assert.True(service.TryReadBehaviourDiagnostics(handle, new object(), out var diagnostics));
        Assert.Equal("Follow Schedule", diagnostics.ActiveName);
        Assert.Equal(2, diagnostics.EnabledTopFirst.Count);
        Assert.Equal("Checkpoint", diagnostics.EnabledTopFirst[1].Name);
        Assert.True(diagnostics.Checkpoint.Enabled);
        Assert.False(diagnostics.Checkpoint.Active);
        Assert.Equal("Western", diagnostics.Checkpoint.Assigned);
        Assert.Equal(OcCheckpointKind.Ours, diagnostics.Checkpoint.Kind);
    }

    [Fact]
    public void Behaviour_diagnostics_read_fails_closed_on_dispose_and_a_throwing_world()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();
        Assert.True(service.TryReadBehaviourDiagnostics(handle, new object(), out _));

        world.Throw = true;
        Assert.False(service.TryReadBehaviourDiagnostics(handle, new object(), out _));

        world.Throw = false;
        service.Dispose();
        Assert.False(service.TryReadBehaviourDiagnostics(handle, new object(), out _));
    }

    [Fact]
    public void Release_unmaps_the_live_clone()
    {
        var world = new FakeOfficerWorld();
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();

        Assert.True(service.Release(handle));
        Assert.Equal(1, world.UnmapCalls);
        Assert.Equal(1, world.LastUnmapped.Serial);
    }

    [Fact]
    public void Release_unmaps_even_when_removal_fails()
    {
        var world = new FakeOfficerWorld { RemoveResult = OcOfficerWorldStatus.Failed };
        var service = Create(world);
        service.TryRequest("owner", new OcOfficerPosition(0f, 0f, 0f), out var handle);
        service.Update();

        Assert.True(service.Release(handle));
        Assert.Equal(1, world.RemoveCalls);
        Assert.Equal(1, world.UnmapCalls);
    }

    [Fact]
    public void Native_leak_read_passes_the_asserted_label_through()
    {
        var world = new FakeOfficerWorld { NativeLeaked = true };
        var service = Create(world);

        Assert.True(service.TryReadNativeLeak("Docks", out var leaked));
        Assert.True(leaked);
        Assert.Equal("Docks", world.LeakLabel);
    }

    [Fact]
    public void Native_leak_read_fails_closed_on_dispose_and_a_throwing_world()
    {
        var world = new FakeOfficerWorld { NativeLeaked = true };
        var service = Create(world);
        Assert.True(service.TryReadNativeLeak("Docks", out _));

        world.Throw = true;
        Assert.False(service.TryReadNativeLeak("Docks", out var leaked));
        Assert.False(leaked);

        world.Throw = false;
        service.Dispose();
        Assert.False(service.TryReadNativeLeak("Docks", out _));
    }

    [Fact]
    public void ClearAll_clears_the_swap_map_without_a_server()
    {
        var world = new FakeOfficerWorld { Host = false };
        var service = Create(world);

        service.OnPreLoad();
        service.OnLoadComplete();
        service.Dispose();

        Assert.Equal(3, world.ClearSwapMapCalls);
    }

}
