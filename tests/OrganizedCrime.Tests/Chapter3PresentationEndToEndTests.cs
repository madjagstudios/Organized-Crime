using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

/// <summary>
/// End-to-end presentation scenarios through the real Chapter 3 service and
/// projector, with a native fake that behaves like the game in the two ways
/// that matter: a sent decision prompt counts as a visible message, and a tap
/// clears the native buttons before the callback runs. Each test asserts the
/// exact visible transcript: every subject text must reach the player once.
/// </summary>
public sealed class Chapter3PresentationEndToEndTests
{
    private const string PlayerId = "76561190000000001";

    private const string Offer1Text = "Got work if you want it: a dead drop delivery. Pays $17,000. You get 2 days once you say yes. Tell me by tomorrow, don't make me fucking chase you.";
    private const string Offer2Text = "Got work if you want it: a package recovery. Pays $15,500. You get 2 days once you say yes. Tell me by tomorrow, don't make me fucking chase you.";
    private const string MakeGoodOpenedText = "That dead drop delivery went to shit. The family wants $17,000 within 2 days. Don't make them ask twice.";
    private const string WarehouseOfferText = "You've been pulling your weight. There's a warehouse on the docks, right next to the Docks Warehouse. $300,000 and it's fucking yours. No rush, this one stays open, and I'll check in every week.";
    private const string WarehouseBoughtText = "Keys are yours. Place'll be ready in the morning. Welcome to the fucking big leagues. The family takes $30,000 a week, and don't make me chase it.";
    private const string WarehouseBuiltText = "Your place is ready. Go take a look, and don't fuck it up.";
    private const string BuyWarehouseLabel = "Buy the warehouse ($300,000)";

    [Fact]
    public void Offer_accept_fail_overdue_pay_says_each_thing_once()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        projector.Reconcile();
        harness.Tap("Accept");
        Assert.True(harness.Service.QaFailActiveJob().Ok);
        projector.Reconcile();
        harness.TickTo(12);
        projector.Reconcile();
        harness.TickTo(13);
        projector.Reconcile();
        transcript.Add("DAY 14");
        harness.TickTo(14);
        projector.Reconcile();
        harness.Tap("Pay $17,000");
        projector.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {Offer1Text} [Accept][Decline]",
                "TAP Accept",
                "Good. I'll send the details.",
                $"[prompt] {MakeGoodOpenedText} [Pay $17,000]",
                "DAY 14",
                "You're late. Arthur's been told, and he is not a patient man.",
                $"[prompt] {Offer2Text} [Pay $17,000][Accept][Decline]",
                "TAP Pay $17,000",
                "Received.",
                "[prompt] Still need an answer on that job. [Accept][Decline]"
            },
            transcript);
    }

    [Fact]
    public void Overdue_with_no_other_change_sends_the_text_once_and_keeps_the_buttons()
    {
        var harness = new Harness();
        harness.SeedOverdueScenario();
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        projector.Reconcile();
        transcript.Add("DAY 12");
        harness.TickTo(12);
        projector.Reconcile();
        transcript.Add("DAY 13");
        harness.TickTo(13);
        projector.Reconcile();
        projector.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {MakeGoodOpenedText} [Pay $17,000]",
                "DAY 12",
                "DAY 13",
                "You're late. Arthur's been told, and he is not a patient man."
            },
            transcript);
        Assert.Single(harness.Native.Events, e => e.StartsWith("SetLabeledDecision:", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
    }

    [Fact]
    public void Short_pay_says_youre_short_then_reprompts_and_nothing_else()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        projector.Reconcile();
        harness.Tap("Accept");
        Assert.True(harness.Service.QaFailActiveJob().Ok);
        projector.Reconcile();
        harness.Wallet.Balance = 0;
        harness.Tap("Pay $17,000");
        projector.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {Offer1Text} [Accept][Decline]",
                "TAP Accept",
                "Good. I'll send the details.",
                $"[prompt] {MakeGoodOpenedText} [Pay $17,000]",
                "TAP Pay $17,000",
                "You're short. Don't waste my fucking time.",
                "[prompt] Still waiting on that fucking money. [Pay $17,000]"
            },
            transcript);
    }

    [Fact]
    public void Reload_rebinds_matching_labels_without_resending()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        projector.Reconcile();
        harness.Native.Decision = new Release1ObservedDecision(
            null, new[] { "Accept", "Decline" }, Chapter3NellCopy.AnswerRepromptText);
        harness.Native.Callbacks.Clear();
        harness.Native.Events.Clear();
        var shown = transcript.Count;

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == "BindExisting");
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
        Assert.Equal(Chapter3NellCopy.AnswerRepromptText, harness.Native.LastSetPrompt);
        Assert.Equal(shown, transcript.Count);

        harness.Tap("Accept");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal("Good. I'll send the details.", transcript[^1]);
    }

    [Fact]
    public void DeadDrop_then_recovery_loop_says_each_thing_once_and_never_repays()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        projector.Reconcile();
        harness.Tap("Accept");
        projector.Reconcile();

        var applied = Assert.Single(harness.Native.AppliedQuests);
        Assert.Equal(Chapter3QuestCopy.QuestKey, applied.Key);
        var questEntry = Assert.Single(applied.Entries);
        Assert.Equal("Drop 2 bricks of Rich at Drop A", questEntry.Text);
        Assert.Equal(new Release1WorldPoint(1f, 2f, 3f), questEntry.Marker);

        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");
        projector.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {Offer1Text} [Accept][Decline]",
                "TAP Accept",
                "Good. 2 bricks of Rich at Drop A. $17,000 when they land. Don't fuck it up.",
                "They landed. $17,000's yours. Not bad."
            },
            transcript);
        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, harness.Native.EndedQuestKeys);
        Assert.Null(harness.Native.Quest);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        var reloaded = new Harness();
        reloaded.Repository.Stored = harness.Repository.Stored;
        reloaded.Service.DeadDropWorld = harness.World;
        reloaded.Service.Wallet = new Chapter3Wallet(harness.World);
        reloaded.Activate();
        var projector2 = reloaded.CreateProjector();
        projector2.Reconcile();

        Assert.Empty(reloaded.Native.Transcript);
        Assert.Single(harness.World.CashChanges);
        Assert.Equal(17_000f, harness.World.CashBalance);
        Assert.Equal(1, reloaded.Service.State!.JobBoard.SuccessfulJobs);

        // The loop repeats with the second archetype: the recovery freezes a
        // pair, stages, pays on handoff, and says each thing once.
        harness.World.SeedDrops(
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "description", 1d, 2d, 3d, true),
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "description", 4d, 5d, 6d, true));
        reloaded.TickTo(12);
        reloaded.TickTo(13);
        reloaded.TickTo(14);
        projector2.Reconcile();
        reloaded.Tap("Accept");
        projector2.Reconcile();

        var recovery = reloaded.Service.State!.JobBoard.ActiveJob!.Recovery!;
        Assert.True(recovery.Staged);
        var staged = Assert.Single(harness.World.Inserts);
        Assert.Equal(recovery.Pickup.Guid, staged.DropGuid);
        Assert.Equal(recovery.BrickCount, staged.Quantity);

        var applied2 = Assert.Single(reloaded.Native.AppliedQuests);
        Assert.Equal(Chapter3QuestCopy.QuestKey, applied2.Key);
        Assert.Equal(2, applied2.Entries.Count);
        Assert.Equal($"Pick up the package at {recovery.Pickup.Name}", applied2.Entries[0].Text);
        Assert.Equal(Release1DesiredEntryState.Active, applied2.Entries[0].State);
        Assert.Equal(
            new Release1WorldPoint((float)recovery.Pickup.X, (float)recovery.Pickup.Y, (float)recovery.Pickup.Z),
            applied2.Entries[0].Marker);
        Assert.Equal($"Bring it to {recovery.Handoff.Name}", applied2.Entries[1].Text);
        Assert.Equal(Release1DesiredEntryState.Active, applied2.Entries[1].State);
        Assert.Equal(
            new Release1WorldPoint((float)recovery.Handoff.X, (float)recovery.Handoff.Y, (float)recovery.Handoff.Z),
            applied2.Entries[1].Marker);

        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(recovery.StagedSlotIndex, "rich", "brick", 0));
        harness.World.FireDropClosed(recovery.Pickup.Guid);
        projector2.Reconcile();

        Assert.Equal(2, reloaded.Native.AppliedQuests.Count);
        var custodyApplied = reloaded.Native.AppliedQuests[1];
        Assert.Equal(Release1DesiredEntryState.Complete, custodyApplied.Entries[0].State);
        Assert.Equal(Release1DesiredEntryState.Active, custodyApplied.Entries[1].State);
        Assert.NotNull(reloaded.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.World.CashChanges);
        Assert.Equal(
            new[]
            {
                $"[prompt] {Offer2Text} [Accept][Decline]",
                "TAP Accept",
                $"Some idiot left a package at {recovery.Pickup.Name}: {recovery.BrickCount} bricks of Rich. Get it to {recovery.Handoff.Name}. $15,500 when it lands.",
                $"You've got it. Now get it to {recovery.Handoff.Name}, and don't get fucking cute."
            },
            reloaded.Native.Transcript);

        harness.World.SeedSlots(recovery.Handoff.Guid, Slot(0, "rich", "brick", recovery.BrickCount));
        harness.World.FireDropClosed(recovery.Handoff.Guid);
        projector2.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {Offer2Text} [Accept][Decline]",
                "TAP Accept",
                $"Some idiot left a package at {recovery.Pickup.Name}: {recovery.BrickCount} bricks of Rich. Get it to {recovery.Handoff.Name}. $15,500 when it lands.",
                $"You've got it. Now get it to {recovery.Handoff.Name}, and don't get fucking cute.",
                "It landed. $15,500's yours."
            },
            reloaded.Native.Transcript);
        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, reloaded.Native.EndedQuestKeys);
        Assert.Null(reloaded.Native.Quest);
        Assert.Null(reloaded.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(2, harness.World.CashChanges.Count);
        Assert.Equal(32_500f, harness.World.CashBalance);
        Assert.Equal(2, reloaded.Service.State.JobBoard.SuccessfulJobs);
    }

    [Fact]
    public void DeadDrop_reload_mid_job_then_deliver_completes()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        var projector = harness.CreateProjector();

        projector.Reconcile();
        harness.Tap("Accept");
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();

        var reloaded = new Harness();
        reloaded.Repository.Stored = harness.Repository.Stored;
        reloaded.Service.DeadDropWorld = harness.World;
        reloaded.Service.Wallet = new Chapter3Wallet(harness.World);
        reloaded.Activate();
        var projector2 = reloaded.CreateProjector();
        var transcript = reloaded.Native.Transcript;
        projector2.Reconcile();

        Assert.Empty(transcript);
        Assert.Single(reloaded.Native.AppliedQuests);

        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");
        projector2.Reconcile();

        Assert.Equal(new[] { "They landed. $17,000's yours. Not bad." }, transcript);
        Assert.Single(harness.World.CashChanges);
        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, reloaded.Native.EndedQuestKeys);
    }

    [Fact]
    public void Emergency_supply_loop_says_each_thing_once_and_pays_nothing()
    {
        var harness = new Harness();
        harness.WireDeadDropWorld();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        var offer = harness.Service.State!.JobBoard.OpenOffer!;
        var bricks = Chapter3JobTuning.BricksForReward(offer.Reward);
        var product = offer.ProductName!;
        var offerText = $"The family needs {bricks} bricks of {product}, and we don't make that shit ourselves. " +
            "No pay on this one. It comes back to you, trust me. You get 12 hours once you say yes.";

        projector.Reconcile();
        harness.Tap("Accept");
        projector.Reconcile();

        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var applied = Assert.Single(harness.Native.AppliedQuests);
        Assert.Equal(Chapter3QuestCopy.QuestKey, applied.Key);
        var questEntry = Assert.Single(applied.Entries);
        Assert.Equal($"Drop {bricks} bricks of {product} at Drop A", questEntry.Text);
        Assert.Equal(new Release1WorldPoint(1f, 2f, 3f), questEntry.Marker);

        harness.World.SeedSlots("drop-a", Slot(0, job.Delivery!.ProductId, "brick", bricks));
        harness.World.FireDropClosed("drop-a");
        projector.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {offerText} [Accept][Decline]",
                "TAP Accept",
                $"{bricks} bricks of {product} at Drop A. Twelve hours. Move your ass.",
                "Got it. The family won't forget that."
            },
            transcript);
        Assert.Equal(new[] { Chapter3QuestCopy.QuestKey }, harness.Native.EndedQuestKeys);
        Assert.Null(harness.Native.Quest);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
        Assert.Equal(0f, harness.World.CashBalance);
        Assert.Equal(1, harness.Service.State.JobBoard.SuccessfulJobs);
        Assert.DoesNotContain(transcript, line => line.Contains("$"));
    }

    private static void SeedFreezableWorld(DeadDropFakeWorld world)
    {
        world.SeedProducts(
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 3_000d, true),
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true));
        world.SeedDrops(new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "description", 1d, 2d, 3d, true));
    }

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);

    [Fact]
    public void Reload_with_unreceipted_scope_delivers_the_subject_once_then_binds()
    {
        var harness = new Harness();
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        // Nothing was ever delivered this session, but an earlier session left
        // the offer buttons natively restored with their subject showing.
        harness.Native.Decision = new Release1ObservedDecision(
            null, new[] { "Accept", "Decline" }, Offer1Text);
        projector.Reconcile();
        projector.Reconcile();

        Assert.Equal(new[] { Offer1Text }, transcript);
        Assert.Contains(
            harness.Service.State!.PresentationReceipts,
            receipt => receipt.CorrelationId == "oc11-ch3/v1/76561190000000001/offer-1");
        Assert.Contains(harness.Native.Events, e => e == "BindExisting");
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
    }

    [Fact]
    public void Warehouse_offer_buy_sleep_then_reload_says_each_thing_once()
    {
        var harness = new Harness();
        harness.SeedWarehouseEligibleScenario();
        harness.Clock.ElapsedDays = 18;
        harness.Wallet.Balance = 1_000_000;
        var acquisition = new FakeAcquisition();
        harness.Service.WarehouseAcquisition = acquisition;
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        var offer = harness.Service.State!.WarehouseOffer;
        Assert.NotNull(offer);
        Assert.Equal(18, offer!.OpenedDay);
        Assert.False(offer.Purchased);

        projector.Reconcile();
        harness.Tap(BuyWarehouseLabel);
        projector.Reconcile();

        // The tap takes the money and records the purchase, but nothing
        // spawns: the world build waits for the sleep screen.
        Assert.Equal(
            new[]
            {
                $"[prompt] {WarehouseOfferText} [{BuyWarehouseLabel}]",
                $"TAP {BuyWarehouseLabel}",
                WarehouseBoughtText
            },
            transcript);
        Assert.Equal(new[] { 300_000 }, harness.Wallet.Debits);
        Assert.Equal(700_000, harness.Wallet.Balance);
        Assert.False(acquisition.Owned);
        Assert.Equal(0, acquisition.AcquireCalls);
        Assert.True(harness.Service.HasPendingWarehouseBuild);
        Assert.True(harness.Service.State!.WarehouseOffer!.Purchased);
        Assert.Equal(18, harness.Service.State.WarehouseOffer.PurchasedDay);

        harness.Service.NotifySleepStarted();
        harness.Service.PollPendingWarehouseBuild(() => true);
        projector.Reconcile();

        Assert.True(acquisition.Owned);
        Assert.Equal(1, acquisition.AcquireCalls);
        Assert.False(harness.Service.HasPendingWarehouseBuild);
        Assert.Equal(
            new[]
            {
                $"[prompt] {WarehouseOfferText} [{BuyWarehouseLabel}]",
                $"TAP {BuyWarehouseLabel}",
                WarehouseBoughtText,
                WarehouseBuiltText
            },
            transcript);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        var reloaded = new Harness();
        reloaded.Repository.Stored = harness.Repository.Stored;
        reloaded.Service.WarehouseAcquisition = new FakeAcquisition { Owned = true };
        reloaded.Clock.ElapsedDays = 18;
        reloaded.Activate();
        reloaded.Service.PollPendingWarehouseBuild(() => false);
        var projector2 = reloaded.CreateProjector();
        projector2.Reconcile();

        Assert.Empty(reloaded.Native.Transcript);
        Assert.Empty(reloaded.Wallet.Debits);
        Assert.True(reloaded.Service.State!.WarehouseOffer!.Purchased);
        Assert.Equal(18, reloaded.Service.State.WarehouseOffer.PurchasedDay);
    }

    [Fact]
    public void Warehouse_buy_then_reload_before_sleep_builds_during_load()
    {
        var harness = new Harness();
        harness.SeedWarehouseEligibleScenario();
        harness.Clock.ElapsedDays = 18;
        harness.Wallet.Balance = 1_000_000;
        harness.Service.WarehouseAcquisition = new FakeAcquisition();
        harness.Activate();
        var projector = harness.CreateProjector();
        var transcript = harness.Native.Transcript;

        projector.Reconcile();
        harness.Tap(BuyWarehouseLabel);
        projector.Reconcile();

        Assert.Equal(
            new[]
            {
                $"[prompt] {WarehouseOfferText} [{BuyWarehouseLabel}]",
                $"TAP {BuyWarehouseLabel}",
                WarehouseBoughtText
            },
            transcript);
        Assert.True(harness.Service.HasPendingWarehouseBuild);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        var reloaded = new Harness();
        reloaded.Repository.Stored = harness.Repository.Stored;
        var acquisition = new FakeAcquisition();
        reloaded.Service.WarehouseAcquisition = acquisition;
        reloaded.Clock.ElapsedDays = 18;
        reloaded.Activate();

        // The bought-but-unowned purchase is pending on load, and the load
        // poll builds it on the first acquirable frame, behind the load
        // screen: Nell's ready text is the only thing the reload says.
        Assert.True(reloaded.Service.HasPendingWarehouseBuild);
        reloaded.Service.PollPendingWarehouseBuild(() => false);

        Assert.True(acquisition.Owned);
        Assert.Equal(1, acquisition.AcquireCalls);
        Assert.False(reloaded.Service.HasPendingWarehouseBuild);

        var projector2 = reloaded.CreateProjector();
        projector2.Reconcile();

        Assert.Equal(new[] { WarehouseBuiltText }, reloaded.Native.Transcript);
        Assert.Empty(reloaded.Wallet.Debits);
        Assert.True(reloaded.Service.State!.WarehouseOffer!.Purchased);
    }

    private sealed class Harness
    {
        public GameNative Native { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = 11 };
        public FakeWallet Wallet { get; } = new() { Balance = 100_000 };
        public DeadDropFakeWorld World { get; } = new();
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.Wallet = Wallet;
        }

        /// <summary>
        /// Wires the dead-drop world and a real wallet over it. Opt-in per
        /// test: the pre-OC-121 transcripts accept with no world and stay
        /// unfrozen.
        /// </summary>
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

        public void TickTo(int elapsedDays)
        {
            Clock.ElapsedDays = elapsedDays;
            Service.OnDayPassed();
        }

        public void Tap(string label)
        {
            Native.Transcript.Add($"TAP {label}");
            Native.Tap(label);
        }

        public Chapter3PresentationProjector CreateProjector() =>
            new(Service, () => Service.GetPresentationPlan(false), Native,
                new Chapter3PresentationCommandRouter(Service), Logs.Add);

        /// <summary>
        /// A make-good opened on chapter day 0, due day 2, with the next offer
        /// held back by cooldown: ticking to chapter day 3 turns it overdue
        /// with no other change.
        /// </summary>
        public void SeedOverdueScenario()
        {
            var board = new Chapter3JobBoard(
                null, null, 10, 1, Chapter3JobType.DeadDrop,
                new[] { Chapter3JobType.DeadDrop }, 0, false, false, 0, 0, 2, 2);
            var ledger = new Chapter3MakeGoodLedger(
                new[]
                {
                    new Chapter3MakeGood(
                        1, new Chapter3FailedJobSource(1, Chapter3JobType.DeadDrop), 17_000, 0, 2,
                        Chapter3MakeGoodStatus.Open, Chapter3MakeGoodStage.None, null, null)
                },
                1, 2);
            Repository.Stored = new Chapter3State(PlayerId, Chapter3Phase.Active, 10, 11, 5, board, ledger);
        }

        /// <summary>
        /// Five successes on chapter day 17 with the job board on cooldown and
        /// the trust lines already receipted: the day-18 tick opens the
        /// warehouse offer with nothing else pending.
        /// </summary>
        public void SeedWarehouseEligibleScenario()
        {
            var board = new Chapter3JobBoard(
                null, null, 999, 17, Chapter3JobType.DeadDrop,
                new[] { Chapter3JobType.DeadDrop }, 0, false, false, 5, 0, 6, 6);
            var receipts = new[]
            {
                new Chapter3PresentationReceipt(Chapter3PresentationReceipt.Correlation(PlayerId, "trust-3"), 5),
                new Chapter3PresentationReceipt(Chapter3PresentationReceipt.Correlation(PlayerId, "trust-5"), 5)
            };
            Repository.Stored = new Chapter3State(
                PlayerId, Chapter3Phase.Active, 0, 17, 5, board, Chapter3MakeGoodLedger.Empty,
                Chapter3WeeklyCuts.Empty, receipts);
        }
    }

    /// <summary>
    /// A native fake that behaves like the game where it matters: a freshly
    /// sent decision prompt is a visible message, a tap clears the buttons
    /// before the callback runs, and setting labels that already show only
    /// rebinds the callbacks (the production bind rule) without showing
    /// anything.
    /// </summary>
    private sealed class GameNative : IRelease1NativePresentation
    {
        public List<string> Transcript { get; } = new();
        public List<string> Events { get; } = new();
        public Dictionary<string, Action> Callbacks { get; } = new(StringComparer.Ordinal);
        public Release1ObservedDecision? Decision { get; set; }
        public string? LastSetPrompt { get; private set; }

        public void Tap(string label)
        {
            var callback = Callbacks[label];
            Decision = null;
            Callbacks.Clear();
            callback();
        }

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
            Transcript.Add(text);
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)
        {
            Events.Add("SetDecision");
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySetLabeledDecision(Release1LabeledDecision decision)
        {
            Events.Add($"SetLabeledDecision:{decision.Id}:{string.Join(",", decision.Options.Select(o => o.Label))}");
            LastSetPrompt = decision.Prompt;
            var labels = decision.Options.Select(o => o.Label).ToArray();
            if (Decision is not null &&
                Release1NativePresentationSupport.ShouldBindExistingResponses(Decision.Labels, labels))
            {
                Events.Add("BindExisting");
                Decision = new Release1ObservedDecision(decision.Id, labels, Decision.Prompt);
            }
            else
            {
                Transcript.Add($"[prompt] {decision.Prompt} {string.Concat(labels.Select(label => $"[{label}]"))}");
                Decision = new Release1ObservedDecision(decision.Id, labels, decision.Prompt);
            }
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
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, PlayerId, System.IO.Path.GetTempPath());

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
        public int AcquireCalls { get; private set; }

        public bool IsOwned() => Owned;

        public bool CanAcquire() => Acquirable && !Owned;

        public bool TryAcquire()
        {
            AcquireCalls++;
            if (!CanAcquire())
                return false;
            Owned = true;
            return true;
        }
    }
}
