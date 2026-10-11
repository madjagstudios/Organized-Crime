using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefProtectionServiceTests
{
    [Fact]
    public void Offer_fires_on_the_third_job_including_already_qualified_saves()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        Assert.False(harness.Record!.ProtectionOffered);

        harness.Jobs = 2;
        ProtectionHarness.Tick(harness);
        Assert.False(harness.Record!.ProtectionOffered);

        harness.Jobs = 3;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Record!.ProtectionOffered);
        var revision = harness.Record.Revision;

        ProtectionHarness.Tick(harness);
        Assert.Equal(revision, harness.Record!.Revision);

        using var qualified = ProtectionHarness.Create();
        qualified.Jobs = 5;
        qualified.Service.Update();
        ProtectionHarness.Tick(qualified);
        Assert.True(qualified.Record!.ProtectionOffered);
    }

    [Fact]
    public void Buy_is_refused_while_unpaid_counted_and_never_charged()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Arrest();
        ProtectionHarness.Tick(harness);
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        harness.Jobs = 3;
        harness.World.CashBalance = 100_000f;

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(1, harness.Record!.ProtectionSettleFirstRefusals);
        Assert.Equal(100_000f, harness.World.CashBalance);
        Assert.Equal(0, harness.Record.ProtectionPurchases);
        Assert.Equal(0, harness.World.DebitCalls);
    }

    [Fact]
    public void Buy_debits_thirty_thousand_and_covers_seven_days_from_today()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        harness.World.CashBalance = 100_000f;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Record!.ProtectionOffered);

        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);

        Assert.Equal(12, harness.Record!.ProtectedUntilDay);
        Assert.Equal(70_000f, harness.World.CashBalance);
        Assert.Equal(Release1ChiefState.Adopted, harness.Record.State);
        Assert.Equal(-30_000f, harness.World.LastDebitAmount);
        Assert.True(harness.Service.IsProtected());
    }

    [Fact]
    public void Extend_adds_seven_days_from_the_end_day()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);

        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);

        Assert.Equal(19, harness.Record!.ProtectedUntilDay);
        Assert.Equal(40_000f, harness.World.CashBalance);
    }

    [Fact]
    public void Buy_is_refused_at_the_banking_cap_without_charge()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);
        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        Assert.Equal(19, harness.Record!.ProtectedUntilDay);

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(40_000f, harness.World.CashBalance);
        Assert.Equal(2, harness.Record!.ProtectionPurchases);
        Assert.Equal(2, harness.World.DebitCalls);
    }

    [Fact]
    public void No_demand_while_covered_and_no_backdating_after_lapse()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);

        harness.Arrest(LocalPressureTier.Quiet, LocalPressureTier.Critical);
        ProtectionHarness.Tick(harness);
        ProtectionHarness.Tick(harness);

        Assert.Equal(Release1ChiefState.Adopted, harness.Record!.State);
        Assert.Equal(0, harness.Record.WatchLinesSent);
        Assert.Equal(0, harness.Record.LockdownAnnouncements);

        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.Null(harness.Record!.ProtectedUntilDay);
        Assert.Equal(1, harness.Record.ProtectionLapseNotices);
        ProtectionHarness.Tick(harness);
        ProtectionHarness.Tick(harness);

        Assert.Equal(Release1ChiefState.Adopted, harness.Record!.State);
        Assert.Equal(0, harness.Record.WatchLinesSent);
    }

    [Fact]
    public void Day_before_warning_fires_once_per_cover_period()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);

        harness.World.TotalMinutes = 11 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.Equal(12, harness.Record!.ProtectionWarnedForUntilDay);
        var revision = harness.Record.Revision;

        ProtectionHarness.Tick(harness);
        Assert.Equal(revision, harness.Record!.Revision);

        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        harness.World.TotalMinutes = 18 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.Equal(19, harness.Record!.ProtectionWarnedForUntilDay);
    }

    [Fact]
    public void Lapse_clears_cover_counts_once_and_leaves_no_demand()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);

        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);

        Assert.Null(harness.Record!.ProtectedUntilDay);
        Assert.Null(harness.Record.ProtectionWarnedForUntilDay);
        Assert.Equal(1, harness.Record.ProtectionLapseNotices);
        Assert.Equal(Release1ChiefState.Adopted, harness.Record.State);
        Assert.False(harness.Service.IsProtected());

        ProtectionHarness.Tick(harness);
        Assert.Equal(1, harness.Record!.ProtectionLapseNotices);
    }

    [Fact]
    public void Arrest_after_lapse_opens_a_normal_demand()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);
        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);

        harness.Arrest();
        ProtectionHarness.Tick(harness);

        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record.DemandRound);
    }

    [Fact]
    public void Late_fee_does_not_run_while_covered()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);

        harness.World.TotalMinutes = 11 * 1440d;
        ProtectionHarness.Tick(harness);

        Assert.Equal(0, harness.Record!.LateFeesCharged);
        Assert.Equal(Release1ChiefState.Adopted, harness.Record.State);
    }

    [Fact]
    public void Settled_record_can_buy_after_paying_its_demand()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Arrest();
        ProtectionHarness.Tick(harness);
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        ProtectionHarness.Tick(harness);
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);

        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Record!.ProtectionOffered);
        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);

        Assert.Equal(12, harness.Record!.ProtectedUntilDay);
        Assert.Equal(Release1ChiefState.Settled, harness.Record.State);
        Assert.Equal(45_000f, harness.World.CashBalance);
    }

    [Fact]
    public void Buy_requires_the_offer()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(100_000f, harness.World.CashBalance);
        Assert.Equal(0, harness.Record!.ProtectionPurchases);
    }

    [Fact]
    public void Buy_fails_closed_when_the_clock_is_unreadable()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Jobs = 3;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Record!.ProtectionOffered);
        harness.World.CashBalance = 100_000f;
        harness.World.TotalMinutes = null;

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(100_000f, harness.World.CashBalance);
        Assert.Equal(0, harness.Record!.ProtectionPurchases);
        Assert.False(harness.Service.IsProtected());
    }

    [Fact]
    public void Short_cash_records_a_shortfall_and_charges_nothing()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        ProtectionHarness.Tick(harness);
        harness.World.CashBalance = 10_000f;

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(20_000f, harness.Record!.LastShortfallNoticed);
        Assert.Equal(10_000f, harness.World.CashBalance);
        Assert.Equal(0, harness.World.DebitCalls);
    }

    [Fact]
    public void Unavailable_debit_hands_the_record_back_for_a_fresh_tap()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        harness.World.CashBalance = 100_000f;
        ProtectionHarness.Tick(harness);
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Unavailable;

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(Release1ChiefState.Adopted, harness.Record!.State);
        Assert.Equal(100_000f, harness.World.CashBalance);

        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        Assert.Equal(12, harness.Record!.ProtectedUntilDay);
        Assert.Equal(70_000f, harness.World.CashBalance);
    }

    [Fact]
    public void Ambiguous_debit_blocks_the_purchase_and_counts_its_own_notice()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        harness.World.CashBalance = 100_000f;
        ProtectionHarness.Tick(harness);
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Ambiguous;

        Assert.False(harness.Service.TryBuyProtection());

        Assert.Equal(Release1ChiefState.PayingProtection, harness.Record!.State);
        Assert.Equal(1, harness.Record.ProtectionBlockedNotices);
        Assert.Equal(0, harness.Record.PaymentBlockedNotices);
        Assert.False(harness.Service.TryBuyProtection());
    }

    [Fact]
    public void Ambiguous_debit_records_the_blocked_protection_notice_despite_zero_round()
    {
        // Regression: PayingProtection with DemandRound 0 used to fail the Chief record
        // invariant inside TrySetChiefRecord, silently dropping the blocked-notice write.
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();
        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        harness.World.CashBalance = 100_000f;
        ProtectionHarness.Tick(harness);
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Ambiguous;

        Assert.False(harness.Service.TryBuyProtection());

        var effect = harness.Story.State!.NativeEffects.Single(e => e.EffectId.StartsWith("chief-campbell-protection-v1-"));
        Assert.True(effect.ExecutionBlocked);
        Assert.Equal(1, harness.Record!.ProtectionBlockedNotices);
    }

    [Fact]
    public void Checkpoints_are_disabled_once_per_cover_period()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);

        Assert.Equal(1, harness.Checkpoints.DisableCalls);
        ProtectionHarness.Tick(harness);
        ProtectionHarness.Tick(harness);
        Assert.Equal(1, harness.Checkpoints.DisableCalls);

        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        ProtectionHarness.Tick(harness);
        Assert.Equal(2, harness.Checkpoints.DisableCalls);
    }

    [Fact]
    public void Checkpoint_disable_failure_retries_on_the_next_pass()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.FailNext = true;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);

        Assert.Equal(1, harness.Checkpoints.DisableCalls);
        ProtectionHarness.Tick(harness);
        Assert.Equal(2, harness.Checkpoints.DisableCalls);
        ProtectionHarness.Tick(harness);
        Assert.Equal(2, harness.Checkpoints.DisableCalls);
    }

    [Fact]
    public void Cover_start_snapshots_enabled_checkpoints_before_disabling()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);

        Assert.Equal(5, harness.Record!.ProtectionCoverStartCheckpoints);
        Assert.Equal(new[] { "read", "disable" }, harness.Checkpoints.CallOrder);
    }

    [Fact]
    public void Lapse_restores_exactly_the_snapshotted_locations_once()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);
        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);

        Assert.Equal(1, harness.Checkpoints.RestoreCalls);
        Assert.Equal(5, harness.Checkpoints.LastRestoredMask);
        Assert.Equal(5, harness.Checkpoints.EnabledMask);
        Assert.Null(harness.Record!.ProtectionCoverStartCheckpoints);
        ProtectionHarness.Tick(harness);
        Assert.Equal(1, harness.Checkpoints.RestoreCalls);
    }

    [Fact]
    public void Restore_skips_locations_the_schedule_already_turned_back_on()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);
        harness.Checkpoints.EnabledMask = 1;
        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);

        Assert.Equal(4, harness.Checkpoints.LastRestoredMask);
        Assert.Equal(5, harness.Checkpoints.EnabledMask);
    }

    [Fact]
    public void Lapse_with_unreadable_world_consumes_the_snapshot_without_restoring()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);
        harness.Checkpoints.FailReadNext = true;
        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);

        Assert.Equal(0, harness.Checkpoints.RestoreCalls);
        Assert.Null(harness.Record!.ProtectionCoverStartCheckpoints);
    }

    [Fact]
    public void ReadProtectionSnapshot_reports_offer_until_and_cover()
    {
        using var harness = ProtectionHarness.Create();
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);

        var covered = harness.Service.ReadProtectionSnapshot();

        Assert.True(covered.Offered);
        Assert.Equal(12, covered.ProtectedUntilDay);
        Assert.True(covered.CoveredNow);

        harness.World.TotalMinutes = 12 * 1440d;
        ProtectionHarness.Tick(harness);

        var lapsed = harness.Service.ReadProtectionSnapshot();

        Assert.True(lapsed.Offered);
        Assert.Null(lapsed.ProtectedUntilDay);
        Assert.False(lapsed.CoveredNow);
    }

    [Fact]
    public void Full_arc_from_third_job_to_lapse()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        harness.Service.Update();
        Assert.False(harness.Record!.ProtectionOffered);

        harness.Jobs = 3;
        harness.World.TotalMinutes = 5 * 1440d;
        harness.World.CashBalance = 100_000f;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Record!.ProtectionOffered);

        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Service.IsProtected());
        Assert.Equal(12, harness.Record!.ProtectedUntilDay);
        Assert.Equal(5, harness.Record!.ProtectionCoverStartCheckpoints);

        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        ProtectionHarness.Tick(harness);
        Assert.Equal(19, harness.Record!.ProtectedUntilDay);

        Assert.False(harness.Service.TryBuyProtection());
        Assert.Equal(19, harness.Record!.ProtectedUntilDay);

        harness.World.TotalMinutes = 18 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.Equal(19, harness.Record!.ProtectionWarnedForUntilDay);
        ProtectionHarness.Tick(harness);
        Assert.Equal(19, harness.Record!.ProtectionWarnedForUntilDay);

        harness.World.TotalMinutes = 19 * 1440d;
        ProtectionHarness.Tick(harness);
        Assert.Null(harness.Record!.ProtectedUntilDay);
        Assert.Equal(1, harness.Record!.ProtectionLapseNotices);
        Assert.Equal(5, harness.Checkpoints.LastRestoredMask);
        Assert.Equal(Release1ChiefState.Adopted, harness.Record!.State);
    }

    [Fact]
    public void Qa_offer_sets_the_flag_without_any_jobs()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();

        var result = harness.Service.QaProtectionOffer();

        Assert.True(result.Ok);
        Assert.True(harness.Record!.ProtectionOffered);
    }

    [Fact]
    public void Qa_lapse_ends_cover_on_the_next_pass()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);

        var result = harness.Service.QaProtectionLapse();

        Assert.True(result.Ok);
        Assert.Equal(5, harness.Record!.ProtectedUntilDay);
        ProtectionHarness.Tick(harness);
        Assert.Null(harness.Record!.ProtectedUntilDay);
        Assert.Equal(1, harness.Checkpoints.RestoreCalls);
        Assert.Equal(5, harness.Checkpoints.LastRestoredMask);
    }

    [Fact]
    public void Qa_lapse_refuses_while_uncovered()
    {
        using var harness = ProtectionHarness.Create();
        harness.Service.Update();

        var result = harness.Service.QaProtectionLapse();

        Assert.False(result.Ok);
    }

    [Fact]
    public void Reload_mid_cover_keeps_the_snapshot_and_redisables()
    {
        using var harness = ProtectionHarness.Create();
        harness.Checkpoints.EnabledMask = 5;
        Cover(harness, today: 5);
        ProtectionHarness.Tick(harness);
        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();
        ProtectionHarness.Tick(harness);

        Assert.Equal(5, harness.Record!.ProtectionCoverStartCheckpoints);
        Assert.Equal(2, harness.Checkpoints.DisableCalls);
        Assert.Equal(1, harness.Checkpoints.ReadCalls);
    }

    private static void Cover(ProtectionHarness harness, int today)
    {
        harness.Service.Update();
        harness.Jobs = 3;
        harness.World.TotalMinutes = today * 1440d;
        harness.World.CashBalance = 100_000f;
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Service.TryBuyProtection());
        ProtectionHarness.Tick(harness);
        Assert.True(harness.Service.IsProtected());
    }

    private sealed class ProtectionHarness : IDisposable
    {
        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyDepositTests.FakeContext Context { get; }
        public ProtectionFakeWorld World { get; }
        public ProtectionFakeCheckpoints Checkpoints { get; }
        public Release1ChiefTierObserver Observer { get; }
        public ProtectionLedger Ledger { get; }
        public Release1ChiefService Service { get; }
        private readonly JobsBox _jobs;
        public int Jobs { get => _jobs.Count; set => _jobs.Count = value; }

        public string PlayerId => Context.Snapshot.PlayerId;
        public Release1ChiefRecord? Record => Story.State?.ChiefRecord;

        private ProtectionHarness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyDepositTests.FakeContext context,
            ProtectionFakeWorld world,
            ProtectionFakeCheckpoints checkpoints,
            Release1ChiefTierObserver observer,
            ProtectionLedger ledger,
            Release1ChiefService service,
            JobsBox jobs)
        {
            Story = story;
            Context = context;
            World = world;
            Checkpoints = checkpoints;
            Observer = observer;
            Ledger = ledger;
            Service = service;
            _jobs = jobs;
        }

        public void Arrest(LocalPressureTier previousTier = LocalPressureTier.Quiet, LocalPressureTier currentTier = LocalPressureTier.Quiet) =>
            Observer.Publish(new(
                Context.Snapshot.SessionEpoch, Context.Snapshot.LoadEpoch, PlayerId, null,
                $"arrest-{Guid.NewGuid()}", previousTier, currentTier, null, null));

        public static ProtectionHarness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            const string receipt = "chief-protection-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var world = new ProtectionFakeWorld();
            var checkpoints = new ProtectionFakeCheckpoints();
            var observer = new Release1ChiefTierObserver();
            var ledger = new ProtectionLedger
            {
                SessionEpoch = context.Snapshot.SessionEpoch,
                LoadEpoch = context.Snapshot.LoadEpoch,
                State = LocalPressureState.Quiet(context.Snapshot.PlayerId)
            };
            var jobs = new JobsBox();
            var service = new Release1ChiefService(
                story, world, observer, ledger.Read, ledger.Wipe, ledger.Epochs, LocalPressureProfile.Moderate,
                () => jobs.Count, checkpoints);
            return new ProtectionHarness(story, context, world, checkpoints, observer, ledger, service, jobs);
        }

        private sealed class JobsBox
        {
            public int Count { get; set; }
        }

        public static void Tick(ProtectionHarness harness)
        {
            harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
            harness.Service.Update();
        }

        public void Dispose()
        {
            Service.Dispose();
            Story.Dispose();
        }
    }

    private sealed class ProtectionFakeWorld : Release1ChiefWorldStub
    {
        public float CashBalance { get; set; } = 100_000f;
        public double? TotalMinutes { get; set; } = 0d;
        public int DebitCalls { get; private set; }
        public float? LastDebitAmount { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus DebitResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;

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

        public override Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = CashBalance;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
        {
            DebitCalls++;
            LastDebitAmount = amount;
            if (DebitResult == Release1SmallCourtesyWorldMutationStatus.Succeeded) CashBalance += amount;
            return DebitResult;
        }
    }

    private sealed class ProtectionFakeCheckpoints : IRelease1ProtectionCheckpointWorld
    {
        public int DisableCalls { get; private set; }
        public bool FailNext { get; set; }
        public int EnabledMask { get; set; } = Release1ProtectionCheckpoints.MaskAll;
        public int ReadCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int LastRestoredMask { get; private set; }
        public bool FailReadNext { get; set; }
        public readonly List<string> CallOrder = new();

        public bool TryDisableAllCheckpoints()
        {
            DisableCalls++;
            CallOrder.Add("disable");
            if (FailNext)
            {
                FailNext = false;
                return false;
            }
            EnabledMask = 0;
            return true;
        }

        public bool TryReadEnabled(out int mask)
        {
            ReadCalls++;
            CallOrder.Add("read");
            if (FailReadNext)
            {
                FailReadNext = false;
                mask = 0;
                return false;
            }
            mask = EnabledMask;
            return true;
        }

        public bool TryRestoreEnabled(int mask)
        {
            RestoreCalls++;
            LastRestoredMask = mask;
            CallOrder.Add("restore");
            EnabledMask |= mask & Release1ProtectionCheckpoints.MaskAll;
            return true;
        }
    }

    private sealed class ProtectionLedger
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
}
