using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PresentationProjectorTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Inactive_service_produces_zero_native_calls()
    {
        var harness = new Harness();
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.Empty(harness.Native.Events);
    }

    [Fact]
    public void Saving_service_produces_zero_native_calls()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Service.OnSaveStart();
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.Empty(harness.Native.Events);
    }

    [Fact]
    public void Save_suspended_projector_skips_the_pass()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.OnSaveStart();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
    }

    [Fact]
    public void Fresh_offer_sets_labeled_buttons()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();

        projector.Reconcile();

        var setEvent = Assert.Single(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.Contains("Accept", setEvent, StringComparison.Ordinal);
        Assert.Contains("Decline", setEvent, StringComparison.Ordinal);
        Assert.NotNull(projector.LastPlan);
        Assert.NotNull(projector.LastPlan!.Decision);
    }

    [Fact]
    public void Choosing_accept_routes_to_the_service_and_sends_the_reply()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        var accept = harness.Native.Callbacks["Accept"];

        accept();

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        var sendEvent = Assert.Single(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        Assert.Equal(Chapter3NellCopy.AcceptedText, sendEvent["SendMessage:".Length..]);
        Assert.NotEmpty(harness.Service.State!.PresentationReceipts);
    }

    [Fact]
    public void Second_reconcile_sends_nothing()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();
        var sendsAfterFirst = SendCount(harness);

        projector.Reconcile();

        Assert.Equal(sendsAfterFirst, SendCount(harness));
        Assert.Equal(1, sendsAfterFirst);
    }

    [Fact]
    public void Owned_elsewhere_skips_the_decision_but_still_sends_messages()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector(ownedElsewhere: () => true);

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");

        harness.Service.TryAcceptOffer();
        projector.Reconcile();

        Assert.Equal(1, SendCount(harness));
    }

    [Fact]
    public void Matching_observed_decision_sets_nothing()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
    }

    [Fact]
    public void Null_observed_id_with_same_labels_rebinds()
    {
        var harness = new Harness();
        harness.Activate();
        var desired = harness.Service.GetPresentationPlan(false).Decision!;
        harness.Native.Decision = new Release1ObservedDecision(
            null, desired.Options.Select(o => o.Label).ToArray(), desired.Prompt);
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
    }

    [Fact]
    public void Null_observed_id_with_other_prompt_rebinds_by_labels()
    {
        var harness = new Harness();
        harness.Activate();
        var desired = harness.Service.GetPresentationPlan(false).Decision!;
        harness.Native.Decision = new Release1ObservedDecision(
            null, desired.Options.Select(o => o.Label).ToArray(), "Some other prompt.");
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
        Assert.Single(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaving_active_clears_last_plan()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        Assert.NotNull(projector.LastPlan);

        harness.Service.OnSaveStart();
        var events = harness.Native.Events.Count;
        projector.Reconcile();

        Assert.Null(projector.LastPlan);
        Assert.Equal(events, harness.Native.Events.Count);
    }

    [Fact]
    public void Sends_and_taps_log_one_line_each_and_idle_passes_log_nothing()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.Contains("Chapter 3 Nell: sent offer-1", harness.Logs);

        harness.Native.Callbacks["Accept"]();

        Assert.Contains("Chapter 3 Nell: Accept -> job 1", harness.Logs);
        Assert.Contains("Chapter 3 Nell: sent job-1-accepted", harness.Logs);

        var logged = harness.Logs.Count;
        projector.Reconcile();

        Assert.Equal(logged, harness.Logs.Count);
    }

    [Fact]
    public void Mismatched_labels_clear_then_set()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Native.Decision = new Release1ObservedDecision("other", new[] { "Yes", "No" }, "Other.");
        var projector = harness.CreateProjector();

        projector.Reconcile();

        var clearIndex = harness.Native.Events.IndexOf("ClearDecision");
        var setIndex = harness.Native.Events.FindIndex(e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.True(clearIndex >= 0, "Expected a ClearDecision event.");
        Assert.True(setIndex >= 0, "Expected a SetLabeledDecision event.");
        Assert.True(clearIndex < setIndex, "Expected the clear before the set.");
    }

    [Fact]
    public void No_desired_decision_clears_the_observed_one()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Service.TryAcceptOffer();
        harness.Native.Decision = new Release1ObservedDecision("stale", new[] { "Accept", "Decline" }, "Stale.");
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.Contains("ClearDecision", harness.Native.Events);
    }

    [Fact]
    public void Fault_stops_the_pass_and_next_pass_retries()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Service.TryAcceptOffer();
        harness.Native.NextSendStatus = Release1NativePresentationStatus.Faulted;
        var projector = harness.CreateProjector();

        projector.Reconcile();

        Assert.Equal(1, SendCount(harness));
        Assert.Empty(harness.Service.State!.PresentationReceipts);

        harness.Native.Events.Clear();
        harness.Native.NextSendStatus = Release1NativePresentationStatus.Succeeded;
        projector.Reconcile();

        Assert.Equal(1, SendCount(harness));
        Assert.NotEmpty(harness.Service.State!.PresentationReceipts);
    }

    [Fact]
    public void Ownership_check_fault_still_reconciles()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector(ownedElsewhere: () => throw new InvalidOperationException("boom"));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
    }

    [Fact]
    public void OnPreLoad_resets_and_delegates()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        Assert.NotNull(projector.LastPlan);

        projector.OnPreLoad();

        Assert.Null(projector.LastPlan);
        Assert.Contains("NativeOnPreLoad", harness.Native.Events);
    }

    [Fact]
    public void Frozen_job_applies_the_quest_once()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();

        projector.Reconcile();

        var applied = Assert.Single(harness.Native.AppliedQuests);
        Assert.Equal(Chapter3QuestCopy.QuestKey, applied.Key);
        Assert.Equal("Family Work", applied.Title);
        var entry = Assert.Single(applied.Entries);
        Assert.Equal("Drop 2 bricks of Rich at Drop A", entry.Text);
        Assert.Equal(Release1DesiredEntryState.Active, entry.State);
        Assert.Equal(new Release1WorldPoint(1f, 2f, 3f), entry.Marker);
    }

    [Fact]
    public void Second_reconcile_makes_no_quest_writes()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();
        projector.Reconcile();
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e == "ApplyQuest");
        Assert.DoesNotContain(harness.Native.Events, e => e == "EndQuest");
    }

    [Fact]
    public void Quest_reapplies_when_entry_texts_move_under_steady_states()
    {
        static Release1DesiredEntry[] DemandEntries(string made, string handed) => new[]
        {
            new Release1DesiredEntry("Family demand: 250 OG Kush", Release1DesiredEntryState.Active, null),
            new Release1DesiredEntry(made, Release1DesiredEntryState.Active, null),
            new Release1DesiredEntry(handed, Release1DesiredEntryState.Active, null),
        };
        static Release1DesiredQuest DemandQuest(string made, string handed) => new(
            Chapter3QuestCopy.QuestKey, Chapter3QuestCopy.QuestTitle, DemandEntries(made, handed));
        var harness = new Harness();
        harness.Activate();
        var quest = DemandQuest("Made in the warehouse: 30/250", "Handed over: 0/250");
        var projector = harness.CreateProjector(plan: () => new Chapter3PresentationPlan(
            Array.Empty<Chapter3DesiredMessage>(), null, quest));
        projector.Reconcile();
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e == "ApplyQuest");
        quest = DemandQuest("Made in the warehouse: 90/250", "Handed over: 40/250");
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == "ApplyQuest");
        Assert.Equal(2, harness.Native.AppliedQuests.Count);
        Assert.Equal("Made in the warehouse: 90/250", harness.Native.AppliedQuests[1].Entries[1].Text);
    }

    [Fact]
    public void Quest_ends_on_completion()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();
        projector.Reconcile();
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");

        projector.Reconcile();

        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, harness.Native.EndedQuestKeys);
        Assert.Null(harness.Native.Quest);
    }

    [Fact]
    public void Quest_ends_on_failure()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();
        projector.Reconcile();
        Assert.True(harness.Service.QaFailActiveJob().Ok);

        projector.Reconcile();

        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, harness.Native.EndedQuestKeys);
    }

    [Fact]
    public void Quest_ends_on_leaving_active()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();
        projector.Reconcile();
        harness.Service.OnPreLoad();

        projector.Reconcile();

        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, harness.Native.EndedQuestKeys);
        Assert.Null(projector.LastPlan);
    }

    [Fact]
    public void Quest_step_runs_while_release1_owns_the_decision()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector(ownedElsewhere: () => true);
        projector.Reconcile();
        harness.Service.TryAcceptOffer();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.Single(harness.Native.AppliedQuests);
    }

    [Fact]
    public void No_quest_projected_for_an_unfrozen_job()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        harness.Native.Callbacks["Accept"]();

        projector.Reconcile();

        Assert.Empty(harness.Native.AppliedQuests);
        Assert.Empty(harness.Native.EndedQuestKeys);
    }

    [Fact]
    public void Quest_key_round_trip_through_the_fake_with_unknown_keys_rejected()
    {
        var native = new FakeNative();
        var quest = new Release1DesiredQuest(
            Chapter3QuestCopy.QuestKey,
            "Family Work",
            new[] { new Release1DesiredEntry("Drop 2 bricks", Release1DesiredEntryState.Active, new Release1WorldPoint(1f, 2f, 3f)) });

        Assert.Equal(Release1NativePresentationStatus.Succeeded, native.TryApplyQuest(quest));
        Assert.Equal(Release1NativePresentationStatus.Succeeded, native.TryReadQuest(Chapter3QuestCopy.QuestKey, out var observed));
        Assert.NotNull(observed);
        Assert.Equal(Release1NativePresentationStatus.Succeeded, native.TryEndQuest(Chapter3QuestCopy.QuestKey));
        Assert.Equal(Release1NativePresentationStatus.Succeeded, native.TryReadQuest(Chapter3QuestCopy.QuestKey, out observed));
        Assert.Null(observed);

        var unknown = new Release1DesiredQuest("nope", "Nope", Array.Empty<Release1DesiredEntry>());
        Assert.Equal(Release1NativePresentationStatus.Rejected, native.TryApplyQuest(unknown));
    }

    [Fact]
    public void Scene_change_skips_the_pass_until_load_complete()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.OnPreSceneChange();

        projector.Reconcile();

        Assert.Empty(harness.Native.Events);

        projector.OnLoadComplete();
        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
    }

    [Fact]
    public void Reload_without_restored_responses_sets_the_decision_on_the_first_pass()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        projector.Reconcile();
        Assert.Contains(harness.Logs, line => line.Contains("sent offer-1", StringComparison.Ordinal));

        // Reload with the sent subject persisted. The fresh native observes
        // no decision, modelling the S1API 3.2.1-beta.7 finding that Nell's
        // restored responses never come back (SaveGame_5/NPCs.json holds
        // Accept/Decline yet LoadedResponses stays empty). The subject was
        // already sent, so the first pass reprompts immediately instead of
        // holding for a restore that never lands.
        var reloaded = new Harness();
        reloaded.Repository.Stored = harness.Service.State;
        reloaded.Activate();
        Assert.Null(reloaded.Native.Decision);
        var reloadedProjector = reloaded.CreateProjector();

        reloadedProjector.Reconcile();

        Assert.DoesNotContain(reloaded.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        var setEvent = Assert.Single(reloaded.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.Contains("Accept", setEvent, StringComparison.Ordinal);
        Assert.Contains("Decline", setEvent, StringComparison.Ordinal);
        Assert.Contains(reloaded.Logs, line => line.Contains("reprompted", StringComparison.Ordinal));
    }


    private static void SeedFreezableWorld(DeadDropFakeWorld world)
    {
        world.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true));
        world.SeedDrops(new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "description", 1d, 2d, 3d, true));
    }

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);

    private static int SendCount(Harness harness) =>
        harness.Native.Events.Count(e => e.StartsWith("SendMessage:", StringComparison.Ordinal));

    private sealed class Harness
    {
        public FakeNative Native { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = 11 };
        public DeadDropFakeWorld World { get; } = new();
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.Wallet = new FakeWallet { Balance = 100_000 };
        }

        public void WireDeadDropWorld()
        {
            Service.DeadDropWorld = World;
            Service.Wallet = new Chapter3Wallet(World);
        }

        public void Activate()
        {
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
        }

        public Chapter3PresentationProjector CreateProjector(
            Func<Chapter3PresentationPlan>? plan = null, Func<bool>? ownedElsewhere = null) =>
            new(Service, plan ?? (() => Service.GetPresentationPlan(false)), Native,
                new Chapter3PresentationCommandRouter(Service), Logs.Add, ownedElsewhere);
    }

    private sealed class FakeNative : IRelease1NativePresentation
    {
        public List<string> Events { get; } = new();
        public Dictionary<string, Action> Callbacks { get; } = new(StringComparer.Ordinal);
        public Release1ObservedDecision? Decision { get; set; }
        public Release1NativePresentationStatus NextSendStatus { get; set; } = Release1NativePresentationStatus.Succeeded;

        public Release1NativePresentationStatus TryEnsureContact()
        {
            Events.Add("EnsureContact");
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts)
        {
            texts = Array.Empty<string>();
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision)
        {
            Events.Add("ReadDecision");
            decision = Decision;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySendMessage(string text)
        {
            Events.Add($"SendMessage:{text}");
            return NextSendStatus;
        }

        public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)
        {
            Events.Add("SetDecision");
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySetLabeledDecision(Release1LabeledDecision decision)
        {
            Events.Add($"SetLabeledDecision:{decision.Id}:{string.Join(",", decision.Options.Select(o => o.Label))}");
            Decision = new Release1ObservedDecision(
                decision.Id, decision.Options.Select(o => o.Label).ToArray(), decision.Prompt);
            foreach (var option in decision.Options)
                Callbacks[option.Label] = option.OnChosen;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryClearDecision()
        {
            Events.Add("ClearDecision");
            Decision = null;
            Callbacks.Clear();
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1ObservedQuest? Quest { get; set; }
        public List<Release1DesiredQuest> AppliedQuests { get; } = new();
        public List<string> EndedQuestKeys { get; } = new();

        public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest)
        {
            Events.Add("ReadQuest");
            quest = Quest?.Key == key ? Quest : null;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest)
        {
            Events.Add("ApplyQuest");
            if (quest.Key != Chapter3QuestCopy.QuestKey)
                return Release1NativePresentationStatus.Rejected;
            AppliedQuests.Add(quest);
            Quest = new Release1ObservedQuest(quest.Key, quest.Entries.Select(e => e.State).ToArray());
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryEndQuest(string key)
        {
            Events.Add("EndQuest");
            EndedQuestKeys.Add(key);
            if (Quest?.Key == key)
                Quest = null;
            return Release1NativePresentationStatus.Succeeded;
        }

        public void OnPreLoad() => Events.Add("NativeOnPreLoad");

        public void OnSaveStart() => Events.Add("NativeOnSaveStart");
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("44444444-4444-4444-4444-444444444444"), 1, PlayerId, System.IO.Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3State? Stored { get; set; }

        public Chapter3StoreLoadResult Load() => Stored is null
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

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; }
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public bool Readable { get; set; } = true;
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return Readable;
        }

        public void RaiseDay() => DayPassed?.Invoke();

        public void RaiseWeek() => WeekPassed?.Invoke();
    }

    private sealed class FakeWallet : IChapter3Wallet
    {
        public int Balance { get; set; }
        public List<int> Debits { get; } = new();

        public Chapter3WalletDebit TryDebit(int amount)
        {
            Debits.Add(amount);
            if (amount > Balance)
                return Chapter3WalletDebit.Short;
            Balance -= amount;
            return Chapter3WalletDebit.Debited;
        }
    }

    private sealed class FakeAcquisition : IChapter3WarehouseAcquisition
    {
        public bool Owned { get; set; }
        public bool Acquirable { get; set; } = true;
        public bool AcquireSucceeds { get; set; } = true;

        public bool IsOwned() => Owned;

        public bool CanAcquire() => Acquirable && !Owned;

        public bool TryAcquire()
        {
            if (!AcquireSucceeds || !CanAcquire())
                return false;
            Owned = true;
            return true;
        }
    }
}
