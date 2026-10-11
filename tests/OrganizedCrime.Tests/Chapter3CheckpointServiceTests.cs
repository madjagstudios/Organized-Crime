using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CheckpointServiceTests
{
    private const string PlayerId = "76561190000000001";
    private const double Day5SixAm = 5 * 1440 + 360;

    [Fact]
    public void Pick_at_06_00_writes_the_busiest_property_and_day()
    {
        using var harness = CheckpointHarness.Create();

        harness.Tick();

        var target = harness.Runtime.ReadCheckpointTarget();
        Assert.Equal("barn", target.PropertyCode);
        Assert.Equal(5, target.PickDay);
    }

    [Fact]
    public void Pick_runs_once_per_day()
    {
        using var harness = CheckpointHarness.Create();
        harness.Tick();
        var revision = harness.Runtime.State!.Revision;

        harness.Minutes = 5 * 1440 + 720;
        harness.Tick();
        harness.Minutes = 5 * 1440 + 1439;
        harness.Tick();
        Assert.Equal(revision, harness.Runtime.State!.Revision);
        Assert.Equal(5, harness.Runtime.ReadCheckpointTarget().PickDay);

        harness.Minutes = 6 * 1440 + 360;
        harness.Tick();
        Assert.Equal(6, harness.Runtime.ReadCheckpointTarget().PickDay);
        Assert.Equal(revision + 1, harness.Runtime.State!.Revision);
    }

    [Fact]
    public void Pick_runs_after_load_when_today_has_none_even_before_06_00()
    {
        using var harness = CheckpointHarness.Create();
        harness.Minutes = 5 * 1440 + 180;
        harness.LoadCycle();

        harness.Tick();

        var target = harness.Runtime.ReadCheckpointTarget();
        Assert.Equal("barn", target.PropertyCode);
        Assert.Equal(5, target.PickDay);
    }

    [Fact]
    public void No_pick_after_load_when_today_has_one()
    {
        using var harness = CheckpointHarness.Create();
        harness.Tick();
        var revision = harness.Runtime.State!.Revision;
        harness.RuntimeRepo.Stored = harness.Runtime.State;
        harness.Minutes = 5 * 1440 + 900;
        harness.LoadCycle();

        harness.Tick();

        Assert.Equal(revision, harness.Runtime.State!.Revision);
        Assert.Equal("barn", harness.Runtime.ReadCheckpointTarget().PropertyCode);
    }

    [Fact]
    public void Pick_with_no_owned_property_records_no_target_and_spawns_nothing()
    {
        using var harness = CheckpointHarness.Create();
        harness.Courtesy.Properties = Array.Empty<Release1ProductionPropertySnapshot>();
        harness.Checkpoints.OwnedPositions = Array.Empty<Chapter3OwnedPropertyPosition>();
        harness.SetHeat(80);

        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var target = harness.Runtime.ReadCheckpointTarget();
        Assert.Null(target.PropertyCode);
        Assert.Equal(5, target.PickDay);
        Assert.Equal(0, harness.Checkpoints.SpawnCalls);
        Assert.Equal(0, harness.Roster.Count);
    }

    [Fact]
    public void Watched_spawns_the_nearest_spot_and_asserts_the_nearest_native()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(60);

        harness.Tick();

        var snapshot = harness.Service.Snapshot();
        Assert.Equal(new[] { "outside-barn" }, snapshot.ActiveLabels);
        var entry = Assert.Single(snapshot.Managed);
        Assert.Equal("outside-barn", entry.Label);
        Assert.True(entry.Spawned);
        Assert.Equal("Docks", snapshot.AssertedNative);
        Assert.Contains(harness.Checkpoints.NativeSets, call => call.Location == "Docks" && call.Enabled);
        harness.Tick();
        Assert.Equal(1, harness.Checkpoints.SpawnCalls);
    }

    [Fact]
    public void Critical_spawns_three_spots_one_per_minute()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(80);

        harness.Minutes = Day5SixAm;
        harness.Tick();
        Assert.Equal(new[] { "outside-barn" }, ManagedLabels(harness));
        harness.Tick();
        Assert.Equal(1, harness.Checkpoints.SpawnCalls);

        harness.Minutes = Day5SixAm + 1;
        harness.Tick();
        Assert.Equal(2, harness.Checkpoints.SpawnCalls);

        harness.Minutes = Day5SixAm + 2;
        harness.Tick();
        Assert.Equal(
            new[] { "outside-barn", "outside-manor", "outside-storage-unit" },
            ManagedLabels(harness).OrderBy(label => label, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Cooling_below_Watched_despawns_and_releases_officers()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(80);
        for (var minute = 0; minute < 10; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        Assert.Equal(3, harness.Service.Snapshot().Managed.Count);
        Assert.Equal(6, harness.Roster.Count);

        harness.SetHeat(10);
        for (var minute = 10; minute < 13; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Equal(3, harness.Checkpoints.DespawnCalls);
        Assert.Equal(0, harness.Roster.Count);
    }

    [Fact]
    public void Protection_clears_everything_and_stops_the_native_assert()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(80);
        for (var minute = 0; minute < 10; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        Assert.Equal(6, harness.Roster.Count);
        var assertsBefore = harness.Checkpoints.NativeSets.Count;

        harness.Protect(untilDay: 8);
        for (var minute = 10; minute < 13; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Equal(0, harness.Roster.Count);
        Assert.Null(harness.Service.Snapshot().AssertedNative);
        Assert.Equal(assertsBefore, harness.Checkpoints.NativeSets.Count);
    }

    [Fact]
    public void Two_officers_requested_and_bound_per_checkpoint()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(60);

        for (var minute = 0; minute < 8; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.Equal(2, entry.Officers.Count);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Bound, officer.BindState));
        Assert.Equal(
            new[] { "checkpoint:outside-barn:0", "checkpoint:outside-barn:1" },
            harness.Roster.Snapshot().Select(slot => slot.OwnerKey).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.NotNull(harness.Officers.BoundCheckpoint);
    }

    [Fact]
    public void Native_assert_repeats_each_pass_and_never_disables()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(60);

        for (var minute = 0; minute < 3; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        Assert.Equal(3, harness.Checkpoints.NativeSets.Count);
        Assert.All(harness.Checkpoints.NativeSets, call => Assert.True(call.Enabled));

        harness.SetHeat(10);
        harness.Minutes = Day5SixAm + 3;
        harness.Tick();
        Assert.Equal(3, harness.Checkpoints.NativeSets.Count);
    }

    [Fact]
    public void Update_is_host_only()
    {
        using var harness = CheckpointHarness.Create();
        harness.Checkpoints.Host = false;
        harness.Officers.Host = false;
        harness.SetHeat(80);

        harness.Tick();

        Assert.Null(harness.Runtime.ReadCheckpointTarget().PickDay);
        Assert.Equal(0, harness.Checkpoints.SpawnCalls);

        harness.Checkpoints.Host = true;
        harness.Officers.Host = true;
        harness.Tick();

        Assert.Equal(5, harness.Runtime.ReadCheckpointTarget().PickDay);
    }

    [Fact]
    public void PreLoad_clears_without_host_and_touches_no_world()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(80);
        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        Assert.NotEmpty(harness.Service.Snapshot().Managed);

        harness.Checkpoints.Host = false;
        var despawnsBefore = harness.Checkpoints.DespawnCalls;
        harness.Service.OnPreLoad();

        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Null(harness.Service.Snapshot().AssertedNative);
        Assert.Equal(despawnsBefore, harness.Checkpoints.DespawnCalls);
    }

    [Fact]
    public void Save_complete_rebinds_held_officers()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(60);
        for (var minute = 0; minute < 8; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Bound, officer.BindState));
        var bindsBefore = harness.Officers.BindCalls;

        harness.Roster.OnSaveStart();
        harness.Service.OnSaveStart();
        harness.Roster.OnSaveComplete();
        harness.Service.OnSaveComplete();
        for (var minute = 8; minute < 12; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.True(harness.Officers.BindCalls > bindsBefore);
        entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Bound, officer.BindState));
    }

    [Fact]
    public void Failed_spawn_logs_once_and_retries_on_a_later_pass()
    {
        using var harness = CheckpointHarness.Create();
        harness.Checkpoints.SpawnResult = Chapter3CheckpointWorldStatus.Failed;
        harness.SetHeat(60);

        harness.Minutes = Day5SixAm;
        harness.Tick();
        harness.Tick();
        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Equal(1, harness.Logs.Count(line => line.Contains("outside-barn", StringComparison.Ordinal)));

        harness.Checkpoints.SpawnResult = Chapter3CheckpointWorldStatus.Ok;
        harness.Minutes = Day5SixAm + 1;
        harness.Tick();
        Assert.Equal(new[] { "outside-barn" }, ManagedLabels(harness));
        Assert.Equal(1, harness.Logs.Count(line => line.Contains("outside-barn", StringComparison.Ordinal)));
    }

    [Fact]
    public void QaSpawnTest_spawns_at_the_baked_spot_with_two_officers()
    {
        using var harness = CheckpointHarness.Create();

        var result = harness.Service.QaSpawnTest("outside-barn");

        Assert.True(result.Ok, result.Message);
        Assert.Equal("outside-barn", Assert.Single(harness.Service.Snapshot().Tests).Label);
        for (var minute = 0; minute < 8; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        var entry = Assert.Single(harness.Service.Snapshot().Tests);
        Assert.Equal(2, entry.Officers.Count);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Bound, officer.BindState));
        Assert.Equal(
            new[] { "checkpoint-test:outside-barn:0", "checkpoint-test:outside-barn:1" },
            harness.Roster.Snapshot().Select(slot => slot.OwnerKey).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void QaSpawnTest_refuses_unknown_labels()
    {
        using var harness = CheckpointHarness.Create();

        var result = harness.Service.QaSpawnTest("nowhere");

        Assert.False(result.Ok);
        Assert.Equal(0, harness.Checkpoints.SpawnCalls);
    }

    [Fact]
    public void QaSpawnTestAt_spawns_at_the_given_pose()
    {
        using var harness = CheckpointHarness.Create();

        var result = harness.Service.QaSpawnTestAt("outside-x", 1f, 2f, 3f, 45f);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(new FakeSpawn("outside-x", 1f, 2f, 3f, 45f), harness.Checkpoints.LastSpawn);
    }

    [Fact]
    public void QaSpotHere_logs_the_paste_ready_line_and_spawns()
    {
        using var harness = CheckpointHarness.Create();
        harness.Checkpoints.CameraPose = new Chapter3CameraPose(1.2f, 0f, 9.9f, 12.5f);

        var result = harness.Service.QaSpotHere("outside-x");

        Assert.True(result.Ok, result.Message);
        Assert.Contains("new Chapter3CheckpointSpot(\"outside-x\", 1.20f, 0.00f, 9.90f, 12.50f),", result.Message);
        Assert.Equal(new FakeSpawn("outside-x", 1.2f, 0f, 9.9f, 12.5f), harness.Checkpoints.LastSpawn);
        Assert.Equal("outside-x", Assert.Single(harness.Service.Snapshot().Tests).Label);
    }

    [Fact]
    public void QaSpotHere_refuses_off_host_and_with_an_unreadable_camera()
    {
        using var harness = CheckpointHarness.Create();
        harness.Checkpoints.Host = false;

        Assert.False(harness.Service.QaSpotHere("outside-x").Ok);

        harness.Checkpoints.Host = true;
        harness.Checkpoints.CameraResult = Chapter3CheckpointWorldStatus.NotReady;
        Assert.False(harness.Service.QaSpotHere("outside-x").Ok);
        Assert.Equal(0, harness.Checkpoints.SpawnCalls);
    }

    [Fact]
    public void QaClearTests_removes_tests_and_releases_officers()
    {
        using var harness = CheckpointHarness.Create();
        Assert.True(harness.Service.QaSpawnTest("outside-barn").Ok);
        Assert.True(harness.Service.QaSpawnTest("outside-manor").Ok);

        var result = harness.Service.QaClearTests();

        Assert.True(result.Ok, result.Message);
        Assert.Contains("2", result.Message);
        Assert.Empty(harness.Service.Snapshot().Tests);
        Assert.Equal(2, harness.Checkpoints.DespawnCalls);
        Assert.Equal(0, harness.Roster.Count);
    }

    [Fact]
    public void QaPickNow_picks_regardless_of_hour()
    {
        using var harness = CheckpointHarness.Create();
        harness.Minutes = 5 * 1440 + 180;

        var result = harness.Service.QaPickNow();

        Assert.True(result.Ok, result.Message);
        var target = harness.Runtime.ReadCheckpointTarget();
        Assert.Equal("barn", target.PropertyCode);
        Assert.Equal(5, target.PickDay);
    }

    [Fact]
    public void QaStatus_reports_tier_target_active_and_native()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(80);
        for (var minute = 0; minute < 10; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var lines = harness.Service.QaStatus();

        Assert.Contains(lines, line => line.Contains("Critical", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("barn", StringComparison.Ordinal) && line.Contains("day 5", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("outside-barn", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Docks", StringComparison.Ordinal));
    }

    [Fact]
    public void Managed_reconcile_leaves_test_checkpoints_alone()
    {
        using var harness = CheckpointHarness.Create();
        Assert.True(harness.Service.QaSpawnTest("outside-barn").Ok);
        harness.Tick();
        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Single(harness.Service.Snapshot().Tests);

        harness.SetHeat(80);
        harness.Minutes = Day5SixAm + 1;
        harness.Tick();
        Assert.Single(harness.Service.Snapshot().Managed);
        Assert.Single(harness.Service.Snapshot().Tests);

        harness.SetHeat(10);
        harness.Minutes = Day5SixAm + 2;
        harness.Tick();
        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Single(harness.Service.Snapshot().Tests);
    }

    [Fact]
    public void Officers_are_requested_at_stand_point_positions_not_eye_height()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(60);
        harness.Tick();
        harness.Tick();

        var positions = harness.Roster.Snapshot().Select(slot => slot.Position).OrderBy(position => position.X).ToArray();

        Assert.Equal(2, positions.Length);
        Assert.Equal(new OcOfficerPosition(3f, 0.5f, 4f, 88.24f), positions[0]);
        Assert.Equal(new OcOfficerPosition(4f, 0.5f, 5f, 88.24f), positions[1]);
        Assert.Equal(new OcOfficerPosition(3f, 0.5f, 4f, 88.24f), harness.Officers.BindStandPoint);
    }

    [Fact]
    public void Officer_stays_Binding_until_active_and_near_then_Bound()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "FootPatrol";
        harness.Officers.CheckpointActive = false;
        harness.Officers.ClonePosition = new OcOfficerPosition(30f, 0f, 40f);
        harness.SetHeat(60);
        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Binding, officer.BindState));

        harness.Officers.BehaviourName = "Checkpoint";
        harness.Officers.CheckpointActive = true;
        harness.Officers.ClonePosition = new OcOfficerPosition(3f, 0.5f, 4f);
        harness.Minutes = Day5SixAm + 5;
        harness.Tick();

        entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Bound, officer.BindState));
    }

    [Theory]
    [InlineData(6.9f, Chapter3BindState.Bound)]
    [InlineData(8.2f, Chapter3BindState.Binding)]
    public void Bound_requires_within_3m_of_the_stand_point(float cloneZ, Chapter3BindState expected)
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "Checkpoint";
        harness.Officers.CheckpointActive = true;
        harness.Officers.ClonePosition = new OcOfficerPosition(3f, 0.5f, cloneZ);
        harness.SetHeat(60);
        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(expected, officer.BindState));
    }

    [Fact]
    public void Binding_timeout_logs_once_and_rebinds()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "FootPatrol";
        harness.Officers.CheckpointActive = false;
        harness.SetHeat(60);
        for (var minute = 0; minute < 25; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Binding, officer.BindState));
        Assert.Equal(2, harness.Logs.Count(line => line.Contains("bind timed out", StringComparison.Ordinal)));
        Assert.True(harness.Officers.BindCalls >= 4);
    }

    [Fact]
    public void Status_reports_per_officer_state_name_and_distance()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "FootPatrol";
        harness.Officers.CheckpointActive = false;
        harness.SetHeat(60);
        harness.Tick();
        harness.Tick();

        var lines = harness.Service.QaStatus();

        Assert.Contains(lines, line => line.Contains("Binding", StringComparison.Ordinal)
            && line.Contains("FootPatrol", StringComparison.Ordinal)
            && line.Contains("m from stand", StringComparison.Ordinal));
    }

    [Fact]
    public void Officers_wait_while_stand_points_are_unreadable()
    {
        using var harness = CheckpointHarness.Create();
        harness.Checkpoints.StandResult = Chapter3CheckpointWorldStatus.Failed;
        harness.SetHeat(60);
        harness.Tick();
        harness.Tick();

        Assert.Equal(0, harness.Roster.Count);
        Assert.Equal(1, harness.Logs.Count(line => line.Contains("outside-barn", StringComparison.Ordinal)));

        harness.Checkpoints.StandResult = Chapter3CheckpointWorldStatus.Ok;
        harness.Minutes = Day5SixAm + 1;
        harness.Tick();

        Assert.Equal(2, harness.Roster.Count);
    }

    [Fact]
    public void Checkpoint_qa_refuses_while_unwired()
    {
        var service = new Chapter3RuntimeService(new Ch3FakeContext(), new Ch3FakeRepository(), new Ch3FakeGate(), new Ch3FakeClock());

        Assert.Equal(new[] { "checkpoints: the service is not wired." }, service.QaCheckpointStatus());
        Assert.False(service.QaCheckpointSpotHere("outside-x").Ok);
        Assert.False(service.QaCheckpointTest("outside-barn").Ok);
        Assert.False(service.QaCheckpointClear().Ok);
        Assert.False(service.QaCheckpointPick().Ok);
    }

    [Fact]
    public void Checkpoint_qa_flows_through_the_command_to_the_wired_service()
    {
        using var harness = CheckpointHarness.Create();
        harness.Runtime.Checkpoints = harness.Service;
        harness.SetHeat(60);
        harness.Tick();

        var status = Chapter3QaCommand.Execute(harness.Runtime, new[] { "checkpoint", "status" }, qaEnabled: true);

        Assert.Contains(status, line => line.Contains("Watched", StringComparison.Ordinal));
        Assert.Contains(status, line => line.Contains("barn", StringComparison.Ordinal));

        var test = Chapter3QaCommand.Execute(harness.Runtime, new[] { "checkpoint", "test", "outside-manor" }, qaEnabled: true);

        Assert.Equal(new[] { "Test checkpoint spawned: outside-manor." }, test);
        status = Chapter3QaCommand.Execute(harness.Runtime, new[] { "checkpoint", "status" }, qaEnabled: true);
        Assert.Contains(status, line => line.Contains("outside-manor", StringComparison.Ordinal) && line.Contains("test", StringComparison.Ordinal));

        var pick = Chapter3QaCommand.Execute(harness.Runtime, new[] { "checkpoint", "pick" }, qaEnabled: true);

        Assert.Equal(new[] { "Checkpoint target picked: barn (day 5)." }, pick);

        var clear = Chapter3QaCommand.Execute(harness.Runtime, new[] { "checkpoint", "clear" }, qaEnabled: true);

        Assert.Equal(new[] { "Cleared 1 test checkpoint(s)." }, clear);
    }

    private static IReadOnlyList<string> ManagedLabels(CheckpointHarness harness) =>
        harness.Service.Snapshot().Managed.Select(entry => entry.Label).ToList();

    private sealed class CheckpointHarness : IDisposable
    {
        public Chapter3RuntimeService Runtime { get; }
        public Ch3FakeRepository RuntimeRepo { get; }
        public Release1StoryRuntimeService Story { get; }
        public FakeLedger Ledger { get; }
        public FakeChiefWorld ChiefWorld { get; }
        public Release1ChiefService Chief { get; }
        public FakeCourtesy Courtesy { get; }
        public FakeCheckpointWorld Checkpoints { get; }
        public FakeOfficerWorld Officers { get; }
        public OcOfficerRosterService Roster { get; }
        public Chapter3CheckpointService Service { get; }
        public List<string> Logs { get; }

        private CheckpointHarness(
            Chapter3RuntimeService runtime,
            Ch3FakeRepository runtimeRepo,
            Release1StoryRuntimeService story,
            FakeLedger ledger,
            FakeChiefWorld chiefWorld,
            Release1ChiefService chief,
            FakeCourtesy courtesy,
            FakeCheckpointWorld checkpoints,
            FakeOfficerWorld officers,
            OcOfficerRosterService roster,
            Chapter3CheckpointService service,
            List<string> logs)
        {
            Runtime = runtime;
            RuntimeRepo = runtimeRepo;
            Story = story;
            Ledger = ledger;
            ChiefWorld = chiefWorld;
            Chief = chief;
            Courtesy = courtesy;
            Checkpoints = checkpoints;
            Officers = officers;
            Roster = roster;
            Service = service;
            Logs = logs;
            Minutes = Day5SixAm;
        }

        public double Minutes
        {
            get => Courtesy.TotalMinutes ?? 0d;
            set
            {
                Courtesy.TotalMinutes = value;
                ChiefWorld.TotalMinutes = value;
                Officers.Minutes = value;
            }
        }

        public static CheckpointHarness Create()
        {
            var runtimeRepo = new Ch3FakeRepository();
            var runtime = new Chapter3RuntimeService(
                new Ch3FakeContext(), runtimeRepo, new Ch3FakeGate { Satisfied = true }, new Ch3FakeClock { ElapsedDays = 11 });
            runtime.OnPreLoad();
            runtime.OnLoadComplete();
            runtime.OnDayPassed();
            Assert.Equal(Chapter3RuntimePhase.Active, runtime.Phase);

            var storyContext = new Release1SmallCourtesyDepositTests.FakeContext();
            var story = new Release1StoryRuntimeService(storyContext, new Release1SmallCourtesyDepositTests.FakeRepository());
            story.OnPreLoad();
            story.OnLoadComplete();
            const string receipt = "checkpoint-service-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                storyContext.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                storyContext.Snapshot.SessionEpoch, storyContext.Snapshot.LoadEpoch, storyContext.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var ledger = new FakeLedger
            {
                SessionEpoch = storyContext.Snapshot.SessionEpoch,
                LoadEpoch = storyContext.Snapshot.LoadEpoch,
                State = new LocalPressureState(0, false, null, null, null, PlayerId, null, null, 0)
            };
            var chiefWorld = new FakeChiefWorld();
            var chief = new Release1ChiefService(
                story, chiefWorld, new Release1ChiefTierObserver(), ledger.Read, ledger.Wipe, ledger.Epochs, LocalPressureProfile.Moderate);

            var courtesy = new FakeCourtesy();
            var checkpoints = new FakeCheckpointWorld();
            var officers = new FakeOfficerWorld();
            var logs = new List<string>();
            var roster = new OcOfficerRosterService(officers, "test141", logs.Add);
            var service = new Chapter3CheckpointService(runtime, chief, courtesy, checkpoints, roster, logs.Add);
            return new CheckpointHarness(
                runtime, runtimeRepo, story, ledger, chiefWorld, chief, courtesy, checkpoints, officers, roster, service, logs);
        }

        public void SetHeat(int heat) =>
            Ledger.State = new LocalPressureState(heat, false, null, null, null, PlayerId, null, null, 0);

        public void Protect(int untilDay)
        {
            Assert.True(Story.TrySetChiefRecord(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)).Accepted);
            var record = Story.State!.ChiefRecord!;
            Assert.True(Story.TrySetChiefRecord(record with { ProtectedUntilDay = untilDay, Revision = record.Revision + 1 }).Accepted);
            Assert.True(Chief.IsProtected());
        }

        public void Tick()
        {
            Roster.Update();
            Service.Update();
        }

        public void LoadCycle()
        {
            Service.OnPreLoad();
            Roster.OnPreLoad();
            Runtime.OnPreLoad();
            Story.OnPreLoad();
            Service.OnLoadComplete();
            Roster.OnLoadComplete();
            Runtime.OnLoadComplete();
            Story.OnLoadComplete();
            Runtime.OnDayPassed();
            Assert.Equal(Chapter3RuntimePhase.Active, Runtime.Phase);
        }

        public void Dispose()
        {
            Service.Dispose();
            Roster.Dispose();
            Runtime.Dispose();
            Story.Dispose();
            Chief.Dispose();
        }
    }

    private sealed class Ch3FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, PlayerId, System.IO.Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Status;
        }
    }

    private sealed class Ch3FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3State? Stored { get; set; }

        public Chapter3StoreLoadResult Load() =>
            Stored is null
                ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
                : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");

        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Stored = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
        }

        public Chapter3StoreUpdateResult PreserveRawContents(string rawContents) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
    }

    private sealed class Ch3FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; }
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    private sealed class Ch3FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return true;
        }

        public void Raise() => DayPassed?.Invoke();
    }

    private sealed class FakeLedger
    {
        public LocalPressureState? State { get; set; }
        public Guid SessionEpoch { get; set; }
        public long LoadEpoch { get; set; }

        public LocalPressureState? Read(string playerId) => State;

        public LocalPressureEvidenceWriteResult Wipe(string playerId) =>
            new(true, LocalPressureEvidenceWriteRejectReason.None, null, "wiped");

        public bool Epochs(out Guid sessionEpoch, out long loadEpoch)
        {
            sessionEpoch = SessionEpoch;
            loadEpoch = LoadEpoch;
            return true;
        }
    }

    private sealed class FakeChiefWorld : Release1ChiefWorldStub
    {
        public double? TotalMinutes { get; set; }

        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            if (TotalMinutes is { } minutes)
            {
                totalMinutes = minutes;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
    }

    private sealed class FakeCourtesy : Release1ProductionMissionWorldStub
    {
        public double? TotalMinutes { get; set; }
        public IReadOnlyList<Release1ProductionPropertySnapshot> Properties { get; set; } = new[]
        {
            CheckpointHarnessProperty("barn", "Barn", employees: 3, stations: 2),
            CheckpointHarnessProperty("manor", "Manor", employees: 1, stations: 0),
        };

        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = new Release1ProductionActivitySnapshot(Properties);
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            if (TotalMinutes is { } minutes)
            {
                totalMinutes = minutes;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
    }

    private static Release1ProductionPropertySnapshot CheckpointHarnessProperty(string code, string name, int employees, int stations)
    {
        var staff = new List<Release1ProductionEmployeeSnapshot>();
        for (var i = 0; i < employees; i++)
            staff.Add(new Release1ProductionEmployeeSnapshot($"{code}-e{i}", $"Worker {i}", "Botanist", false, true, false, 0, false, null, null, false));
        var kit = new List<Release1ProductionStationSnapshot>();
        for (var i = 0; i < stations; i++)
            kit.Add(new Release1ProductionStationSnapshot($"{code}-s{i}", Release1ProductionStationKind.ChemistryStation, false, null, 0));
        return new Release1ProductionPropertySnapshot(code, name, staff, kit);
    }

    private sealed record FakeNativeSet(string Location, bool Enabled);

    private sealed record FakeSpawn(string Label, float X, float Y, float Z, float YawDegrees);

    private sealed class FakeCheckpointWorld : IChapter3CheckpointWorld
    {
        private int _handles;

        public bool Host { get; set; } = true;
        public IReadOnlyList<Chapter3OwnedPropertyPosition> OwnedPositions { get; set; } = new[]
        {
            new Chapter3OwnedPropertyPosition("barn", 133f, 0f, 11f),
            new Chapter3OwnedPropertyPosition("manor", 143f, 0f, -105f),
        };
        public string? LastVisitedCode { get; set; } = "manor";
        public IReadOnlyList<Chapter3NativeCheckpointPosition> NativePositions { get; set; } = new[]
        {
            new Chapter3NativeCheckpointPosition("Docks", 0f, 0f, 0f),
            new Chapter3NativeCheckpointPosition("Western", 500f, 0f, 500f),
        };
        public Chapter3CheckpointWorldStatus SpawnResult { get; set; } = Chapter3CheckpointWorldStatus.Ok;
        public Chapter3CheckpointWorldStatus CameraResult { get; set; } = Chapter3CheckpointWorldStatus.Ok;
        public Chapter3CheckpointWorldStatus StandResult { get; set; } = Chapter3CheckpointWorldStatus.Ok;
        public IReadOnlyList<Chapter3StandPoint> StandPoints { get; set; } = new[]
        {
            new Chapter3StandPoint(3f, 0.5f, 4f),
            new Chapter3StandPoint(4f, 0.5f, 5f),
        };
        public Chapter3CameraPose CameraPose { get; set; } = new Chapter3CameraPose(0f, 0f, 0f, 0f);
        public int SpawnCalls { get; private set; }
        public FakeSpawn? LastSpawn { get; private set; }
        public int DespawnCalls { get; private set; }
        public List<FakeNativeSet> NativeSets { get; } = new();
        public Dictionary<string, object> Spawned { get; } = new(StringComparer.Ordinal);

        public bool IsHost => Host;

        public Chapter3CheckpointWorldStatus TryReadOwnedPropertyPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions)
        {
            positions = OwnedPositions;
            return Chapter3CheckpointWorldStatus.Ok;
        }

        public Chapter3CheckpointWorldStatus TryReadLastVisitedPropertyCode(out string? propertyCode)
        {
            propertyCode = LastVisitedCode;
            return Chapter3CheckpointWorldStatus.Ok;
        }

        public Chapter3CheckpointWorldStatus TryReadNativeCheckpointPositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions)
        {
            positions = NativePositions;
            return Chapter3CheckpointWorldStatus.Ok;
        }

        public Chapter3CheckpointWorldStatus TrySpawnCheckpoint(string label, float x, float y, float z, float yawDegrees, out string? handle)
        {
            SpawnCalls++;
            LastSpawn = new FakeSpawn(label, x, y, z, yawDegrees);
            if (SpawnResult != Chapter3CheckpointWorldStatus.Ok)
            {
                handle = null;
                return SpawnResult;
            }
            _handles++;
            handle = $"fake-handle-{_handles}";
            Spawned[handle] = new object();
            return Chapter3CheckpointWorldStatus.Ok;
        }

        public List<string>? OrderLog { get; set; }

        public Chapter3CheckpointWorldStatus TryDespawnCheckpoint(string handle)
        {
            DespawnCalls++;
            OrderLog?.Add("despawn");
            Spawned.Remove(handle);
            return Chapter3CheckpointWorldStatus.Ok;
        }

        public Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? nativeCheckpoint)
        {
            if (Spawned.TryGetValue(handle, out var native))
            {
                nativeCheckpoint = native;
                return Chapter3CheckpointWorldStatus.Ok;
            }
            nativeCheckpoint = null;
            return Chapter3CheckpointWorldStatus.InvalidHandle;
        }

        public Chapter3CheckpointWorldStatus TrySetNativeCheckpoint(string location, bool enabled)
        {
            NativeSets.Add(new FakeNativeSet(location, enabled));
            return Chapter3CheckpointWorldStatus.Ok;
        }

        public Chapter3CheckpointWorldStatus TryReadPlayerCameraPose(out Chapter3CameraPose pose)
        {
            pose = CameraPose;
            return CameraResult;
        }

        public Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points)
        {
            points = StandPoints;
            return StandResult;
        }
    }

    private sealed class FakeOfficerWorld : IOcOfficerWorld
    {
        private int _serial;

        public bool Host { get; set; } = true;
        public double Minutes { get; set; }
        public int BindCalls { get; private set; }
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

        public bool IsHost => Host;

        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes)
        {
            minutes = Minutes;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail)
        {
            status = new OcOfficerTemplateStatus(true, true, 120, templateId);
            detail = "fake template";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail)
        {
            _serial++;
            clone = new OcOfficerCloneRef(new object(), 1000L + _serial, _serial);
            detail = "fake spawned";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut)
        {
            knockedOut = false;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position)
        {
            position = ClonePosition;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
        {
            activeBehaviour = BehaviourName;
            checkpointActive = CheckpointActive;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state)
        {
            state = new OcOfficerBoundState(BoundActive, BoundEnabled, BoundKind, BoundStandPoint);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics)
        {
            diagnostics = new OcOfficerBehaviourDiagnostics(DiagStack, DiagActive, DiagCheckpoint);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail)
        {
            BindCalls++;
            BoundCheckpoint = nativeCheckpoint;
            BindStandPoint = standPoint;
            detail = "fake bound";
            return OcOfficerWorldStatus.Ok;
        }

        public List<string>? OrderLog { get; set; }

        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        {
            OrderLog?.Add("remove");
            detail = "fake removed";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
        {
            detail = "fake template destroyed";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryClearSwapMap(out string detail)
        {
            detail = "fake swap map cleared";
            return OcOfficerWorldStatus.Ok;
        }

        public int UnmapCalls { get; private set; }
        public bool NativeLeaked { get; set; }
        public string? LeakLabel { get; private set; }

        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail)
        {
            UnmapCalls++;
            detail = "fake swap entry cleared";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail)
        {
            LeakLabel = nativeLabel;
            leaked = NativeLeaked;
            detail = leaked ? "fake leak" : "fake no leak";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
        {
            status = new OcOfficerTemplateStatus(true, true, 120, "fake-template");
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts)
        {
            counts = new OcOfficerNativeCounts(10, 110);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees)
        {
            position = new OcOfficerPosition(0f, 0f, 0f);
            yawDegrees = 0f;
            return OcOfficerWorldStatus.Ok;
        }
    }
    [Fact]
    public void Bound_requires_the_checkpoint_to_be_ours()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "Checkpoint";
        harness.Officers.CheckpointActive = true;
        harness.Officers.BoundKind = OcCheckpointKind.Native;
        harness.SetHeat(60);
        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Binding, officer.BindState));
    }

    [Fact]
    public void Bound_uses_the_native_stand_point_when_readable()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "Checkpoint";
        harness.Officers.CheckpointActive = true;
        harness.Officers.BoundKind = OcCheckpointKind.Ours;
        harness.Officers.BoundStandPoint = new OcOfficerPosition(100f, 0f, 100f);
        harness.SetHeat(60);
        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var entry = Assert.Single(harness.Service.Snapshot().Managed);
        Assert.All(entry.Officers, officer => Assert.Equal(Chapter3BindState.Binding, officer.BindState));
    }

    [Fact]
    public void Binding_timeout_logs_once_per_officer_but_keeps_rebinding()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "FootPatrol";
        harness.Officers.CheckpointActive = false;
        harness.SetHeat(60);
        for (var minute = 0; minute < 55; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.Equal(2, harness.Logs.Count(line => line.Contains("bind timed out", StringComparison.Ordinal)));
        Assert.True(harness.Officers.BindCalls >= 8);
    }

    [Fact]
    public void Checkpoint_enabled_but_not_top_logs_the_blocker_once()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "Follow Schedule";
        harness.Officers.CheckpointActive = false;
        harness.Officers.BoundEnabled = true;
        harness.SetHeat(60);
        for (var minute = 0; minute < 55; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.Equal(2, harness.Logs.Count(line => line.Contains("outranks the checkpoint", StringComparison.Ordinal)));
        Assert.All(
            harness.Logs.Where(line => line.Contains("outranks the checkpoint", StringComparison.Ordinal)),
            line => Assert.Contains("Follow Schedule", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Clone_on_a_native_checkpoint_warns_once()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "Checkpoint";
        harness.Officers.CheckpointActive = true;
        harness.Officers.BoundKind = OcCheckpointKind.Native;
        harness.SetHeat(60);
        for (var minute = 0; minute < 10; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.Equal(2, harness.Logs.Count(line => line.Contains("sits on a native checkpoint", StringComparison.Ordinal)));
    }

    [Fact]
    public void Status_reports_behaviour_stack_and_checkpoint_detail()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.BehaviourName = "Follow Schedule";
        harness.Officers.CheckpointActive = false;
        harness.Officers.DiagActive = "Follow Schedule";
        harness.Officers.DiagCheckpoint = new OcOfficerCheckpointDetail(true, false, "Western", OcCheckpointKind.Ours);
        harness.Officers.DiagStack.Add(new OcOfficerBehaviourInfo("Follow Schedule", 50, true, true));
        harness.Officers.DiagStack.Add(new OcOfficerBehaviourInfo("Checkpoint", 40, true, false));
        harness.SetHeat(60);
        for (var minute = 0; minute < 5; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        var lines = harness.Service.QaStatus();
        Assert.Contains(lines, line => line.Contains("Follow Schedule", StringComparison.Ordinal) && line.Contains("50", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("enabled True", StringComparison.Ordinal) && line.Contains("active False", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Western", StringComparison.Ordinal) && line.Contains("OC", StringComparison.Ordinal));
    }

    [Fact]
    public void Despawn_releases_officers_before_despawning_the_checkpoint()
    {
        using var harness = CheckpointHarness.Create();
        var order = new List<string>();
        harness.Checkpoints.OrderLog = order;
        harness.Officers.OrderLog = order;
        harness.SetHeat(80);
        for (var minute = 0; minute < 10; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }
        Assert.Equal(3, harness.Service.Snapshot().Managed.Count);
        order.Clear();

        harness.SetHeat(10);
        for (var minute = 10; minute < 13; minute++)
        {
            harness.Minutes = Day5SixAm + minute;
            harness.Tick();
        }

        Assert.Empty(harness.Service.Snapshot().Managed);
        Assert.Equal(6, order.Count(entry => entry == "remove"));
        Assert.Equal(3, order.Count(entry => entry == "despawn"));
        Assert.All(order.Take(2), entry => Assert.Equal("remove", entry));
    }

    [Fact]
    public void Asserted_native_listing_a_clone_warns_once_from_status()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.NativeLeaked = true;
        harness.SetHeat(60);
        harness.Minutes = Day5SixAm;
        harness.Tick();
        Assert.Equal("Docks", harness.Service.Snapshot().AssertedNative);

        var first = harness.Service.QaStatus();
        var second = harness.Service.QaStatus();

        Assert.Equal("Docks", harness.Officers.LeakLabel);
        Assert.Equal(1, harness.Logs.Count(line => line.Contains("lists an OC clone in its AssignedNPCs", StringComparison.Ordinal)));
        Assert.Contains(first, line => line.Contains("Docks", StringComparison.Ordinal) && line.Contains("lists an OC clone", StringComparison.Ordinal));
        Assert.Contains(second, line => line.Contains("Docks", StringComparison.Ordinal) && line.Contains("lists an OC clone", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_stays_quiet_when_the_asserted_native_lists_no_clone()
    {
        using var harness = CheckpointHarness.Create();
        harness.SetHeat(60);
        harness.Minutes = Day5SixAm;
        harness.Tick();
        Assert.Equal("Docks", harness.Service.Snapshot().AssertedNative);

        var lines = harness.Service.QaStatus();

        Assert.Equal("Docks", harness.Officers.LeakLabel);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("lists an OC clone", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("lists an OC clone", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_skips_the_leak_check_without_an_asserted_native()
    {
        using var harness = CheckpointHarness.Create();
        harness.Officers.NativeLeaked = true;
        harness.SetHeat(10);
        harness.Minutes = Day5SixAm;
        harness.Tick();
        Assert.Null(harness.Service.Snapshot().AssertedNative);

        harness.Service.QaStatus();

        Assert.Null(harness.Officers.LeakLabel);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("lists an OC clone", StringComparison.Ordinal));
    }

}
