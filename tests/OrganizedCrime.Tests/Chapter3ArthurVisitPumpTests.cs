using System.Reflection;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurVisitPumpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Client_pump_keeps_load_reconcile_pending_without_reading_or_mutating_arthur(bool spawned)
    {
        var world = new FakeWorld
        {
            HasAuthority = false,
            Next = new Chapter3ArthurSnapshot(true, spawned, true, false, true, (5f, 0f, 5f), 7f, 3),
            Impact = true
        };
        var pump = new Chapter3ArthurVisitPump(() => 0f);
        pump.BeginAfterLoad();

        Assert.Null(pump.Pump(world));
        Assert.True(ReconcilePending(pump));
        Assert.Equal(0, world.Reads);
        Assert.Empty(world.Mutations);
        Assert.True(world.Impact);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);

        // The same arm is consumed only once authority and the spawn are ready.
        world.HasAuthority = true;
        world.Next = world.Next with { Spawned = true };
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, pump.Pump(world)!.Outcome);
        Assert.False(ReconcilePending(pump));
        Assert.Equal(1, world.Reads);
        Assert.Equal(new[] { "verify", "dedupe", "park" }, world.Mutations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Client_pump_does_not_advance_a_visit_or_consume_an_impact(bool leaving)
    {
        var world = new FakeWorld { HasAuthority = false, Impact = true };
        var pump = new Chapter3ArthurVisitPump(() => 100f);
        Assert.True(pump.BeginCome());
        if (leaving) Assert.True(pump.BeginLeave());
        var state = pump.State;

        Assert.Null(pump.Pump(world));
        Assert.Equal(state, pump.State);
        Assert.Equal(0, world.Reads);
        Assert.Empty(world.Mutations);
        Assert.True(world.Impact);
    }

    [Fact]
    public void Reload_always_resolves_to_parked()
    {
        var pump = new Chapter3ArthurVisitPump(() => 0f);
        Assert.True(pump.BeginCome());
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);

        pump.BeginAfterLoad();

        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Full_visit_cycles_parked_coming_arrived_leaving_parked()
    {
        var now = 100f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));

        Assert.True(pump.BeginCome());
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);

        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 25f, Position = (25f, 0f, 0f) };
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);

        // The walk grace ignores the same-pass "not moving" read; the
        // arrival only counts past the grace, by distance.
        world.Next = world.Next with { Moving = false, DistanceToPlayer = 25f };
        Assert.Null(pump.Pump(world));
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        world.Next = world.Next with { Moving = false, DistanceToPlayer = 2f, Position = (2f, 0f, 0f) };
        var arrived = pump.Pump(world);
        Assert.NotNull(arrived);
        Assert.Equal(Chapter3ArthurVisitOutcome.Arrived, arrived!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.Arrived, pump.State);
        Assert.Contains(world.Mutations, mutation => mutation == "face");

        Assert.True(pump.BeginLeave());
        Assert.Equal(Chapter3ArthurVisitState.Leaving, pump.State);

        world.Next = world.Next with { Moving = true, DistanceToPlayer = 10f };
        Assert.Null(pump.Pump(world));
        // Past the grace, 30 m out parks him: the leave visibly walks
        // away before the park.
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        world.Next = world.Next with { Moving = false, DistanceToPlayer = 30f };
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Contains(world.Mutations, mutation => mutation == "park");
    }

    [Fact]
    public void BeginCome_refuses_unless_parked_and_BeginLeave_refuses_while_parked()
    {
        var pump = new Chapter3ArthurVisitPump(() => 0f);

        Assert.False(pump.BeginLeave());
        Assert.True(pump.BeginCome());
        Assert.False(pump.BeginCome());
        Assert.True(pump.BeginLeave());
        Assert.False(pump.BeginLeave());
    }

    [Fact]
    public void Reconcile_moves_out_of_sight_while_unspawned_and_parks_only_after_spawn()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, false, true, false, true, (5f, 0f, 5f), 7f, 1);

        var pending = pump.Pump(world);
        Assert.NotNull(pending);
        Assert.Equal(Chapter3ArthurVisitOutcome.SpawnPending, pending!.Outcome);
        Assert.Equal(new[] { "verify", "move" }, world.Mutations);

        // Still unspawned: the move repeats (idempotent), the park never runs.
        now += 10f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(new[] { "verify", "move", "move" }, world.Mutations);

        world.Next = world.Next! with { Spawned = true };
        now += 10f;
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        Assert.Equal(new[] { "verify", "move", "move", "park" }, world.Mutations);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        // Settled: further pumps stay quiet.
        Assert.Null(pump.Pump(world));
    }

    [Fact]
    public void Reconcile_parks_on_the_first_read_when_already_spawned()
    {
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => 0f);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (5f, 0f, 5f), 7f, 1);

        var step = pump.Pump(world);

        Assert.NotNull(step);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, step!.Outcome);
        Assert.Equal(new[] { "verify", "park" }, world.Mutations);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Null(pump.Pump(world));
    }

    [Fact]
    public void Reconcile_dedupes_before_parking_when_several_share_the_id()
    {
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => 0f);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (5f, 0f, 5f), 7f, 3);

        var step = pump.Pump(world);

        Assert.NotNull(step);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, step!.Outcome);
        Assert.Equal(new[] { "verify", "dedupe", "park" }, world.Mutations);
    }

    [Fact]
    public void Reconcile_reports_a_non_ready_read_once_then_parks_when_spawned()
    {
        var now = 0f;
        var world = new FakeWorld { Status = Chapter3ArthurReadStatus.Pending };
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();

        var first = pump.Pump(world);
        Assert.NotNull(first);
        Assert.Equal(Chapter3ArthurVisitOutcome.ReadFailed, first!.Outcome);

        now += 10f;
        Assert.Null(pump.Pump(world));

        world.Status = Chapter3ArthurReadStatus.Ready;
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (5f, 0f, 5f), 7f, 1);
        now += 10f;
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
    }

    [Fact]
    public void Reconcile_throttles_attempts_and_expires_at_the_bound()
    {
        var now = 0f;
        var world = new FakeWorld { Status = Chapter3ArthurReadStatus.Pending };
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        var readsAfterFirst = world.Reads;
        now += 0.5f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(readsAfterFirst, world.Reads);

        now = Chapter3ArthurVisitPump.MaxSeconds + 1f;
        var expired = pump.Pump(world);
        Assert.NotNull(expired);
        Assert.Equal(Chapter3ArthurVisitOutcome.BoundExpired, expired!.Outcome);
        Assert.Contains("left untouched", expired.Lines[0]);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Reconcile_keeps_waiting_while_absent_and_expires_untouched()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = Chapter3ArthurSnapshot.Absent();

        // Absent is transient (the wrapper constructs after load), so the
        // reconcile reports once and keeps waiting instead of settling.
        var first = pump.Pump(world);
        Assert.NotNull(first);
        Assert.Equal(Chapter3ArthurVisitOutcome.ReadFailed, first!.Outcome);
        Assert.Empty(world.Mutations);

        now += 10f;
        Assert.Null(pump.Pump(world));
        Assert.Empty(world.Mutations);

        now = Chapter3ArthurVisitPump.MaxSeconds + 1f;
        var expired = pump.Pump(world);
        Assert.NotNull(expired);
        Assert.Equal(Chapter3ArthurVisitOutcome.BoundExpired, expired!.Outcome);
        Assert.Empty(world.Mutations);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Reconcile_bound_expiry_with_an_unspawned_contact_parks_nothing()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, false, true, false, true, (5f, 0f, 5f), 7f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.DoesNotContain("park", world.Mutations);

        now = Chapter3ArthurVisitPump.MaxSeconds + 1f;
        var expired = pump.Pump(world);

        Assert.NotNull(expired);
        Assert.Equal(Chapter3ArthurVisitOutcome.BoundExpired, expired!.Outcome);
        Assert.Contains("never spawned", expired.Lines[0]);
        Assert.DoesNotContain("park", world.Mutations);
        Assert.DoesNotContain("dedupe", world.Mutations);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Coming_ignores_unspawned_reads()
    {
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => 0f);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        world.Next = world.Next! with { Spawned = false, Moving = false, DistanceToPlayer = 1f };
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
    }

    [Fact]
    public void Leave_parks_at_the_bound_even_when_still_moving()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        world.Next = world.Next! with { Moving = false, DistanceToPlayer = 2f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginLeave());

        world.Next = world.Next with { Moving = true, DistanceToPlayer = 15f };
        now += Chapter3ArthurVisitPump.LeaveSeconds - 1f;
        Assert.Null(pump.Pump(world));

        now += 2f;
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Contains("park", world.Mutations);
    }

    [Fact]
    public void Idle_parked_pump_reads_nothing()
    {
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => 0f);

        Assert.Null(pump.Pump(world));
        Assert.Equal(0, world.Reads);
        Assert.Null(pump.Pump(null));
    }

    [Fact]
    public void Presence_chapter_inactive_stays_parked()
    {
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => 0f);
        pump.HqPresence = new FakePresence { Chapter = false, Inside = true };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.Null(pump.Pump(world));

        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.DoesNotContain("place", world.Mutations);
    }

    [Fact]
    public void Presence_active_inside_places_at_hq_and_outside_parks()
    {
        var now = 0f;
        var world = new FakeWorld();
        var presence = new FakePresence { Chapter = true, Inside = false };
        var pump = new Chapter3ArthurVisitPump(() => now) { HqPresence = presence };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));

        // Outside: parked, no place.
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.DoesNotContain("place", world.Mutations);

        // Inside: placed at the HQ spot.
        presence.Inside = true;
        now += 1f;
        var placed = pump.Pump(world);
        Assert.NotNull(placed);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, placed!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);
        Assert.Contains("place", world.Mutations);

        // Outside again: parked.
        presence.Inside = false;
        now += 1f;
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Contains("park", world.Mutations);
    }

    [Fact]
    public void Presence_force_on_places_without_chapter_and_force_off_parks_inside()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now)
        {
            HqPresence = new FakePresence { Chapter = false, Inside = false },
            HqForce = Chapter3ArthurHqForce.On
        };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));

        now += 1f;
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);

        pump.HqForce = Chapter3ArthurHqForce.Off;
        now += 1f;
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Presence_entering_hq_mid_visit_ends_the_visit_then_places()
    {
        var now = 0f;
        var world = new FakeWorld();
        var presence = new FakePresence { Chapter = true, Inside = false };
        var pump = new Chapter3ArthurVisitPump(() => now) { HqPresence = presence };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        world.Mutations.Clear();

        presence.Inside = true;
        now += 1f;
        var placed = pump.Pump(world);

        Assert.NotNull(placed);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, placed!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);
        Assert.Equal(new[] { "park", "place" }, world.Mutations);
    }

    [Fact]
    public void Presence_visit_overrides_hq_and_reload_never_resolves_mid_visit()
    {
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => 0f)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);

        // A visit overrides the presence.
        Assert.True(pump.BeginCome());
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
        Assert.False(pump.BeginLeave() && pump.State == Chapter3ArthurVisitState.AtHq);

        // A reload resolves to parked even mid-visit; the presence
        // takes over on a later pass, never mid-visit.
        pump.BeginAfterLoad();
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        var placed = pump.Pump(world);
        Assert.NotNull(placed);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, placed!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);
    }

    [Fact]
    public void Presence_never_places_a_pending_spawn()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, false, true, false, true, (5f, 0f, 5f), 7f, 1);

        // Ten throttled passes, still unspawned: the reconcile moves,
        // the presence never places.
        for (var i = 0; i < 10; i++)
        {
            pump.Pump(world);
            now += 1f;
        }

        Assert.DoesNotContain("place", world.Mutations);
        Assert.DoesNotContain("park", world.Mutations);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Coming_has_no_arrival_before_the_grace_period()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        // Stopped and already within arrival range on the same pass
        // the walk was issued: still no arrival.
        world.Next = world.Next! with { Moving = false, DistanceToPlayer = 2f };
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);

        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        var arrived = pump.Pump(world);
        Assert.NotNull(arrived);
        Assert.Equal(Chapter3ArthurVisitOutcome.Arrived, arrived!.Outcome);
    }

    [Fact]
    public void Come_timeout_stops_and_faces_where_he_is()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 10f };
        now = Chapter3ArthurVisitPump.ComeSeconds - 1f;
        Assert.Null(pump.Pump(world));

        now = Chapter3ArthurVisitPump.ComeSeconds + 1f;
        var arrived = pump.Pump(world);

        Assert.NotNull(arrived);
        Assert.Equal(Chapter3ArthurVisitOutcome.Arrived, arrived!.Outcome);
        Assert.Contains("bound expired", arrived.Lines[0]);
        Assert.Equal(Chapter3ArthurVisitState.Arrived, pump.State);
        Assert.Contains("face", world.Mutations);
    }

    [Fact]
    public void Stalled_walks_reissue_bounded_then_wait_for_the_bound()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        world.Next = world.Next! with { Moving = false, DistanceToPlayer = 10f };

        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        var first = pump.Pump(world);
        Assert.NotNull(first);
        Assert.Equal(Chapter3ArthurVisitOutcome.WalkReissued, first!.Outcome);

        now += 0.1f;
        var second = pump.Pump(world);
        Assert.NotNull(second);
        Assert.Equal(Chapter3ArthurVisitOutcome.WalkReissued, second!.Outcome);

        now += 0.1f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
        Assert.Equal(Chapter3ArthurVisitPump.MaxWalkResumes, world.Mutations.Count(mutation => mutation == "resume"));
    }

    [Fact]
    public void Leave_does_not_park_during_grace_even_when_far()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (2f, 0f, 0f), 2f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginLeave());

        // Already past the park distance on the same pass the walk
        // was issued: the grace still holds the park.
        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 30f };
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Leaving, pump.State);

        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
    }

    [Fact]
    public void Leave_parks_out_of_view_before_the_distance()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (2f, 0f, 0f), 2f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginLeave());

        // Still walking, only 10 m out, but out of view: parks.
        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 10f, InPlayerView = false };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        var parked = pump.Pump(world);

        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        Assert.Contains("out of the player's view", parked.Lines[0]);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
    }

    [Fact]
    public void Leave_keeps_walking_while_visible_and_near()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (2f, 0f, 0f), 2f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginLeave());
        world.Mutations.Clear();

        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 10f, InPlayerView = true };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Leaving, pump.State);
        Assert.DoesNotContain("park", world.Mutations);
    }

    [Fact]
    public void Hq_maintain_holds_through_a_rebuild_gap_then_replaces()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (1002.5f, 0f, 1004.2f), 5f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);

        // The interior goes away: one holding line, still at the HQ.
        world.PlaceStatus = Chapter3ArthurMutationStatus.Unavailable;
        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        var holding = pump.Pump(world);
        Assert.NotNull(holding);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, holding!.Outcome);
        Assert.Contains("holding position", holding.Lines[0]);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);

        // Still gone: quiet.
        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        Assert.Null(pump.Pump(world));

        // The interior is back: re-placed with one line.
        world.PlaceStatus = Chapter3ArthurMutationStatus.Succeeded;
        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        var rebuilt = pump.Pump(world);
        Assert.NotNull(rebuilt);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, rebuilt!.Outcome);
        Assert.Contains("re-placed after the interior rebuild", rebuilt.Lines[0]);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);
    }

    [Fact]
    public void Hq_maintain_faces_the_player_only_within_range()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (1002.5f, 0f, 1004.2f), 5f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.NotNull(pump.Pump(world));
        world.Mutations.Clear();

        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        pump.Pump(world);
        Assert.Contains("facehq", world.Mutations);
        Assert.DoesNotContain("face", world.Mutations);

        world.Mutations.Clear();
        world.Next = world.Next! with { DistanceToPlayer = Chapter3ArthurHqSpot.FaceRangeMetres + 1f };
        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        pump.Pump(world);
        Assert.DoesNotContain("facehq", world.Mutations);
    }

    [Fact]
    public void Attack_logs_once_per_incident_at_the_hq()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (1002.5f, 0f, 1004.2f), 5f, 1, 100f);
        Assert.NotNull(pump.Pump(world));
        Assert.NotNull(pump.Pump(world));

        // One quiet maintain pass establishes the health baseline.
        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        Assert.Null(pump.Pump(world));

        // A punch: the exact sentence, once.
        world.Next = world.Next! with { Health = 90f };
        now += Chapter3ArthurHqSpot.MaintainIntervalSeconds + 0.05f;
        var attacked = pump.Pump(world);
        Assert.NotNull(attacked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Attacked, attacked!.Outcome);
        Assert.Equal("Chapter 3 Arthur was attacked (HQ).", attacked.Lines[0]);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);

        // A second punch in the same incident: quiet.
        world.Next = world.Next with { Health = 80f };
        now += 1f;
        Assert.Null(pump.Pump(world));

        // Healed back to the baseline: the next punch logs again.
        world.Next = world.Next with { Health = 100f };
        now += 1f;
        Assert.Null(pump.Pump(world));
        world.Next = world.Next with { Health = 90f };
        now += 1f;
        var again = pump.Pump(world);
        Assert.NotNull(again);
        Assert.Equal(Chapter3ArthurVisitOutcome.Attacked, again!.Outcome);
    }

    [Fact]
    public void Attack_logs_the_visit_context_mid_visit_and_rearms_when_quiet()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1, 100f);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        // One pass at full health establishes the baseline (the grace
        // holds the walk, but the attack check still runs).
        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 20f, Health = 100f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds / 2f;
        Assert.Null(pump.Pump(world));

        world.Next = world.Next with { Health = 90f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds;
        var attacked = pump.Pump(world);
        Assert.NotNull(attacked);
        Assert.Equal("Chapter 3 Arthur was attacked (visit).", attacked!.Lines[0]);

        // Ten quiet seconds re-base the incident: the next punch logs.
        now += Chapter3ArthurVisitPump.AttackIncidentSeconds + 0.1f;
        Assert.Null(pump.Pump(world));
        world.Next = world.Next with { Health = 80f };
        now += 0.1f;
        var again = pump.Pump(world);
        Assert.NotNull(again);
        Assert.Equal(Chapter3ArthurVisitOutcome.Attacked, again!.Outcome);
    }

    [Fact]
    public void Attack_ignores_an_unreadable_health()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 20f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
    }

    [Fact]
    public void Impact_opens_an_incident_without_a_health_drop()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1, 100f);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        // One pass at full health establishes the baseline.
        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 20f, Health = 100f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds / 2f;
        Assert.Null(pump.Pump(world));

        // A suppressed impact with no health drop: the exact sentences, once.
        world.Impact = true;
        now += Chapter3ArthurVisitPump.WalkGraceSeconds;
        var attacked = pump.Pump(world);
        Assert.NotNull(attacked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Attacked, attacked!.Outcome);
        Assert.Equal(
            new[]
            {
                "Chapter 3 Arthur was attacked (visit).",
                "Chapter 3 Arthur native reaction suppressed (ImpactReceived).",
            },
            attacked.Lines);

        // A second impact in the same incident: quiet.
        world.Impact = true;
        now += 1f;
        Assert.Null(pump.Pump(world));
    }

    [Fact]
    public void Health_drop_without_impact_opens_without_the_suppression_line()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1, 100f);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 20f, Health = 100f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds / 2f;
        Assert.Null(pump.Pump(world));

        // The fallback: damage with no recorded impact still opens, but
        // carries no suppression line.
        world.Next = world.Next with { Health = 90f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds;
        var attacked = pump.Pump(world);
        Assert.NotNull(attacked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Attacked, attacked!.Outcome);
        Assert.Single(attacked.Lines);
        Assert.Equal("Chapter 3 Arthur was attacked (visit).", attacked.Lines[0]);
    }

    [Fact]
    public void Parked_drains_a_stale_impact()
    {
        var now = 0f;
        var world = new FakeWorld();
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 0f), 25f, 1, 100f);
        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());

        // A baseline from the first visit: without the parked drain, a
        // stale impact would open the second visit as an attack.
        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 20f, Health = 100f };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds / 2f;
        Assert.Null(pump.Pump(world));

        // Leave and park out of view.
        Assert.True(pump.BeginLeave());
        world.Next = world.Next with { InPlayerView = false };
        now += Chapter3ArthurVisitPump.WalkGraceSeconds + 0.1f;
        var parked = pump.Pump(world);
        Assert.NotNull(parked);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, parked!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);

        // A hit between visits drains on the parked pass.
        world.Impact = true;
        Assert.Null(pump.Pump(world));
        Assert.False(world.Impact);

        // The next visit starts clean.
        Assert.True(pump.BeginCome());
        world.Next = world.Next! with { Moving = true, DistanceToPlayer = 20f, InPlayerView = true, Health = 100f };
        now += 0.1f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
    }

    [Fact]
    public void BeginCome_runs_from_hq_and_BeginLeave_refuses_there()
    {
        var pump = new Chapter3ArthurVisitPump(() => 0f)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        var world = new FakeWorld();
        pump.BeginAfterLoad();
        world.Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1);
        Assert.NotNull(pump.Pump(world));
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);

        Assert.False(pump.BeginLeave());
        Assert.True(pump.BeginCome());
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
    }

    private sealed class FakePresence : IChapter3HqPresence
    {
        public bool Chapter;
        public bool Inside;
        public bool ChapterActive => Chapter;
        public bool PlayerInsideHq => Inside;
    }

    private static bool ReconcilePending(Chapter3ArthurVisitPump pump) =>
        (bool)typeof(Chapter3ArthurVisitPump)
            .GetField("_reconcilePending", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pump)!;

    private sealed class FakeWorld : IChapter3ArthurWorld
    {
        public bool HasAuthority { get; set; } = true;
        public Chapter3ArthurReadStatus Status = Chapter3ArthurReadStatus.Ready;
        public Chapter3ArthurSnapshot? Next = Chapter3ArthurSnapshot.Absent();
        public int Reads;
        public List<string> Mutations { get; } = new();

        public bool Impact;

        public bool PollArthurImpact()
        {
            var observed = Impact;
            Impact = false;
            return observed;
        }

        public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
        {
            Reads++;
            snapshot = Next ?? Chapter3ArthurSnapshot.Absent();
            return Status;
        }

        public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason)
        {
            Mutations.Add("move");
            reason = "moved out of sight";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryParkArthur(out string reason)
        {
            Mutations.Add("park");
            reason = "parked";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason)
        {
            Mutations.Add("verify");
            reason = "paths verified";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryComeArthur(out string reason)
        {
            Mutations.Add("come");
            reason = "coming";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason)
        {
            Mutations.Add($"say:{text}");
            reason = "said";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason)
        {
            Mutations.Add("leave");
            reason = "leaving";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryFacePlayer(out string reason)
        {
            Mutations.Add("face");
            reason = "faced";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus CaptureStatus = Chapter3ArthurMutationStatus.Succeeded;

        public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)
        {
            Mutations.Add("capture");
            local = (-2.9f, 0f, 3.7f);
            yawDegrees = 180f;
            reason = CaptureStatus == Chapter3ArthurMutationStatus.Succeeded ? "captured" : "the player is not inside the HQ.";
            return CaptureStatus;
        }

        public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
        {
            Mutations.Add("facehq");
            reason = "faced directly";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus PlaceStatus = Chapter3ArthurMutationStatus.Succeeded;

        public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason)
        {
            Mutations.Add("place");
            reason = PlaceStatus == Chapter3ArthurMutationStatus.Succeeded ? "placed at the HQ spot" : "the HQ interior is not created.";
            return PlaceStatus;
        }

        public Chapter3ArthurMutationStatus TryResumeWalk(out string reason)
        {
            Mutations.Add("resume");
            reason = "re-issued";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason)
        {
            Mutations.Add("dedupe");
            reason = "deduped";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason)
        {
            Mutations.Add($"anim:{trigger}");
            reason = $"played {trigger}.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

    }
}
