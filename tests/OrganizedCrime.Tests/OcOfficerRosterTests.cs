using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcOfficerRosterTests
{
    private static OcOfficerPosition Position(float x = 1f, float y = 2f, float z = 3f) => new(x, y, z);

    [Fact]
    public void Position_distance_matches_3d_distance()
    {
        var a = new OcOfficerPosition(0f, 0f, 0f);
        var b = new OcOfficerPosition(3f, 4f, 0f);

        Assert.Equal(5d, b.DistanceTo(a), precision: 9);
    }

    [Fact]
    public void Position_reports_nonfinite_components()
    {
        Assert.True(Position().IsFinite);
        Assert.False(new OcOfficerPosition(float.NaN, 0f, 0f).IsFinite);
        Assert.False(new OcOfficerPosition(0f, float.PositiveInfinity, 0f, 0f).IsFinite);
    }

    [Fact]
    public void Roster_rejects_a_blank_session()
    {
        Assert.Throws<ArgumentException>(() => new OcOfficerRoster("  "));
    }

    [Fact]
    public void Request_accepts_with_sequential_ids()
    {
        var roster = new OcOfficerRoster("test");

        var status = roster.TryRequest("checkpoint:outside-barn:0", Position(), updateIndex: 0, out var handle);

        Assert.Equal(OcOfficerRequestStatus.Accepted, status);
        Assert.Equal(0, handle);
        Assert.Equal(1, roster.Count);
        var snapshot = Assert.Single(roster.Snapshot());
        Assert.Equal(0, snapshot.Handle);
        Assert.Equal("checkpoint:outside-barn:0", snapshot.OwnerKey);
        Assert.Equal(OcOfficerSlotState.Requested, snapshot.State);
        Assert.Equal("oc_officer_test_1", snapshot.CloneId);
        Assert.Equal(0, snapshot.Attempts);
        Assert.False(snapshot.HasLiveClone);
    }

    [Fact]
    public void Request_over_the_cap_is_refused()
    {
        var roster = new OcOfficerRoster("test");
        for (var i = 0; i < OcOfficerRoster.MaxOfficers; i++)
            Assert.Equal(OcOfficerRequestStatus.Accepted, roster.TryRequest($"owner:{i}", Position(), 0, out _));

        var status = roster.TryRequest("owner:extra", Position(), 0, out var handle);

        Assert.Equal(OcOfficerRequestStatus.RefusedCap, status);
        Assert.Equal(-1, handle);
        Assert.Equal(OcOfficerRoster.MaxOfficers, roster.Count);
    }

    [Fact]
    public void Request_rejects_a_blank_owner_and_a_nonfinite_position()
    {
        var roster = new OcOfficerRoster("test");

        Assert.Equal(OcOfficerRequestStatus.RefusedInvalid, roster.TryRequest(" ", Position(), 0, out var blankHandle));
        Assert.Equal(OcOfficerRequestStatus.RefusedInvalid, roster.TryRequest(null, Position(), 0, out _));
        Assert.Equal(OcOfficerRequestStatus.RefusedInvalid, roster.TryRequest("owner", new OcOfficerPosition(float.NaN, 0f, 0f), 0, out _));

        Assert.Equal(-1, blankHandle);
        Assert.Equal(0, roster.Count);
    }

    [Fact]
    public void Release_frees_the_slot_and_returns_its_live_clone()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);
        roster.MarkSpawning(handle, 0);
        var live = new OcOfficerCloneRef(new object(), 12345L, 7);
        Assert.True(roster.MarkActive(handle, live));

        var released = roster.Release(handle, out var returned);

        Assert.True(released);
        Assert.Equal(live, returned);
        Assert.Equal(0, roster.Count);
        Assert.Empty(roster.Snapshot());
    }

    [Fact]
    public void Release_of_an_unknown_handle_fails()
    {
        var roster = new OcOfficerRoster("test");

        Assert.False(roster.Release(3, out var live));
        Assert.Null(live);
    }

    [Fact]
    public void Released_handle_is_reused_with_a_fresh_id()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("first", Position(), 0, out var first);
        roster.Release(first, out _);

        var status = roster.TryRequest("second", Position(), 0, out var second);

        Assert.Equal(OcOfficerRequestStatus.Accepted, status);
        Assert.Equal(first, second);
        Assert.Equal("oc_officer_test_2", Assert.Single(roster.Snapshot()).CloneId);
    }

    [Fact]
    public void Attempt_is_due_only_after_the_backoff()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 100, out var handle);

        Assert.True(roster.IsAttemptDue(handle, 100));
        Assert.False(roster.IsAttemptDue(handle + 1, 100));
        roster.MarkSpawning(handle, 100);
        roster.MarkAttemptFailed(handle, 100);

        Assert.False(roster.IsAttemptDue(handle, 100 + OcOfficerRoster.AttemptBackoffUpdates - 1));
        Assert.True(roster.IsAttemptDue(handle, 100 + OcOfficerRoster.AttemptBackoffUpdates));
    }

    [Fact]
    public void Attempt_failures_retry_then_fail()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);

        for (var attempt = 1; attempt < OcOfficerRoster.MaxAttempts; attempt++)
        {
            roster.MarkSpawning(handle, attempt);
            Assert.True(roster.MarkAttemptFailed(handle, attempt));
            var snapshot = Assert.Single(roster.Snapshot());
            Assert.Equal(OcOfficerSlotState.Requested, snapshot.State);
            Assert.Equal(attempt, snapshot.Attempts);
        }

        roster.MarkSpawning(handle, OcOfficerRoster.MaxAttempts);
        Assert.True(roster.MarkAttemptFailed(handle, OcOfficerRoster.MaxAttempts));
        var failed = Assert.Single(roster.Snapshot());
        Assert.Equal(OcOfficerSlotState.Failed, failed.State);
        Assert.False(roster.IsAttemptDue(handle, long.MaxValue));
    }

    [Fact]
    public void State_transitions_reject_the_wrong_state()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);
        var live = new OcOfficerCloneRef(new object(), 1L, 1);

        Assert.False(roster.MarkActive(handle, live));
        Assert.False(roster.MarkAttemptFailed(handle, 0));
        Assert.False(roster.MarkKnockedOut(handle, 10d));
        Assert.True(roster.MarkSpawning(handle, 0));
        Assert.False(roster.MarkSpawning(handle, 0));
        Assert.False(roster.MarkKnockedOut(handle, 10d));
        Assert.True(roster.MarkActive(handle, live));
        Assert.Equal(OcOfficerSlotState.Active, Assert.Single(roster.Snapshot()).State);
    }

    [Fact]
    public void Knockout_records_minutes_and_respawn_requeues_with_a_new_id()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);
        roster.MarkSpawning(handle, 0);
        var live = new OcOfficerCloneRef(new object(), 9L, 9);
        roster.MarkActive(handle, live);

        Assert.True(roster.MarkKnockedOut(handle, 500d));
        var knocked = Assert.Single(roster.Snapshot());
        Assert.Equal(OcOfficerSlotState.KnockedOut, knocked.State);
        Assert.Equal(500d, knocked.KnockedOutGameMinutes);
        Assert.True(knocked.HasLiveClone);

        Assert.True(roster.MarkRespawnRequested(handle, roster.MintCloneId(), 60, out var removed));
        Assert.Equal(live, removed);
        var requeued = Assert.Single(roster.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, requeued.State);
        Assert.Equal(0, requeued.Attempts);
        Assert.Null(requeued.KnockedOutGameMinutes);
        Assert.False(requeued.HasLiveClone);
        Assert.Equal("oc_officer_test_2", requeued.CloneId);
        Assert.True(roster.IsAttemptDue(handle, 60));
    }

    [Fact]
    public void Suspend_for_save_returns_live_clones_and_requeues_slots()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("active", Position(), 0, out var active);
        roster.MarkSpawning(active, 0);
        var live = new OcOfficerCloneRef(new object(), 3L, 3);
        roster.MarkActive(active, live);
        roster.TryRequest("knocked", Position(), 0, out var knocked);
        roster.MarkSpawning(knocked, 0);
        roster.MarkActive(knocked, new OcOfficerCloneRef(new object(), 4L, 4));
        roster.MarkKnockedOut(knocked, 100d);
        roster.TryRequest("failed", Position(), 0, out var failed);
        for (var i = 0; i < OcOfficerRoster.MaxAttempts; i++)
        {
            roster.MarkSpawning(failed, i);
            roster.MarkAttemptFailed(failed, i);
        }

        var suspended = roster.SuspendForSave();

        Assert.Equal(2, suspended.Count);
        Assert.Contains(live, suspended);
        Assert.Equal(3, roster.Count);
        foreach (var snapshot in roster.Snapshot())
        {
            if (snapshot.Handle == failed)
            {
                Assert.Equal(OcOfficerSlotState.Failed, snapshot.State);
                Assert.Equal(OcOfficerRoster.MaxAttempts, snapshot.Attempts);
            }
            else
            {
                Assert.Equal(OcOfficerSlotState.Requested, snapshot.State);
                Assert.Equal(0, snapshot.Attempts);
                Assert.False(snapshot.HasLiveClone);
                Assert.Null(snapshot.KnockedOutGameMinutes);
            }
        }
        // IDs survive the save boundary so a stale registration reads as the same ID twice.
        var ids = roster.Snapshot().Select(item => item.CloneId).ToArray();
        Assert.Equal(new[] { "oc_officer_test_1", "oc_officer_test_2", "oc_officer_test_3" }, ids);
    }

    [Fact]
    public void Clear_frees_everything_and_returns_live_clones()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);
        roster.MarkSpawning(handle, 0);
        var live = new OcOfficerCloneRef(new object(), 5L, 5);
        roster.MarkActive(handle, live);

        var cleared = roster.Clear();

        Assert.Equal(new[] { live }, cleared);
        Assert.Equal(0, roster.Count);
    }

    [Fact]
    public void Live_clone_read_follows_the_slot()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);

        Assert.False(roster.TryGetLiveClone(handle, out _));
        Assert.False(roster.TryGetLiveClone(handle + 1, out _));

        roster.MarkSpawning(handle, 0);
        var live = new OcOfficerCloneRef(new object(), 11L, 11);
        roster.MarkActive(handle, live);

        Assert.True(roster.TryGetLiveClone(handle, out var read));
        Assert.Equal(live, read);
    }

    [Fact]
    public void Minted_ids_never_repeat()
    {
        var roster = new OcOfficerRoster("session");

        Assert.Equal("oc_officer_session_1", roster.MintCloneId());
        Assert.Equal("oc_officer_session_2", roster.MintCloneId());
    }
    [Fact]
    public void Failed_slot_retries_after_30_game_minutes()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);
        for (var attempt = 1; attempt <= OcOfficerRoster.MaxAttempts; attempt++)
        {
            roster.MarkSpawning(handle, attempt);
            roster.MarkAttemptFailed(handle, attempt, 100d);
        }
        Assert.Equal(OcOfficerSlotState.Failed, Assert.Single(roster.Snapshot()).State);

        Assert.False(roster.IsRetryDue(handle, 100d + OcOfficerRoster.FailedRetryMinutes - 1d));
        Assert.True(roster.IsRetryDue(handle, 100d + OcOfficerRoster.FailedRetryMinutes));

        var before = Assert.Single(roster.Snapshot()).CloneId;
        Assert.True(roster.MarkRetryFromFailed(handle, "retry-id", 999));
        var retried = Assert.Single(roster.Snapshot());
        Assert.Equal(OcOfficerSlotState.Requested, retried.State);
        Assert.Equal(0, retried.Attempts);
        Assert.Equal("retry-id", retried.CloneId);
        Assert.NotEqual(before, retried.CloneId);
        Assert.True(roster.IsAttemptDue(handle, 999));
    }

    [Fact]
    public void Failed_without_a_clock_never_self_heals()
    {
        var roster = new OcOfficerRoster("test");
        roster.TryRequest("owner", Position(), 0, out var handle);
        for (var attempt = 1; attempt <= OcOfficerRoster.MaxAttempts; attempt++)
        {
            roster.MarkSpawning(handle, attempt);
            roster.MarkAttemptFailed(handle, attempt);
        }
        Assert.False(roster.IsRetryDue(handle, double.MaxValue));
    }

}
