using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ArthurFieldContactHarnessTests
{
    private static Release1FieldContactSnapshot PresentSnapshot() => new(
        true, true, true, 4.5f, true,
        100f, 100f, true, false, false,
        90f, 100f, false, false, false, false,
        Release1FieldContactPursuitLevel.None, false, false, false,
        2, 1,
        0.5f, 20f, 8f, string.Empty);

    private static Release1FieldContactSnapshot AbsentSnapshot() => Release1FieldContactSnapshot.AbsentContact(
        90f, 100f, false, false, false, false,
        Release1FieldContactPursuitLevel.None, false, false, false, 2, 1);

    [Fact]
    public void Every_entry_point_reports_faulted_when_the_world_is_null()
    {
        var parked = false;
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryPark(null!).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryUnpark(null!).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryDespawn(null!).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryProvoke(null!).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryReport(null!, false).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryToggle(null!, ref parked).Status);

        Assert.Single(Release1ArthurFieldContactHarness.TryPark(null!).Lines);
        Assert.Single(Release1ArthurFieldContactHarness.TryUnpark(null!).Lines);
        Assert.Single(Release1ArthurFieldContactHarness.TryDespawn(null!).Lines);
        Assert.Single(Release1ArthurFieldContactHarness.TryProvoke(null!).Lines);
        Assert.Single(Release1ArthurFieldContactHarness.TryReport(null!, false).Lines);
        Assert.Single(Release1ArthurFieldContactHarness.TryToggle(null!, ref parked).Lines);
    }

    [Fact]
    public void Report_prints_every_snapshot_field_and_the_parked_flag_when_the_contact_is_present()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()) };
        var result = Release1ArthurFieldContactHarness.TryReport(world, false);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        var joined = string.Join("\n", result.Lines);
        Assert.Contains("parked False", joined);
        Assert.Contains("present True physical True visible True", joined);
        Assert.Contains("distance to player 4.5 moving True", joined);
        Assert.Contains("contact health 100 of 100", joined);
        Assert.Contains("player health 90 of 100", joined);
        Assert.Contains("pursuit level None wanted False", joined);
        Assert.Contains("active officers 2 dispatch officers 1", joined);
        Assert.Contains("aggressiveness 0.5 give up range 20 give up time 8", joined);
    }

    [Fact]
    public void Report_prints_the_parked_flag_as_given_even_when_true()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()) };
        var result = Release1ArthurFieldContactHarness.TryReport(world, true);
        Assert.Contains(result.Lines, line => line == "parked True");
    }

    [Fact]
    public void Report_maps_a_ready_read_with_an_absent_contact_to_unavailable_and_still_prints_the_player_and_law_fields()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, AbsentSnapshot()) };
        var result = Release1ArthurFieldContactHarness.TryReport(world, false);

        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        var joined = string.Join("\n", result.Lines);
        Assert.Contains("present False", joined);
        Assert.Contains("player health 90 of 100", joined);
        Assert.Contains("active officers 2", joined);
    }

    [Fact]
    public void Report_maps_a_not_authoritative_read_to_rejected()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.NotAuthoritative, Release1FieldContactSnapshot.Unavailable()) };
        Assert.Equal(Release1StagingHarnessStatus.Rejected, Release1ArthurFieldContactHarness.TryReport(world, false).Status);
    }

    [Fact]
    public void Report_maps_an_unavailable_read_to_unavailable()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Unavailable, Release1FieldContactSnapshot.Unavailable()) };
        Assert.Equal(Release1StagingHarnessStatus.Unavailable, Release1ArthurFieldContactHarness.TryReport(world, false).Status);
    }

    [Fact]
    public void Report_maps_a_faulted_read_to_faulted()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Faulted, Release1FieldContactSnapshot.Unavailable()) };
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryReport(world, false).Status);
    }

    [Fact]
    public void Despawn_reports_succeeded_and_then_a_report_block_showing_present_false()
    {
        var world = new FakeWorld
        {
            DespawnResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "despawned fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, AbsentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryDespawn(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Equal($"despawn oc_release1_arthur_spike status Succeeded", result.Lines[0]);
        Assert.Contains(result.Lines, line => line.StartsWith("present False", StringComparison.Ordinal));
    }

    [Fact]
    public void Despawn_reports_faulted_and_names_the_residue_when_a_succeeded_despawn_leaves_the_contact_resolving()
    {
        var world = new FakeWorld
        {
            DespawnResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "despawned fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryDespawn(world);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains(result.Lines, line => line.Contains("the contact still resolves", StringComparison.Ordinal));
    }

    [Fact]
    public void Despawn_reports_ambiguous_not_faulted_when_the_world_itself_reports_ambiguous()
    {
        // The world's Ambiguous status is an expected, non exceptional outcome (the owner protocol's
        // own PASS bar names it), not a fault; it must print as Ambiguous, not fall through to the
        // Faulted default.
        var world = new FakeWorld
        {
            DespawnResult = (Release1SmallCourtesyWorldMutationStatus.Ambiguous, "despawn threw"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryDespawn(world);
        Assert.Equal(Release1StagingHarnessStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void Park_reports_the_world_reason_line_and_then_a_report_block()
    {
        var world = new FakeWorld
        {
            ParkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "parked fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryPark(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Equal("park oc_release1_arthur_spike status Succeeded", result.Lines[0]);
        Assert.Equal("reason: parked fine", result.Lines[1]);
        Assert.Contains(result.Lines, line => line.StartsWith("present True", StringComparison.Ordinal));
    }

    [Fact]
    public void Park_maps_an_ambiguous_park_to_ambiguous_not_faulted()
    {
        var world = new FakeWorld
        {
            ParkResult = (Release1SmallCourtesyWorldMutationStatus.Ambiguous, "park threw partway through"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryPark(world);
        Assert.Equal(Release1StagingHarnessStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void Unpark_passes_the_frozen_contact_id_and_ahead_metres()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()) };
        Release1ArthurFieldContactHarness.TryUnpark(world);

        Assert.Equal(("oc_release1_arthur_spike", 3f), world.LastUnparkCall);
        Assert.Equal("oc_release1_arthur_spike", Release1ArthurFieldContactHarness.ContactId);
        Assert.Equal(3f, Release1ArthurFieldContactHarness.AheadMetres);
    }

    [Fact]
    public void Unpark_reports_the_world_reason_line_and_then_a_report_block()
    {
        var world = new FakeWorld
        {
            UnparkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "unparked fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryUnpark(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Equal("unpark oc_release1_arthur_spike ahead metres 3 status Succeeded", result.Lines[0]);
        Assert.Equal("reason: unparked fine", result.Lines[1]);
        Assert.Contains(result.Lines, line => line.StartsWith("present True", StringComparison.Ordinal));
    }

    [Fact]
    public void Unpark_maps_an_ambiguous_unpark_to_ambiguous_not_faulted()
    {
        // A CanGetTo refusal on the recheck is an expected, non exceptional outcome, not a fault.
        var world = new FakeWorld
        {
            UnparkResult = (Release1SmallCourtesyWorldMutationStatus.Ambiguous,
                "positioned at (0, 0, 0) but CanGetTo((0, 0, 0), 2) refused, so no approach was issued."),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryUnpark(world);
        Assert.Equal(Release1StagingHarnessStatus.Ambiguous, result.Status);
        Assert.Contains(result.Lines, line => line.StartsWith("present True", StringComparison.Ordinal));
    }

    [Fact]
    public void Toggle_parks_a_present_contact_when_not_yet_parked()
    {
        var world = new FakeWorld { ParkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "parked fine") };
        world.EnqueueRead(Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot());
        world.EnqueueRead(Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot());
        var parked = false;

        var result = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.True(parked);
        Assert.Equal(1, world.ParkCallCount);
        Assert.Equal("park oc_release1_arthur_spike status Succeeded", result.Lines[0]);
    }

    [Fact]
    public void Toggle_reports_no_contact_and_does_nothing_else_when_absent()
    {
        // On-demand construction is abandoned: an absent contact means none has ever been found this
        // session, and F4 must log one line and do nothing else, never spawn one of its own.
        var world = new FakeWorld();
        world.EnqueueRead(Release1SmallCourtesyWorldReadStatus.Ready, AbsentSnapshot());
        var parked = false;

        var result = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);

        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        Assert.False(parked);
        Assert.Equal(0, world.ParkCallCount);
        Assert.Equal(0, world.UnparkCallCount);
        Assert.Single(result.Lines);
        Assert.Contains("built by the game's own load-time sweep at the next load", result.Lines[0]);
    }

    [Fact]
    public void Toggle_unparks_without_a_presence_read_when_already_parked()
    {
        var world = new FakeWorld
        {
            UnparkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "unparked fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var parked = true;

        var result = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.False(parked);
        Assert.Equal(1, world.UnparkCallCount);
        Assert.Equal(0, world.ParkCallCount);
        Assert.Equal("unpark oc_release1_arthur_spike ahead metres 3 status Succeeded", result.Lines[0]);
    }

    [Fact]
    public void Toggle_unparks_then_parks_again_on_the_next_press()
    {
        // The exact F4 sequence the owner protocol names: unparked once (starting parked), then parked
        // again on the very next press.
        var world = new FakeWorld
        {
            UnparkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "unparked fine"),
            ParkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "parked fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var parked = true;

        var first = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, first.Status);
        Assert.False(parked);
        Assert.Equal(1, world.UnparkCallCount);
        Assert.Equal(0, world.ParkCallCount);

        var second = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, second.Status);
        Assert.True(parked);
        Assert.Equal(1, world.ParkCallCount);
        Assert.Equal("park oc_release1_arthur_spike status Succeeded", second.Lines[0]);
    }

    [Fact]
    public void Toggle_parks_then_unparks_across_two_presses()
    {
        var world = new FakeWorld
        {
            ParkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "parked fine"),
            UnparkResult = (Release1SmallCourtesyWorldMutationStatus.Succeeded, "unparked fine"),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var parked = false;

        var first = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, first.Status);
        Assert.True(parked);
        Assert.Equal(1, world.ParkCallCount);

        var second = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, second.Status);
        Assert.False(parked);
        Assert.Equal(1, world.UnparkCallCount);
    }

    [Fact]
    public void TryToggle_reports_the_read_status_instead_of_guessing_when_the_presence_read_is_not_ready()
    {
        var world = new FakeWorld { ReadResult = (Release1SmallCourtesyWorldReadStatus.Faulted, Release1FieldContactSnapshot.Unavailable()) };
        var parked = false;

        var result = Release1ArthurFieldContactHarness.TryToggle(world, ref parked);

        Assert.False(parked);
        Assert.Equal(0, world.ParkCallCount);
        Assert.Contains(result.Lines, line => line.Contains("status Faulted", StringComparison.Ordinal));
    }

    [Fact]
    public void Provoke_calls_provoke_then_report_in_one_press()
    {
        // The weapon/aggression invariant this test's name used to claim is asserted separately, by
        // the reachability suite's source scan over S1ApiRelease1FieldContactRuntime.cs
        // (The_field_contact_runtime_never_saves_never_damages_and_never_writes_aggression_tuning),
        // not by anything StrictProvokeWorld can observe from the harness side of the boundary.
        var world = new StrictProvokeWorld();
        var result = Release1ArthurFieldContactHarness.TryProvoke(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.True(world.ProvokeCalled);
        Assert.True(world.ReadCalled);
    }

    [Fact]
    public void Provoke_maps_a_rejected_provoke_to_rejected_and_still_prints_the_report_block()
    {
        var world = new FakeWorld
        {
            ProvokeResult = (Release1SmallCourtesyWorldMutationStatus.Rejected, "the field contact provoke was refused."),
            ReadResult = (Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot())
        };
        var result = Release1ArthurFieldContactHarness.TryProvoke(world);

        Assert.Equal(Release1StagingHarnessStatus.Rejected, result.Status);
        Assert.Contains(result.Lines, line => line.StartsWith("present True", StringComparison.Ordinal));
    }

    [Fact]
    public void No_entry_point_throws_when_the_world_throws_from_every_member()
    {
        var world = new ThrowingWorld();
        var parked = false;

        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryPark(world).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryUnpark(world).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryDespawn(world).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryProvoke(world).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryReport(world, false).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, Release1ArthurFieldContactHarness.TryToggle(world, ref parked).Status);
    }

    [Fact]
    public void The_harness_source_holds_no_unity_and_no_s1api_reference()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurFieldContactHarness.cs"));
        foreach (var forbidden in new[] { "UnityEngine", "S1API", "Il2Cpp", "Vector3", "MelonLoader" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "OrganizedCrime"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        public (Release1SmallCourtesyWorldReadStatus Status, Release1FieldContactSnapshot Snapshot) ReadResult { get; set; } =
            (Release1SmallCourtesyWorldReadStatus.Ready, Release1FieldContactSnapshot.Unavailable());
        public (Release1SmallCourtesyWorldMutationStatus Status, string Reason) ParkResult { get; set; } =
            (Release1SmallCourtesyWorldMutationStatus.Succeeded, "fake park");
        public (Release1SmallCourtesyWorldMutationStatus Status, string Reason) UnparkResult { get; set; } =
            (Release1SmallCourtesyWorldMutationStatus.Succeeded, "fake unpark");
        public (Release1SmallCourtesyWorldMutationStatus Status, string Reason) DespawnResult { get; set; } =
            (Release1SmallCourtesyWorldMutationStatus.Succeeded, "fake despawn");
        public (Release1SmallCourtesyWorldMutationStatus Status, string Reason) ProvokeResult { get; set; } =
            (Release1SmallCourtesyWorldMutationStatus.Succeeded, "fake provoke");
        public (string ContactId, float AheadMetres)? LastUnparkCall { get; private set; }
        public int ParkCallCount { get; private set; }
        public int UnparkCallCount { get; private set; }
        public int DespawnCallCount { get; private set; }

        // A decision read that unparks by handle (parked true) consumes no read at all, a decision read
        // that decides to park an absent-turned-present contact consumes one (the decision itself; the
        // park mutation's own confirm read is answered by ReadResult once the queue runs dry). A queue
        // lets a test hand back a different snapshot for each of those reads; ReadResult alone (below)
        // still answers once the queue runs dry, so a single-read test needs no queue at all.
        private readonly Queue<(Release1SmallCourtesyWorldReadStatus Status, Release1FieldContactSnapshot Snapshot)> _readQueue = new();
        public void EnqueueRead(Release1SmallCourtesyWorldReadStatus status, Release1FieldContactSnapshot snapshot) =>
            _readQueue.Enqueue((status, snapshot));

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        {
            if (_readQueue.Count > 0)
            {
                var next = _readQueue.Dequeue();
                snapshot = next.Snapshot;
                return next.Status;
            }
            snapshot = ReadResult.Snapshot;
            return ReadResult.Status;
        }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { ParkCallCount++; reason = ParkResult.Reason; return ParkResult.Status; }

        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        { UnparkCallCount++; LastUnparkCall = (contactId, aheadMetres); reason = UnparkResult.Reason; return UnparkResult.Status; }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { DespawnCallCount++; reason = DespawnResult.Reason; return DespawnResult.Status; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { reason = ProvokeResult.Reason; return ProvokeResult.Status; }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new InvalidOperationException();
    }

    private sealed class StrictProvokeWorld : IRelease1SmallCourtesyWorld
    {
        public bool ProvokeCalled { get; private set; }
        public bool ReadCalled { get; private set; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { ProvokeCalled = true; reason = "provoked."; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        { ReadCalled = true; snapshot = PresentSnapshot(); return Release1SmallCourtesyWorldReadStatus.Ready; }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new InvalidOperationException();
    }

    private sealed class ThrowingWorld : IRelease1SmallCourtesyWorld
    {
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot) => throw new InvalidOperationException("read fault");
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason) => throw new InvalidOperationException("park fault");
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new InvalidOperationException("unpark fault");
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new InvalidOperationException("despawn fault");
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => throw new InvalidOperationException("provoke fault");

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new InvalidOperationException();
    }
}
