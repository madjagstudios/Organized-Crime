using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc181HqRetentionTests
{
    [Fact]
    public void Oc181_context_read_exception_does_not_reset_warning_gate_after_same_load_recovers()
    {
        using var h = new Harness();
        Assert.True(h.Service.OnLoadComplete());
        for (var i = 0; i < 2; i++)
        {
            h.Context.ThrowRead = true;
            Assert.True(h.Service.OnSaveStart());
            Assert.True(h.Service.OnSaveComplete());
            Assert.True(h.Interior.Enabled);
        }
        Assert.Single(h.Logs, x => x.Contains("failed closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Oc181_missing_native_player_declines_before_any_transform_access()
    {
        var transport = new SyndicateHqNativePlayerTransport();
        Assert.False(transport.TryReadPosition(out var position));
        Assert.Equal(default, position);
    }

    [Fact]
    public void Oc181_non_native_closet_declines_before_any_native_member_access()
    {
        var boundary = new SyndicateHqNativeStorageBoundary();
        Assert.False(boundary.TryValidate(new object(), SyndicateHqStorageContract.Definitions[0], out _));
        Assert.Null(Record.Exception(() => boundary.SetInteractionEnabled(new object(), true)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oc181_decline_and_exception_retry_deferred_rebuild_and_keep_save_window_inert(bool throws)
    {
        using var h = new Harness();
        Assert.True(h.Service.OnLoadComplete());
        h.Interior.DeferDestroy = true;
        if (throws) h.Interior.ThrowAt = "disable";
        else h.Interior.Ready = false;
        Assert.True(h.Service.OnSaveStart());
        h.Service.Update();
        Assert.Equal(1, h.Interior.Ensures);
        Assert.False(h.Service.OnSaveComplete());
        h.Interior.PendingDestroy = false;
        h.Service.Update();
        Assert.True(h.Interior.Alive);
        Assert.True(h.Interior.Enabled);
        Assert.Equal(3, h.Interior.Ensures);
    }

    [Fact]
    public void Oc181_epoch_change_during_save_rebuilds_instead_of_resuming_old_retention()
    {
        using var h = new Harness();
        Assert.True(h.Service.OnLoadComplete());
        Assert.True(h.Service.OnSaveStart());
        h.Context.Snapshot = h.Context.Snapshot with { LoadEpoch = 2 };
        Assert.True(h.Service.OnSaveComplete());
        Assert.Equal(1, h.Interior.Destroys);
        Assert.Equal(2, h.Interior.Ensures);
        Assert.True(h.Interior.Enabled);
    }

    [Theory]
    [InlineData("door")]
    [InlineData("dead")]
    [InlineData("epoch")]
    [InlineData("session")]
    [InlineData("player")]
    [InlineData("folder")]
    public void Oc181_not_ready_or_stale_declines_without_warning_and_recovers(string reason)
    {
        using var h = new Harness();
        Assert.True(h.Service.OnLoadComplete());
        switch (reason)
        {
            case "door": h.Door.Resolved = false; break;
            case "dead": h.Interior.Alive = false; break;
            case "epoch": h.Context.Snapshot = h.Context.Snapshot with { LoadEpoch = 2 }; break;
            case "session": h.Context.Snapshot = h.Context.Snapshot with { SessionEpoch = Guid.NewGuid() }; break;
            case "player": h.Context.Snapshot = h.Context.Snapshot with { PlayerId = "other" }; break;
            case "folder": h.Context.Snapshot = h.Context.Snapshot with { ActiveSaveFolder = "other" }; break;
        }
        Assert.Null(Record.Exception(() => Assert.True(h.Service.OnSaveStart())));
        Assert.Equal(1, h.Interior.Destroys);
        Assert.Empty(h.Logs);
        h.Door.Resolved = true;
        Assert.True(h.Service.OnSaveComplete());
        Assert.True(h.Interior.Enabled);
        Assert.True(h.Service.OnSaveStart());
        Assert.Equal(1, h.Interior.Destroys);
        Assert.True(h.Service.OnSaveComplete());
    }

    [Theory]
    [InlineData("readiness")]
    [InlineData("position")]
    [InlineData("disable")]
    [InlineData("enable")]
    public void Oc181_unexpected_exception_is_isolated_restores_interactions_and_logs_once_per_load(string step)
    {
        using var h = new Harness();
        Assert.True(h.Service.OnLoadComplete());
        for (var i = 0; i < 2; i++)
        {
            if (step == "enable") Assert.True(h.Service.OnSaveStart());
            h.Interior.ThrowAt = step;
            h.Transport.ThrowRead = step == "position";
            if (step != "enable") Assert.Null(Record.Exception(() => h.Service.OnSaveStart()));
            Assert.Null(Record.Exception(() => h.Service.OnSaveComplete()));
            Assert.True(h.Interior.Enabled);
        }
        var warning = Assert.Single(h.Logs, x => x.Contains("failed closed", StringComparison.Ordinal));
        Assert.Contains("step=", warning);
        Assert.Contains("InvalidOperationException", warning);
        Assert.Contains("managed stack:", warning);
        Assert.Contains(nameof(Oc181HqRetentionTests), warning);
        Assert.True(h.Service.OnPreLoad());
        Assert.True(h.Service.OnLoadComplete());
        h.Interior.ThrowAt = "disable";
        Assert.True(h.Service.OnSaveStart());
        Assert.True(h.Service.OnSaveComplete());
        Assert.Equal(2, h.Logs.Count(x => x.Contains("failed closed", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oc181_load_boundary_cancels_retained_save_even_when_teardown_is_blocked(bool blocked)
    {
        using var h = new Harness();
        Assert.True(h.Service.OnLoadComplete());
        Assert.True(h.Service.OnSaveStart());
        Assert.False(h.Interior.Enabled);
        h.Interior.BlockDestroy = blocked;
        Assert.Equal(!blocked, h.Service.OnPreLoad());
        h.Context.Snapshot = h.Context.Snapshot with { LoadEpoch = 2 };
        h.Interior.BlockDestroy = false;
        Assert.True(h.Service.OnSaveComplete());
        Assert.True(h.Interior.Enabled);
        Assert.True(h.Service.OnSaveStart());
        Assert.True(h.Service.OnSaveComplete());
        Assert.True(h.Interior.Enabled);
        Assert.DoesNotContain(h.Logs, x => x.Contains("failed closed", StringComparison.Ordinal));
    }

    private sealed class Harness : IDisposable
    {
        public Context Context { get; } = new();
        public Door Door { get; } = new();
        public Interior Interior { get; } = new();
        public Transport Transport { get; } = new();
        public List<string> Logs { get; } = new();
        public SyndicateHqRuntimeService Service { get; }
        public Harness() => Service = new(Context, new Authority(), Door, Interior, Transport, Logs.Add, () => 0f);
        public void Dispose() => Service.Dispose();
    }

    private sealed class Context : IRelease1StoryHostContext
    {
        public bool ThrowRead;
        public Release1StoryHostContextSnapshot Snapshot = new(Guid.NewGuid(), 1, "76561190000000001", @"C:\Saves\SaveGame_1");
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; if (ThrowRead) { ThrowRead = false; throw new InvalidOperationException("planned context read failure"); } return Release1StoryHostContextReadStatus.Ready; }
    }
    private sealed class Authority : ISyndicateHqUnlockAuthority
    {
        public SyndicateHqUnlockDecision Evaluate() => SyndicateHqUnlockDecision.Unlocked();
    }
    private sealed class Door : ISyndicateHqDoorSource
    {
        private readonly SyndicateHqDoorFingerprint _fingerprint = new(
            SyndicateHqContract.NightclubDoorPath, "Il2CppScheduleOne.Doors.StaticDoor",
            SyndicateHqContract.NightclubDoorPath + "/IntObj", "Il2CppScheduleOne.Interaction.InteractableObject",
            SyndicateHqContract.NightclubBuildingPath, "Il2CppScheduleOne.Map.NPCEnterableBuilding", SyndicateHqContract.NightclubBuildingGuid,
            SyndicateHqContract.NightclubDoorPath + "/AccessPoint", 0, new(1, 2, 3), new(1, 0, 0), new(4, 5, 6), true, true, true, true, true);
        public bool Resolved = true;
        public SyndicateHqDoorFingerprint? CurrentFingerprint => Resolved ? _fingerprint : null;
        public long Generation { get; private set; }
        public int SubscriptionCount { get; private set; }
        public bool TryAttach() { if (!Resolved) return false; SubscriptionCount = 1; Generation++; return true; }
        public bool Detach() { SubscriptionCount = 0; Generation++; return true; }
        public bool IsCurrent(SyndicateHqDoorFingerprint fingerprint, long generation) => Resolved && ReferenceEquals(fingerprint, _fingerprint) && generation == Generation && SubscriptionCount == 1;
        public bool TryResolveFresh(out SyndicateHqDoorFingerprint fingerprint) { fingerprint = _fingerprint; return Resolved; }
        public bool TryDispose() => Detach();
        public void EvictRetainedForLoad() { }
    }
    private sealed class Interior : ISyndicateHqInterior
    {
        public bool Alive;
        public bool Enabled = true;
        public string? ThrowAt;
        public bool BlockDestroy;
        public bool DeferDestroy;
        public bool PendingDestroy;
        public bool Ready = true;
        public int Destroys;
        public int Ensures;
        public bool IsCreated => Alive;
        public bool IsDestroyPending => PendingDestroy;
        public bool IsReadyForSave { get { MaybeThrow("readiness"); return Alive && Ready; } }
        public bool TryEnsure(SyndicateHqVector3 exteriorAnchor) { Ensures++; if (PendingDestroy) return false; Alive = true; Ready = true; Enabled = true; return true; }
        public bool TryGetMarker(string name, out SyndicateHqVector3 position) { position = new(1000, 31, 1000); return Alive; }
        public bool TrySetSaveInteractions(bool enabled) { Enabled = enabled; MaybeThrow(enabled ? "enable" : "disable"); return Alive; }
        private void MaybeThrow(string step) { if (ThrowAt != step) return; ThrowAt = null; throw new InvalidOperationException("planned native failure"); }
        public void Destroy() { if (BlockDestroy) return; Destroys++; Alive = false; Enabled = false; PendingDestroy = DeferDestroy; }
        public void Dispose() { Alive = false; }
    }
    private sealed class Transport : ISyndicateHqPlayerTransport
    {
        public bool ThrowRead;
        public bool TryCapture(SyndicateHqDoorFingerprint door, Release1StoryHostContextSnapshot context, out SyndicateHqExteriorReturnState state) { state = new(default, door.AccessPointPosition, context.SessionEpoch, context.LoadEpoch, context.PlayerId); return true; }
        public bool TryTeleport(SyndicateHqVector3 position) => true;
        public bool TryReadPosition(out SyndicateHqVector3 position) { position = default; if (!ThrowRead) return true; ThrowRead = false; throw new InvalidOperationException("planned dead player"); }
    }
}
