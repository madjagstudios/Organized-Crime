using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using UnityEngine;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqRuntimeServiceTests
{
    private static readonly string PlayerId = "76561190000000001";

    [Fact]
    public void Load_and_scene_lifecycle_are_idempotent_and_recreate_one_runtime()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        using var service = CreateService(context, source, interior, new FakeTransport());

        Assert.True(service.OnLoadComplete());
        Assert.True(service.OnLoadComplete());
        Assert.Equal(1, source.AttachCalls);
        Assert.Equal(1, interior.EnsureCalls);

        service.OnPreSceneChange();
        service.OnPreSceneChange();
        Assert.Equal(SyndicateHqEntryState.AwaitingDoor, service.State);
        Assert.True(service.OnLoadComplete());
        Assert.Equal(2, source.AttachCalls);
        Assert.Equal(2, interior.EnsureCalls);
    }

    [Fact]
    public void Unlocked_door_enters_at_oc_owned_entry_and_exits_to_captured_position()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, interior, transport);

        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.Equal(SyndicateHqPromptKind.Enter, service.Prompt.Kind);
        Assert.True(service.TryEnter());
        Assert.True(service.IsInside);
        Assert.Equal(interior.Entry, transport.Teleports[0]);

        Assert.True(service.TryExit());
        Assert.False(service.IsInside);
        Assert.Equal(new SyndicateHqVector3(10, 20, 30), transport.Teleports[1]);
    }

    [Fact]
    public void Exit_prompt_only_arms_beside_the_exit_marker_after_the_player_has_left_it()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        // Mirrors the live layout: HQ_ENTRY sits exactly 1.5 units from HQ_EXIT, so an
        // entry teleport lands the player right on the exit-prompt boundary.
        var interior = new FakeInterior { Entry = new(0, 1, -3.0f), Exit = new(0, 1, -4.5f) };
        var transport = new FakeTransport();
        using var service = CreateService(context, source, interior, transport);

        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());

        // First pass right after landing at the entry point: still on the exit boundary,
        // but the prompt must not arm until the player has been seen outside it once.
        service.Update();
        Assert.Equal(SyndicateHqPromptKind.None, service.Prompt.Kind);

        // Player walks further into the room, clear of the exit radius.
        transport.PlayerPosition = new(0, 1, 0);
        service.Update();
        Assert.Equal(SyndicateHqPromptKind.None, service.Prompt.Kind);

        // Player walks back to stand beside the exit marker.
        transport.PlayerPosition = new(0, 1, -4.5f);
        service.Update();
        Assert.Equal(SyndicateHqPromptKind.Exit, service.Prompt.Kind);

        // Player steps away again; the prompt clears on the very next pass instead of
        // lingering across the room until its duration expires.
        transport.PlayerPosition = new(0, 1, 0);
        service.Update();
        Assert.Equal(SyndicateHqPromptKind.None, service.Prompt.Kind);
    }

    [Fact]
    public void Locked_door_never_calls_player_transport()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var transport = new FakeTransport();
        using var service = new SyndicateHqRuntimeService(context, new FakeAuthority(false), source, new FakeInterior(), transport, now: () => 0f);

        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);

        Assert.Equal(SyndicateHqPromptKind.Locked, service.Prompt.Kind);
        Assert.False(service.TryEnter());
        Assert.Empty(transport.Teleports);
    }

    [Fact]
    public void Inside_scene_teardown_returns_safely_before_destroying_runtime()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, interior, transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());

        Assert.True(service.TryTeardown());

        Assert.Equal(new SyndicateHqVector3(10, 20, 30), transport.Teleports[1]);
        Assert.Equal(1, interior.DestroyCalls);
        Assert.Equal(SyndicateHqEntryState.AwaitingDoor, service.State);
        Assert.False(service.IsTeardownBlocked);
    }

    [Fact]
    public void Inside_teardown_with_stale_identity_preserves_recovery_state_and_can_retry()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, interior, transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());
        context.Status = Release1StoryHostContextReadStatus.Pending;

        Assert.False(service.TryTeardown());
        Assert.Equal(SyndicateHqEntryState.Faulted, service.State);
        Assert.True(service.IsTeardownBlocked);
        Assert.Equal(0, interior.DestroyCalls);
        Assert.Single(transport.Teleports); // entry only; no unsafe return was attempted

        context.Status = Release1StoryHostContextReadStatus.Ready;
        Assert.True(service.TryTeardown());
        Assert.Equal(1, interior.DestroyCalls);
    }

    [Fact]
    public void Inside_teardown_transport_exception_preserves_return_state_for_retry()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, interior, transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());
        transport.ThrowOnNextTeleport = true;

        Assert.False(service.TryTeardown());
        Assert.Equal(SyndicateHqEntryState.Faulted, service.State);
        Assert.True(service.IsTeardownBlocked);
        Assert.Equal(0, interior.DestroyCalls);
        transport.ThrowOnNextTeleport = false;
        Assert.True(service.TryTeardown());
    }

    [Fact]
    public void Stale_callback_and_prompt_are_inert_after_teardown_or_generation_change()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        using var service = CreateService(context, source, new FakeInterior(), new FakeTransport());
        service.OnLoadComplete();
        var oldGeneration = source.Generation;
        service.HandleDoorInteraction(source.CurrentFingerprint!, oldGeneration);
        Assert.Equal(SyndicateHqPromptKind.Enter, service.Prompt.Kind);

        service.OnPreSceneChange();
        service.HandleDoorInteraction(source.CurrentFingerprint!, oldGeneration);
        Assert.Equal(SyndicateHqPromptKind.None, service.Prompt.Kind);
        Assert.False(service.TryEnter());

        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, oldGeneration);
        Assert.Equal(SyndicateHqPromptKind.None, service.Prompt.Kind);
    }

    [Fact]
    public void Prompt_action_is_rejected_when_context_epoch_changes()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, new FakeInterior(), transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        context.Snapshot = new(Guid.NewGuid(), 4, PlayerId, @"C:\Saves\76561190000000001\SaveGame_4");

        Assert.False(service.TryEnter());
        Assert.Empty(transport.Teleports);
    }

    [Fact]
    public void Teardown_collaborator_exceptions_leave_a_retryable_faulted_state()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource { ThrowOnDetach = true };
        var interior = new FakeInterior { ThrowOnDestroy = true };
        using var service = CreateService(context, source, interior, new FakeTransport());
        service.OnLoadComplete();

        Assert.False(service.TryTeardown());
        Assert.Equal(SyndicateHqEntryState.Faulted, service.State);
        Assert.True(service.IsTeardownBlocked);
        Assert.False(service.OnLoadComplete());
        source.ThrowOnDetach = false;
        interior.ThrowOnDestroy = false;
        Assert.True(service.TryTeardown());
    }

    [Fact]
    public void Context_exception_during_inside_teardown_preserves_runtime()
    {
        var context = new FakeContext();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        var source = new FakeDoorSource();
        using var service = CreateService(context, source, interior, transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());
        context.ThrowOnRead = true;

        Assert.False(service.TryTeardown());
        Assert.True(service.IsTeardownBlocked);
        Assert.Equal(0, interior.DestroyCalls);
    }

    [Theory]
    [InlineData("preload")]
    [InlineData("scene")]
    [InlineData("save")]
    public void Every_save_and_scene_boundary_returns_inside_player_before_cleanup(string boundary)
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, interior, transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());

        var result = boundary switch
        {
            "preload" => service.OnPreLoad(),
            "scene" => service.OnPreSceneChange(),
            "save" => service.OnSaveStart(),
            _ => false
        };

        Assert.True(result);
        Assert.Equal(new SyndicateHqVector3(10, 20, 30), transport.Teleports[1]);
        Assert.False(service.IsInside);
        Assert.Equal(SyndicateHqPromptKind.None, service.Prompt.Kind);
    }

    [Fact]
    public void Dispose_returns_inside_player_before_marking_service_disposed()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var transport = new FakeTransport();
        var service = CreateService(context, source, interior, transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());

        service.Dispose();

        Assert.Equal(SyndicateHqEntryState.Disposed, service.State);
        Assert.Equal(new SyndicateHqVector3(10, 20, 30), transport.Teleports[1]);
        Assert.Equal(1, interior.DestroyCalls);
    }

    [Fact]
    public void Save_only_lifecycle_recreates_hq_after_safe_save_teardown()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        using var service = CreateService(context, source, interior, new FakeTransport());

        Assert.True(service.OnLoadComplete());
        Assert.True(service.OnSaveStart());
        Assert.True(service.OnSaveComplete());

        Assert.Equal(2, source.AttachCalls);
        Assert.Equal(2, interior.EnsureCalls);
        Assert.False(service.IsTeardownBlocked);
    }

    [Fact]
    public void Composition_evicts_the_retained_door_identity_once_per_real_load_complete_never_on_a_save_only_boundary()
    {
        // Final review fix (finding 5): a vanilla restore can hand back a second in-scene door object
        // for the same identity; the retained cache must be evicted at each real load so the next
        // resolve is forced through the identity-checked fresh scan, but never at a save-only boundary
        // (that would undo Task 3's retained-identity performance fix, since OnSaveStart tears the
        // runtime down too).
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var interior = new FakeInterior();
        var service = CreateService(context, source, interior, new FakeTransport());
        var composition = new SyndicateHqRuntimeComposition(service, () => { });

        Assert.True(composition.OnLoadComplete());
        Assert.Equal(1, source.EvictCalls);

        Assert.True(composition.OnSaveStart());
        Assert.True(composition.OnSaveComplete());
        Assert.Equal(1, source.EvictCalls);

        Assert.True(composition.OnPreLoad());
        Assert.True(composition.OnLoadComplete());
        Assert.Equal(2, source.EvictCalls);
    }

    [Fact]
    public void Activation_failure_with_pending_removal_drains_before_scene_replacement_attach()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource { DetachFailuresRemaining = 2 };
        var interior = new FakeInterior { ThrowOnEnsure = true };
        using var service = CreateService(context, source, interior, new FakeTransport());

        Assert.False(service.OnLoadComplete());
        Assert.True(service.IsTeardownBlocked);
        interior.ThrowOnEnsure = false;

        Assert.False(service.OnPreSceneChange());
        source.DetachFailuresRemaining = 0;
        Assert.True(service.OnLoadComplete());
        Assert.Equal(2, source.AttachCalls);
    }

    [Fact]
    public void Failed_preload_recovery_defers_load_epoch_until_fresh_access_point_recovery()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource();
        var transport = new FakeTransport();
        using var service = CreateService(context, source, new FakeInterior(), transport);
        service.OnLoadComplete();
        service.HandleDoorInteraction(source.CurrentFingerprint!, source.Generation);
        Assert.True(service.TryEnter());

        context.Status = Release1StoryHostContextReadStatus.Pending;
        var beginLoadCalls = 0;
        var composition = new SyndicateHqRuntimeComposition(service, () => beginLoadCalls++);
        Assert.False(composition.OnPreLoad());
        Assert.Equal(0, beginLoadCalls);

        context.Status = Release1StoryHostContextReadStatus.Ready;
        context.Snapshot = context.Snapshot with { LoadEpoch = 4 };
        source.FreshFingerprint = source.CurrentFingerprint! with { AccessPointPosition = new(90, 91, 92) };

        Assert.True(composition.PrepareLoadComplete());
        Assert.Equal(1, beginLoadCalls);
        Assert.Equal(new SyndicateHqVector3(90, 91, 92), transport.Teleports[^1]);
        Assert.True(composition.OnLoadComplete());
    }

    [Fact]
    public void Deinitialization_retains_owner_until_failed_exact_removal_can_retry()
    {
        var context = new FakeContext();
        var source = new FakeDoorSource { DetachFailuresRemaining = 1 };
        var interior = new FakeInterior();
        var service = CreateService(context, source, interior, new FakeTransport());
        service.OnLoadComplete();
        var composition = new SyndicateHqRuntimeComposition(service, () => { });

        Assert.False(composition.TryDispose());
        Assert.True(composition.IsPendingCleanup);
        Assert.False(service.IsDisposed);
        Assert.Equal(1, source.SubscriptionCount);

        source.DetachFailuresRemaining = 0;
        Assert.True(composition.TryDispose());
        Assert.False(composition.IsPendingCleanup);
        Assert.True(service.IsDisposed);
        Assert.Equal(0, source.SubscriptionCount);
        Assert.Equal(1, interior.DisposeCalls);
    }

    private static SyndicateHqRuntimeService CreateService(FakeContext context, FakeDoorSource source, FakeInterior interior, FakeTransport transport) =>
        new(context, new FakeAuthority(true), source, interior, transport, now: () => 0f);

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; set; } = new(Guid.NewGuid(), 3, PlayerId, @"C:\Saves\76561190000000001\SaveGame_4");
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public bool ThrowOnRead { get; set; }
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { if (ThrowOnRead) throw new InvalidOperationException("planned context failure"); snapshot = Snapshot; return Status; }
    }

    private sealed class FakeAuthority : ISyndicateHqUnlockAuthority
    {
        private readonly bool _unlocked;
        public FakeAuthority(bool unlocked) => _unlocked = unlocked;
        public SyndicateHqUnlockDecision Evaluate() => _unlocked ? SyndicateHqUnlockDecision.Unlocked() : SyndicateHqUnlockDecision.Locked("test lock");
    }

    private sealed class FakeDoorSource : ISyndicateHqDoorSource
    {
        public SyndicateHqDoorFingerprint CurrentFingerprint { get; } = new(
            SyndicateHqContract.NightclubDoorPath, "Il2CppScheduleOne.Doors.StaticDoor",
            SyndicateHqContract.NightclubDoorPath + "/IntObj", "Il2CppScheduleOne.Interaction.InteractableObject",
            SyndicateHqContract.NightclubBuildingPath, "Il2CppScheduleOne.Map.NPCEnterableBuilding", SyndicateHqContract.NightclubBuildingGuid,
            SyndicateHqContract.NightclubDoorPath + "/AccessPoint", 0, new(1, 2, 3), new(1, 0, 0), new(4, 5, 6), true, true, true, true, true);
        SyndicateHqDoorFingerprint? ISyndicateHqDoorSource.CurrentFingerprint => CurrentFingerprint;
        public long Generation { get; private set; } = 1;
        public int SubscriptionCount { get; private set; } = 1;
        public int AttachCalls { get; private set; }
        public bool ThrowOnDetach { get; set; }
        public int DetachFailuresRemaining { get; set; }
        public bool ThrowOnResolveFresh { get; set; }
        public SyndicateHqDoorFingerprint FreshFingerprint { get; set; } = null!;
        public bool TryResolveFresh(out SyndicateHqDoorFingerprint fingerprint) { fingerprint = FreshFingerprint ?? CurrentFingerprint; return !ThrowOnResolveFresh; }
        public bool TryAttach() { AttachCalls++; SubscriptionCount = 1; Generation++; return true; }
        public int DetachCalls { get; private set; }
        public bool Detach() { DetachCalls++; if (ThrowOnDetach) throw new InvalidOperationException("planned detach failure"); if (DetachFailuresRemaining > 0) { DetachFailuresRemaining--; return false; } SubscriptionCount = 0; Generation++; return true; }
        public bool IsCurrent(SyndicateHqDoorFingerprint fingerprint, long generation) => ReferenceEquals(CurrentFingerprint, fingerprint) && generation == Generation && SubscriptionCount == 1;
        public bool TryDispose() { SubscriptionCount = 0; return true; }
        public int EvictCalls { get; private set; }
        public void EvictRetainedForLoad() => EvictCalls++;
    }

    private sealed class FakeInterior : ISyndicateHqInterior
    {
        public SyndicateHqVector3 Entry { get; set; } = new(0, 31, 1);
        public SyndicateHqVector3 Exit { get; set; } = new(0, 31, 5);
        public int EnsureCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        public bool IsCreated { get; private set; }
        public bool IsDestroyPending => false;
        public bool ThrowOnDestroy { get; set; }
        public bool ThrowOnEnsure { get; set; }
        public int DisposeCalls { get; private set; }
        public bool TryEnsure(SyndicateHqVector3 _) { EnsureCalls++; if (ThrowOnEnsure) throw new InvalidOperationException("planned ensure failure"); IsCreated = true; return true; }
        public bool TryGetMarker(string name, out SyndicateHqVector3 position)
        {
            position = name == SyndicateHqContract.EntryMarker ? Entry : Exit;
            return name is SyndicateHqContract.EntryMarker or SyndicateHqContract.ExitMarker;
        }
        public void Destroy() { if (ThrowOnDestroy) throw new InvalidOperationException("planned destroy failure"); DestroyCalls++; IsCreated = false; }
        public void Dispose() { DisposeCalls++; IsCreated = false; }
    }

    private sealed class FakeTransport : ISyndicateHqPlayerTransport
    {
        public List<SyndicateHqVector3> Teleports { get; } = new();
        public bool ThrowOnNextTeleport { get; set; }
        public SyndicateHqVector3? PlayerPosition { get; set; }
        public bool TryCapture(SyndicateHqDoorFingerprint door, Release1StoryHostContextSnapshot context, out SyndicateHqExteriorReturnState state)
        {
            state = new(new(10, 20, 30), door.AccessPointPosition, context.SessionEpoch, context.LoadEpoch, context.PlayerId);
            return true;
        }
        public bool TryTeleport(SyndicateHqVector3 position) { if (ThrowOnNextTeleport) { ThrowOnNextTeleport = false; throw new InvalidOperationException("planned teleport failure"); } Teleports.Add(position); PlayerPosition = position; return true; }
        public bool TryReadPosition(out SyndicateHqVector3 position) { position = PlayerPosition ?? default; return true; }
    }
}
