using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyDepositTests
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("52525252-5252-5252-5252-525252525252");

    [Fact]
    public void Only_the_selected_drop_close_prepares_one_exact_unit_without_native_mutation()
    {
        using var harness = ActiveMission();
        var assignment = harness.Assignment;
        var beforeRevision = harness.Story.State!.Revision;

        Assert.Equal(assignment.DeadDropGuid, harness.World.SubscribedDropGuid);
        Assert.Equal(Release1SmallCourtesyDepositStatus.NoWork, harness.Service.TryHandleDropClosed("other-drop"));
        Assert.Empty(harness.Story.State.NativeEffects);

        var result = harness.Service.TryHandleDropClosed(assignment.DeadDropGuid);

        Assert.Equal(Release1SmallCourtesyDepositStatus.AwaitingPreparedSave, result);
        var effect = Assert.Single(harness.Story.State.NativeEffects);
        Assert.Equal(Release1NativeEffectPhase.Prepared, effect.Phase);
        Assert.Equal(beforeRevision + 1, harness.Story.State.Revision);
        Assert.True(Release1SmallCourtesyCargoIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity));
        Assert.Equal(0, identity!.SlotIndex);
        Assert.Equal(2, identity.PreQuantity);
        Assert.Equal(1, identity.PostQuantity);
        Assert.Equal(2, harness.World.Slots[0].Quantity);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.False(harness.Story.TryGetExecutablePreparedEffect(effect.EffectId, out _));
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Prepared_applied_and_committed_follow_two_native_save_boundaries_in_order()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Assert.Equal(new[] { "read", "lock:0:true" }, harness.World.MutationLog);
        harness.World.ResetMutationEvidence();

        Save(harness);

        Assert.Equal(1, harness.World.Slots[0].Quantity);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(
            new[] { "read", "change:0:-1", "read", "lock:0:false" },
            harness.World.MutationLog);
        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(harness).Phase);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Repository.StoredState!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer").Phase);
        Assert.Single(harness.Repository.Updates);

        Save(harness);

        Assert.Equal(Release1NativeEffectPhase.Committed, Cargo(harness).Phase);
        Assert.Equal(1, harness.World.QuantityChanges);
        Save(harness);
        Assert.Equal(1, harness.World.QuantityChanges);
    }

    [Theory]
    [InlineData("other-product", "brick", 1, true)]
    [InlineData("cocaine", "jar", 1, true)]
    [InlineData("cocaine", "brick", 0, true)]
    [InlineData("cocaine", "brick", 1, false)]
    public void Wrong_product_package_quantity_or_packaging_is_inert(
        string productId,
        string packageId,
        int quantity,
        bool isPackaged)
    {
        using var harness = ActiveMission();
        harness.World.Slots[0] = harness.World.Slots[0] with
        {
            ProductId = productId,
            PackagingId = packageId,
            Quantity = quantity,
            IsPackaged = isPackaged
        };

        var result = harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);

        Assert.Equal(Release1SmallCourtesyDepositStatus.NoWork, result);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
    }

    [Fact]
    public void Persisted_prepared_with_exact_post_state_infers_applied_without_consuming_again()
    {
        var repository = new FakeRepository();
        var world = new FakeWorld(ContextSnapshot());
        using (var first = ActiveMission(repository, world))
        {
            Assert.Equal(
                Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
                first.Service.TryHandleDropClosed(first.Assignment.DeadDropGuid));
            first.Story.OnSaveStart();
            first.Service.OnSaveStart();
            first.Story.OnSaveComplete();
            first.Service.OnPreLoad();
        }

        world.Slots[0] = world.Slots[0] with { Quantity = 1 };
        world.ResetMutationEvidence();
        using var restored = LoadedMission(repository, world);

        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(restored).Phase);
        Assert.Equal(0, world.QuantityChanges);
    }

    [Fact]
    public void Prepared_deposit_retains_the_exact_slot_lock_so_player_withdrawal_cannot_false_pass()
    {
        using var harness = ActiveMission();

        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Assert.True(harness.World.IsLocked);
        Assert.False(harness.World.TryPlayerSetQuantity(0, 1));

        Save(harness);

        Assert.Equal(1, harness.World.Slots[0].Quantity);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void Unpersisted_prepared_waits_even_when_the_live_slot_already_matches_post_state()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.World.Slots[0] = harness.World.Slots[0] with { Quantity = 1 };

        var result = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1SmallCourtesyDepositStatus.AwaitingPreparedSave, result);
        Assert.Equal(Release1NativeEffectPhase.Prepared, Cargo(harness).Phase);
        Assert.Equal(0, harness.World.QuantityChanges);
    }

    [Fact]
    public void Persisted_applied_with_pre_state_is_ambiguous_and_never_consumes_again()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Save(harness);
        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(harness).Phase);
        harness.World.Slots[0] = harness.World.Slots[0] with { Quantity = 2 };
        harness.World.ResetMutationEvidence();

        Save(harness);
        var repeated = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1SmallCourtesyDepositStatus.Ambiguous, repeated);
        Assert.Equal(2, harness.World.Slots[0].Quantity);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(harness).Phase);
    }

    [Fact]
    public void Conflicting_slot_after_prepare_is_blocked_ambiguous_and_never_retried()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.World.Slots[0] = harness.World.Slots[0] with { Quantity = 4 };

        Save(harness);

        var effect = Cargo(harness);
        Assert.Equal(Release1NativeEffectPhase.Prepared, effect.Phase);
        Assert.True(effect.ExecutionBlocked);
        Assert.False(harness.World.IsLocked);
        Assert.Equal(0, harness.World.QuantityChanges);
        harness.Service.ReconcileDeposit();
        Assert.Equal(0, harness.World.QuantityChanges);
    }

    [Fact]
    public void Quit_before_applied_save_restores_prepared_and_replays_once_from_native_pre_state()
    {
        var repository = new FakeRepository();
        var world = new FakeWorld(ContextSnapshot());
        using (var first = ActiveMission(repository, world))
        {
            Assert.Equal(
                Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
                first.Service.TryHandleDropClosed(first.Assignment.DeadDropGuid));
            Save(first);
            Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(first).Phase);
            Assert.Equal(Release1NativeEffectPhase.Prepared, repository.StoredState!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer").Phase);
        }

        world.Slots[0] = world.Slots[0] with { Quantity = 2 };
        world.ResetMutationEvidence();
        using var restored = LoadedMission(repository, world);

        Assert.Equal(1, world.Slots[0].Quantity);
        Assert.Equal(1, world.QuantityChanges);
        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(restored).Phase);
    }

    [Fact]
    public void Throwing_mutation_records_ambiguity_releases_the_lock_and_teardown_unsubscribes()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.World.ThrowOnChange = true;

        Save(harness);

        Assert.True(Cargo(harness).ExecutionBlocked);
        Assert.False(harness.World.IsLocked);
        Assert.Contains("lock:0:false", harness.World.MutationLog);
        harness.Service.OnPreLoad();
        Assert.True(harness.World.SubscriptionDisposed);
    }

    [Fact]
    public void Deposit_admission_is_fail_closed_for_saving_context_drift_and_expired_timed_stage()
    {
        using var saving = ActiveMission();
        saving.Service.OnSaveStart();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.Rejected,
            saving.Service.TryHandleDropClosed(saving.Assignment.DeadDropGuid));
        Assert.Empty(saving.Story.State!.NativeEffects);

        using var drifted = ActiveMission();
        drifted.World.Context = drifted.World.Context with { PlayerId = "76561197984645370" };
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.Rejected,
            drifted.Service.TryHandleDropClosed(drifted.Assignment.DeadDropGuid));
        Assert.Empty(drifted.Story.State!.NativeEffects);

        using var expired = ActiveMission();
        expired.World.TotalMinutes = 24d * 60d;
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.NoWork,
            expired.Service.TryHandleDropClosed(expired.Assignment.DeadDropGuid));
        Assert.Empty(expired.Story.State!.NativeEffects);
    }

    [Fact]
    public void Exact_close_subscription_and_duplicate_callbacks_prepare_only_one_effect()
    {
        using var harness = ActiveMission();

        harness.World.RaiseClosed("other-drop");
        Assert.Empty(harness.Story.State!.NativeEffects);
        harness.World.RaiseClosed(harness.Assignment.DeadDropGuid);
        harness.World.RaiseClosed(harness.Assignment.DeadDropGuid);

        Assert.Single(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
    }

    [Fact]
    public void Deposit_prepared_before_deadline_prevents_a_later_tick_from_failing_the_stage()
    {
        using var harness = ActiveMission();
        harness.World.TotalMinutes = (24d * 60d) - 1d;
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.World.TotalMinutes = 24d * 60d;

        harness.Service.Update();

        Assert.Equal(Release1MissionState.Active, harness.Story.State!.Missions[0].State);
        Assert.Equal(Release1NativeEffectPhase.Prepared, Cargo(harness).Phase);
        Assert.Equal(0, harness.World.QuantityChanges);
    }

    [Fact]
    public void Failed_native_save_never_authorizes_slot_consumption()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.Repository.FailNextUpdate = true;

        Save(harness);

        Assert.Equal(2, harness.World.Slots[0].Quantity);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(Release1NativeEffectPhase.Prepared, Cargo(harness).Phase);
    }

    [Fact]
    public void Partial_lock_failure_is_retained_and_released_during_preload()
    {
        using var harness = ActiveMission();
        harness.World.ThrowAfterLockOnce = true;
        harness.World.UnlockFailuresRemaining = 2;

        Assert.Equal(
            Release1SmallCourtesyDepositStatus.Rejected,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));

        Assert.True(harness.World.IsLocked);
        Assert.Equal(0, harness.World.QuantityChanges);
        harness.Service.OnPreLoad();
        Assert.False(harness.World.IsLocked);
        Assert.Contains("lock:0:false", harness.World.MutationLog);
    }

    [Fact]
    public void Ambiguous_partial_lock_status_retains_exact_identity_for_preload_cleanup()
    {
        using var harness = ActiveMission();
        harness.World.ReturnAmbiguousAfterLockOnce = true;
        harness.World.UnlockFailuresRemaining = 2;

        Assert.Equal(
            Release1SmallCourtesyDepositStatus.Rejected,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Assert.True(harness.World.IsLocked);

        harness.Service.OnPreLoad();

        Assert.False(harness.World.IsLocked);
        Assert.Contains("lock:0:false", harness.World.MutationLog);
    }

    [Fact]
    public void Post_mutation_verification_failure_blocks_the_effect_and_unlocks()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.World.ThrowOnReadAfterMutation = true;

        Save(harness);

        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.True(Cargo(harness).ExecutionBlocked);
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void Initial_slot_read_failure_is_rejected_without_preparing_or_mutating()
    {
        using var harness = ActiveMission();
        harness.World.ThrowOnRead = true;

        var result = harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);

        Assert.Equal(Release1SmallCourtesyDepositStatus.Rejected, result);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void Unlock_failure_retains_exact_lock_for_preload_retry()
    {
        using var harness = ActiveMission();
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        harness.World.ThrowOnUnlockOnce = true;

        Save(harness);

        Assert.True(harness.World.IsLocked);
        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(harness).Phase);
        harness.Service.OnPreLoad();
        Assert.False(harness.World.IsLocked);
    }

    internal static Harness ActiveMission(
        FakeRepository? repository = null,
        FakeWorld? world = null,
        Release1SmallCourtesyAssignmentMode mode = Release1SmallCourtesyAssignmentMode.Primary)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var introReceipt = "intro-deposit";
        Assert.True(story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch,
            context.Snapshot.LoadEpoch,
            context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            introReceipt,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, introReceipt).Value)).Accepted);
        world ??= new FakeWorld(context.Snapshot);
        var service = new Release1SmallCourtesyMissionService(story, world);
        service.OnLoadComplete();
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, service.TryReview().Status);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, service.TryAccept().Status);
        if (mode is Release1SmallCourtesyAssignmentMode.MakeGood or Release1SmallCourtesyAssignmentMode.Recovery)
        {
            world.TotalMinutes = 24d * 60d;
            service.Update();
            Assert.Equal(Release1MissionState.MakeGoodOffered, story.State!.Missions[0].State);
            Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, service.TryReview().Status);
            Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, service.TryAccept().Status);
        }
        if (mode == Release1SmallCourtesyAssignmentMode.Recovery)
        {
            world.TotalMinutes = 48d * 60d;
            service.Update();
            Assert.Equal(Release1MissionState.RecoveryAvailable, story.State!.Missions[0].State);
            Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, service.TryReview().Status);
            Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, service.TryAccept().Status);
        }
        repository.ResetEvidence();
        world.ResetMutationEvidence();
        return new(story, service, world, context, repository);
    }

    internal static Harness LoadedMission(FakeRepository repository, FakeWorld world)
    {
        var context = new FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var service = new Release1SmallCourtesyMissionService(story, world);
        service.OnLoadComplete();
        return new(story, service, world, context, repository);
    }

    internal static void Save(Harness harness)
    {
        harness.Story.OnSaveStart();
        harness.Service.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
    }

    private static Release1NativeEffectJournalEntry Cargo(Harness harness) =>
        harness.Story.State!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer");

    private static Release1StoryHostContextSnapshot ContextSnapshot() =>
        new(SessionEpoch, 1, PlayerId, Path.GetTempPath());

    internal sealed class Harness : IDisposable
    {
        public Harness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyMissionService service,
            FakeWorld world,
            FakeContext context,
            FakeRepository repository)
        {
            Story = story;
            Service = service;
            World = world;
            Context = context;
            Repository = repository;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public Release1SmallCourtesyAssignment Assignment => Story.State!.SmallCourtesyAssignments.MaxBy(value => value.Attempt)!;
        public void Dispose() { Service.Dispose(); Story.Dispose(); }
    }

    internal sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        private Action<string>? _closed;

        public FakeWorld(Release1StoryHostContextSnapshot context)
        {
            Context = context;
            Slots[0] = new(0, "cocaine", "brick", 2, true, 1_000f);
        }

        public Release1StoryHostContextSnapshot Context { get; set; }
        public double TotalMinutes { get; set; }
        public Dictionary<int, Release1SmallCourtesySlotSnapshot> Slots { get; } = new();
        public List<string> MutationLog { get; } = new();
        public string? SubscribedDropGuid { get; private set; }
        public bool SubscriptionDisposed { get; private set; }
        public bool IsLocked { get; private set; }
        public bool ThrowOnChange { get; set; }
        public bool ThrowAfterLockOnce { get; set; }
        public bool ReturnAmbiguousAfterLockOnce { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnReadAfterMutation { get; set; }
        public bool ThrowOnUnlockOnce { get; set; }
        public int UnlockFailuresRemaining { get; set; }
        public int QuantityChanges { get; private set; }
        public float CashBalance { get; set; } = 500f;
        public int CashChanges { get; private set; }
        public bool ThrowOnCashChange { get; set; }
        public bool ThrowOnCashReadAfterMutation { get; set; }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            context = Context;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            totalMinutes = TotalMinutes;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "Selected", 1, 2, 3, true) };
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind,
            out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = kind == Release1SmallCourtesyPackageKind.Brick ? new("brick", "Brick") : new("jar", "Jar");
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid,
            out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            MutationLog.Add("read");
            if (ThrowOnRead)
                throw new InvalidOperationException("synthetic slot read failure");
            if (ThrowOnReadAfterMutation && QuantityChanges > 0)
                throw new InvalidOperationException("synthetic post-mutation read failure");
            slots = Slots.Values.OrderBy(slot => slot.SlotIndex).ToArray();
            return deadDropGuid == "drop-a"
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Release1HoldRoomSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
        {
            MutationLog.Add($"lock:{slotIndex}:{locked.ToString().ToLowerInvariant()}");
            if (locked && ReturnAmbiguousAfterLockOnce)
            {
                IsLocked = true;
                ReturnAmbiguousAfterLockOnce = false;
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            }
            if (locked && ThrowAfterLockOnce)
            {
                IsLocked = true;
                ThrowAfterLockOnce = false;
                throw new InvalidOperationException("synthetic partial lock failure");
            }
            if (!locked && UnlockFailuresRemaining > 0)
            {
                UnlockFailuresRemaining--;
                throw new InvalidOperationException("synthetic repeated unlock failure");
            }
            if (!locked && ThrowOnUnlockOnce)
            {
                ThrowOnUnlockOnce = false;
                throw new InvalidOperationException("synthetic unlock failure");
            }
            IsLocked = locked;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
        {
            MutationLog.Add($"change:{slotIndex}:{amount}");
            if (ThrowOnChange) throw new InvalidOperationException("synthetic quantity failure");
            var current = Slots[slotIndex];
            Slots[slotIndex] = current with { Quantity = current.Quantity + amount };
            QuantityChanges++;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            MutationLog.Add($"insert:{slotIndex}:{productId}:{packagingId}:{quantity}");
            if (!Slots.TryGetValue(slotIndex, out var existing) || existing.Quantity != 0)
            {
                reason = "the slot was not empty before staging.";
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            }
            Slots[slotIndex] = new(slotIndex, productId, packagingId, quantity, true, quantity * 1_000f);
            reason = "the packaged product was staged and read back with the exact expected values.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid,
            Action<string> callback,
            out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            SubscribedDropGuid = deadDropGuid;
            SubscriptionDisposed = false;
            _closed = callback;
            subscription = new FakeSubscription(deadDropGuid, () =>
            {
                SubscriptionDisposed = true;
                _closed = null;
            });
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            if (ThrowOnCashReadAfterMutation && CashChanges > 0)
                throw new InvalidOperationException("synthetic post-payment read failure");
            balance = CashBalance;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            if (ThrowOnCashChange) throw new InvalidOperationException("synthetic cash mutation failure");
            CashBalance += amount;
            CashChanges++;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // OC-73. Mirrors S1ApiRelease1SmallCourtesyWorld.TryDebitCashBalance's negative only
        // convention: reused by fakes across the suite that construct this FakeWorld, so a caller
        // exercising the Chief's debit path against it sees the same guard as the production wrapper.
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
        {
            if (!float.IsFinite(amount) || amount >= 0f || CashBalance + amount < 0f)
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            if (ThrowOnCashChange) throw new InvalidOperationException("synthetic cash mutation failure");
            CashBalance += amount;
            CashChanges++;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // OC-73. This general purpose fake is not Chief specific; the lockdown seam is exercised
        // through Release1ChiefServiceTests.cs's own ChiefFakeWorld.
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        // The mission under test never reads production activity.
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = Release1ProductionActivitySnapshot.Empty;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        { snapshot = Release1FieldContactSnapshot.Unavailable(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { reason = "this fake never despawns a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { reason = "this fake never provokes a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { reason = "this fake never parks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        { reason = "this fake never unparks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public void ResetMutationEvidence()
        {
            MutationLog.Clear();
            QuantityChanges = 0;
            CashChanges = 0;
        }

        public void RaiseClosed(string deadDropGuid) => _closed?.Invoke(deadDropGuid);

        public bool TryPlayerSetQuantity(int slotIndex, int quantity)
        {
            if (IsLocked) return false;
            var current = Slots[slotIndex];
            Slots[slotIndex] = current with { Quantity = quantity };
            return true;
        }

        private sealed class FakeSubscription : IRelease1SmallCourtesyDropSubscription
        {
            private readonly Action _dispose;
            private bool _disposed;
            public FakeSubscription(string deadDropGuid, Action dispose) { DeadDropGuid = deadDropGuid; _dispose = dispose; }
            public string DeadDropGuid { get; }
            public void Dispose() { if (_disposed) return; _disposed = true; _dispose(); }
        }
    }

    internal sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = ContextSnapshot();
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    internal sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public List<Release1StoryState> Updates { get; } = new();
        public bool FailNextUpdate { get; set; }

        public Release1StoryStoreLoadResult Load() => new(
            true,
            StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
            Release1StoryStoreFailureReason.None,
            string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic save failure");
            }
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public void ResetEvidence() => Updates.Clear();
    }
}
