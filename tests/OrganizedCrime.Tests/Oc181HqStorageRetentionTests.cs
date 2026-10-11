using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc181HqStorageRetentionTests
{
    [Fact]
    public void Oc181_successful_fallback_rebuild_preserves_normal_teardown_warning_policy()
    {
        var boundary = new Boundary();
        var logs = new List<string>();
        using var runtime = Prepare(boundary, logs);
        boundary.ThrowAt = SyndicateHqStorageContract.Definitions[3].Guid;
        Assert.False(runtime.TrySetSaveInteractions(false));
        Assert.True(runtime.PlaceAtPocket(new(1000, 5, 2000)).IsReady);
        logs.Clear();
        boundary.ThrowOnDisable = true;
        runtime.DisableInteractions();
        Assert.Equal(9, logs.Count);
        Assert.All(logs, x => Assert.Contains("Native HQ storage interaction disable failed:", x));
        boundary.ThrowOnDisable = false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oc181_native_invalidation_inside_visibility_callback_declines_and_restores_survivors(bool resume)
    {
        var boundary = new Boundary();
        var logs = new List<string>();
        using var runtime = Prepare(boundary, logs);
        if (resume) Assert.True(runtime.TrySetSaveInteractions(false));
        boundary.KillOnChange = SyndicateHqStorageContract.Definitions[4].Guid;
        Assert.False(runtime.TrySetSaveInteractions(resume));
        Assert.All(boundary.Enabled.Where(x => x.Key != boundary.Dead), x => Assert.True(x.Value));
        Assert.Empty(logs);
        Assert.Equal(0, boundary.Cleanups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oc181_partial_visibility_exception_restores_all_closets_without_inventory_or_lock_cleanup(bool resume)
    {
        var boundary = new Boundary();
        var logs = new List<string>();
        using var runtime = Prepare(boundary, logs);
        if (resume) Assert.True(runtime.TrySetSaveInteractions(false));
        boundary.ThrowAt = SyndicateHqStorageContract.Definitions[3].Guid;
        Assert.False(runtime.TrySetSaveInteractions(resume));
        Assert.All(boundary.Enabled.Values, Assert.True);
        var warning = Assert.Single(logs, x => x.Contains("failed closed", StringComparison.Ordinal));
        Assert.Contains("step=closet", warning);
        Assert.Contains("InvalidOperationException", warning);
        Assert.Contains("managed stack:", warning);
        Assert.Contains(nameof(Oc181HqStorageRetentionTests), warning);
        Assert.Equal(0, boundary.Cleanups);
        Assert.Equal(0, boundary.LockReleases);
        Assert.True(runtime.CurrentResult.IsReady);
    }

    [Fact]
    public void Oc181_dead_closet_declines_before_suspending_any_closet()
    {
        var boundary = new Boundary();
        var logs = new List<string>();
        using var runtime = Prepare(boundary, logs);
        boundary.Dead = SyndicateHqStorageContract.Definitions[4].Guid;
        boundary.Changes.Clear();
        Assert.False(runtime.TrySetSaveInteractions(false));
        Assert.Empty(boundary.Changes);
        Assert.Empty(logs);
    }

    [Fact]
    public void Oc181_dead_closet_at_resume_does_not_strand_healthy_closets()
    {
        var boundary = new Boundary();
        var logs = new List<string>();
        using var runtime = Prepare(boundary, logs);
        Assert.True(runtime.TrySetSaveInteractions(false));
        boundary.Dead = SyndicateHqStorageContract.Definitions[4].Guid;
        boundary.Changes.Clear();
        Assert.False(runtime.TrySetSaveInteractions(true));
        Assert.All(boundary.Enabled.Where(x => x.Key != boundary.Dead), x => Assert.True(x.Value));
        Assert.DoesNotContain(boundary.Changes, x => x.Guid == boundary.Dead);
        Assert.Empty(logs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oc181_storage_exceptions_log_once_and_reset_on_load(bool validation)
    {
        var boundary = new Boundary();
        var logs = new List<string>();
        using var runtime = Prepare(boundary, logs);
        for (var i = 0; i < 2; i++)
        {
            boundary.ThrowValidation = validation;
            if (!validation) boundary.ThrowAt = SyndicateHqStorageContract.Definitions[2].Guid;
            Assert.Null(Record.Exception(() => Assert.False(runtime.TrySetSaveInteractions(false))));
            Assert.All(boundary.Enabled.Values, Assert.True);
        }
        Assert.Single(logs, x => x.Contains("failed closed", StringComparison.Ordinal));
        Assert.False(runtime.BeginLoad(ReadyContext()).IsReady);
        Assert.False(runtime.TrySetSaveInteractions(false));
        Assert.Equal(SyndicateHqStorageStatus.NotPrepared, runtime.PrepareAtVanillaLoadBoundary().Status);
        Assert.True(runtime.CompleteVanillaLoadBoundary().IsReady);
        Assert.True(runtime.PlaceAtPocket(new(1000, 5, 2000)).IsReady);
        boundary.ThrowAt = SyndicateHqStorageContract.Definitions[2].Guid;
        Assert.False(runtime.TrySetSaveInteractions(false));
        Assert.Equal(2, logs.Count(x => x.Contains("failed closed", StringComparison.Ordinal)));
    }

    private static SyndicateHqNativeStorageRuntime Prepare(Boundary boundary, List<string> logs)
    {
        var runtime = new SyndicateHqNativeStorageRuntime(boundary, logs.Add, _ => { });
        runtime.BeginLoad(ReadyContext());
        Assert.Equal(SyndicateHqStorageStatus.NotPrepared, runtime.PrepareAtVanillaLoadBoundary().Status);
        Assert.True(runtime.CompleteVanillaLoadBoundary().IsReady);
        Assert.True(runtime.PlaceAtPocket(new(1000, 5, 2000)).IsReady);
        logs.Clear();
        return runtime;
    }

    private static SyndicateHqStorageContext ReadyContext() => new(
        true, "76561190000000001", @"C:\Saves\76561190000000001\SaveGame_1", @"C:\Saves\76561190000000001\SaveGame_1");

    private sealed class Boundary : ISyndicateHqNativeStorageBoundary
    {
        public Dictionary<Guid, bool> Enabled { get; } = new();
        public List<(Guid Guid, bool Enabled)> Changes { get; } = new();
        public Guid? Dead;
        public Guid? ThrowAt;
        public Guid? KillOnChange;
        public bool ThrowValidation;
        public bool ThrowOnDisable;
        public int Cleanups;
        public int LockReleases;
        public bool TryPrepareGrid(out object grid, out string reason) { grid = new object(); reason = "ready"; return true; }
        public void EvictRetainedClosetsForLoad() { }
        public SyndicateHqNativeStorageLookupStatus FindRestoredCloset(SyndicateHqStorageDefinition definition, out object entity, out string reason) { entity = definition.Guid; reason = "restored"; return Enabled.ContainsKey(definition.Guid) ? SyndicateHqNativeStorageLookupStatus.Restored : SyndicateHqNativeStorageLookupStatus.Missing; }
        public bool TryCreate(object grid, SyndicateHqStorageDefinition definition, out object entity, out string reason) { entity = definition.Guid; Enabled[definition.Guid] = false; reason = "created"; return true; }
        public bool TryValidate(object entity, SyndicateHqStorageDefinition definition, out string reason) { reason = "valid"; if (ThrowValidation) { ThrowValidation = false; throw new InvalidOperationException("planned validation failure"); } return entity is Guid guid && guid != Dead; }
        public bool TryPlace(object entity, SyndicateHqVector3 worldPosition, float yawDegrees, out string reason) { reason = "placed"; return true; }
        public void SetInteractionEnabled(object entity, bool enabled) { var guid = (Guid)entity; Assert.NotEqual(Dead, guid); if (!enabled && ThrowOnDisable) throw new InvalidOperationException("planned normal disable failure"); if (guid == ThrowAt) { ThrowAt = null; throw new InvalidOperationException("planned visibility failure"); } Enabled[guid] = enabled; Changes.Add((guid, enabled)); if (guid == KillOnChange) { Dead = guid; KillOnChange = null; } }
        public bool TryCleanup(object entity, out string reason) { Cleanups++; reason = "cleanup"; return true; }
        public void ReleaseOwnedClosetSlotLocks() => LockReleases++;
        public bool TryReadClosetSlots(SyndicateHqStorageDefinition definition, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots, out string reason) { slots = Array.Empty<Release1SmallCourtesySlotSnapshot>(); reason = "unused"; return false; }
        public Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(SyndicateHqStorageDefinition definition, int slotIndex, out float balance, out string reason) { balance = 0; reason = "unused"; return Release1SmallCourtesyWorldReadStatus.Unavailable; }
        public Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(SyndicateHqStorageDefinition definition, int slotIndex, bool locked, out string reason) { reason = "unused"; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
        public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(SyndicateHqStorageDefinition definition, int slotIndex, float amount, out string reason) { reason = "unused"; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
    }
}
