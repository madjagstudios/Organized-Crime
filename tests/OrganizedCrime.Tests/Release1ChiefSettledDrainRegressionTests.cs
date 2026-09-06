using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-73 settled drain fix regression. Reconstructs, as faithfully as the existing Chief fixtures
/// allow, the loaded shape of a real save that looped nonstop right after load on the settled drain
/// fix (2e6093f) even though the previous build (b5ed4df) never touched it: a Chief record Settled at
/// round three, an earlier round's cash effect already Committed, the current round's cash effect
/// Applied but not yet committed, every line the record has ever earned already receipted, and a
/// Local Pressure ledger that has since cooled to Noticed (well below Watched).
///
/// The loop has nothing to do with the commit sweep itself: it is
/// <see cref="Release1ChiefService.ReconcileLockdown"/>'s fresh engage branch, which never checked the
/// currently observed tier before deciding to engage, only that the record had ever been announced
/// (LockdownAnnouncements, a counter that never resets) and was currently unpaid. That branch used to
/// be unreachable for a record reopened out of Settled, because the settled drain defect this fix
/// closes kept ContinuePayment short circuiting DrainOne and ReconcileLockdown for as long as the
/// record stayed Settled, so an arrest could never even reopen the demand. Once the fix lets DrainOne
/// reopen a Settled record again, a record whose tier has long since cooled below Watched but whose
/// LockdownAnnouncements history is still nonzero re engages immediately, cools right back off on the
/// very next pass (minting a fresh CooledLifts receipt and a "Curfew is lifted" message), and repeats
/// forever: engage, cool, engage, cool, with no warning anywhere in the Melon log, since both the
/// native engage/release calls and Release1PresentationProjector's own receipt write are silent on
/// success.
/// </summary>
public sealed class Release1ChiefSettledDrainRegressionTests
{
    [Fact]
    public void A_settled_record_reopened_by_an_arrest_does_not_resend_its_lines_every_pass()
    {
        using var h = Harness.Create();
        h.Composition.Update(); // Adopt

        // Cycle A: a full payoff at round two that gets committed by a save, exactly the historical,
        // closed cash effect the real save's chief-campbell-cash-v1-r2-c4 entry represents.
        h.Arrest();
        h.Tick(); // Adopted + arrest -> DemandOpen round one
        Assert.True(h.Composition.Service.TryDecline());
        h.Tick();
        h.Arrest();
        h.Tick(); // Declined + arrest -> DemandOpen round two
        Assert.Equal(2, h.Record!.DemandRound);
        h.World.CashBalance = 100_000f;
        Assert.True(h.Composition.Service.TryPay());
        h.Tick(); // wipe and settle land on this pass
        Assert.Equal(Release1ChiefState.Settled, h.Record!.State);

        h.Story.OnSaveStart();
        h.Story.OnSaveComplete();
        h.Composition.OnSaveComplete();
        h.Tick(); // commits the round two effect now that the save covers it
        var historicalEffectId = h.Story.State!.NativeEffects.Single(e => e.Phase == Release1NativeEffectPhase.Committed).EffectId;

        // Cycle B: reopen, climb to round three, earn a watch line and a lockdown announcement,
        // engage it, then pay it off while engaged so the settle lifts it (PaidLifts), exactly the
        // real save's paidLifts 1, lockdownAnnouncements 1, lockdownEngaged false shape once it
        // settles again, with demandRound 3 and the round three cash effect still Applied.
        h.Arrest();
        h.Tick(); // Settled + arrest resets the ladder to round one
        Assert.Equal(1, h.Record!.DemandRound);
        Assert.True(h.Composition.Service.TryDecline());
        h.Tick();
        h.Arrest();
        h.Tick();
        Assert.Equal(2, h.Record!.DemandRound);
        Assert.True(h.Composition.Service.TryDecline());
        h.Tick();
        h.Arrest();
        h.Tick();
        Assert.Equal(3, h.Record!.DemandRound);

        SetLedgerHeat(h, 65); // Noticed -> Watched
        h.Arrest(LocalPressureTier.Noticed, LocalPressureTier.Watched);
        h.Tick(); // drains the arrest itself; already DemandOpen, so no ladder change
        h.Tick(); // drains RoseToWatched
        Assert.Equal(1, h.Record!.WatchLinesSent);

        SetLedgerHeat(h, 85); // Watched -> Critical
        h.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        h.Tick(); // drains the arrest itself
        h.Tick(); // drains RoseToCritical: announces this pass
        Assert.Equal(1, h.Record!.LockdownAnnouncements);
        h.Tick(); // engages, now that the announcement's own receipt exists
        Assert.True(h.Record!.LockdownEngaged);

        h.World.CashBalance = 100_000f;
        Assert.True(h.Composition.Service.TryPay());
        h.Tick(); // wipe, settle, and the paid lift all land on this pass
        Assert.Equal(Release1ChiefState.Settled, h.Record!.State);
        Assert.False(h.Record!.LockdownEngaged);
        Assert.Equal(1, h.Record!.PaidLifts);
        var appliedEffectId = h.Record!.NativeEffectIds[^1];
        Assert.Equal(Release1NativeEffectPhase.Applied, h.Story.State!.NativeEffects.Single(e => e.EffectId == appliedEffectId).Phase);

        // Save exactly here, with the round three payoff still Applied and uncommitted: the real save
        // the owner reported the loop on caught the record mid cycle the same way, with
        // LastPersistedRevision equal to the loaded revision.
        h.Story.OnSaveStart();
        h.Story.OnSaveComplete();
        h.Composition.OnSaveComplete();
        Assert.Equal(h.Story.State!.Revision, h.Story.LastPersistedRevision);

        // Reload: a fresh story runtime over the same repository, a fresh world, a fresh observer (an
        // empty queue) and a fresh native fake, with the Local Pressure ledger read back at its own
        // current value: heat 29, Known Offender true, tier Noticed, well below Watched, matching the
        // owner's own sidecar.
        using var restart = h.Restart();
        restart.Ledger.State = new LocalPressureState(29, true, null, null, null, restart.PlayerId, null, null, 0);
        restart.Composition.OnLoadComplete();
        Assert.Empty(restart.Native.SentMessages);
        Assert.Equal(Release1ChiefState.Settled, restart.Record!.State);
        Assert.Equal(3, restart.Record!.DemandRound);
        Assert.False(restart.Record!.LockdownEngaged);
        Assert.Equal(Release1NativeEffectPhase.Applied, restart.Story.State!.NativeEffects.Single(e => e.EffectId == appliedEffectId).Phase);
        Assert.Equal(Release1NativeEffectPhase.Committed, restart.Story.State!.NativeEffects.Single(e => e.EffectId == historicalEffectId).Phase);

        // Ten convergence passes plus projector reconciles, no event queued at all: nothing should be
        // sent, since every line this record has ever earned already carries a receipt from before the
        // reload.
        var baselineTrace = new System.Text.StringBuilder();
        for (var pass = 1; pass <= 10; pass++)
        {
            var unreceipted = UnreceiptedCorrelations(restart);
            restart.Tick();
            baselineTrace.AppendLine(
                $"pass {pass}: unreceipted before tick [{string.Join(", ", unreceipted)}]; sent so far {restart.Native.SentMessages.Count}; " +
                $"CooledLifts {restart.Record!.CooledLifts}; LockdownEngaged {restart.Record!.LockdownEngaged}.");
        }
        Assert.True(
            restart.Native.SentMessages.Count == 0,
            "Expected zero sends across ten convergence passes with an empty observer queue while Settled, got " +
            $"{restart.Native.SentMessages.Count}: [{string.Join(" | ", restart.Native.SentMessages)}].\n{baselineTrace}");

        // Now queue exactly one arrest and confirm it reopens the demand exactly once, with no
        // repeated Chief line anywhere in the next ten passes.
        restart.Arrest();
        var sentBeforeArrest = restart.Native.SentMessages.Count;
        var arrestTrace = new System.Text.StringBuilder();
        for (var pass = 1; pass <= 10; pass++)
        {
            var unreceipted = UnreceiptedCorrelations(restart);
            restart.Tick();
            arrestTrace.AppendLine(
                $"pass {pass}: unreceipted before tick [{string.Join(", ", unreceipted)}]; " +
                $"sent this scenario so far {restart.Native.SentMessages.Count - sentBeforeArrest}; " +
                $"CooledLifts {restart.Record!.CooledLifts}; LockdownEngaged {restart.Record!.LockdownEngaged}; " +
                $"EngageCalls {restart.World.EngageCalls}; ReleaseCalls {restart.World.ReleaseCalls}.");
        }

        var newSends = restart.Native.SentMessages.Skip(sentBeforeArrest).ToArray();
        Assert.True(
            newSends.Length == 1 && newSends[0] == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText),
            "Expected exactly one first contact decision presentation and no repeated sends once the arrest reopened " +
            $"the demand, got {newSends.Length}: [{string.Join(" | ", newSends)}].\n{arrestTrace}");
        Assert.Equal(Release1ChiefState.DemandOpen, restart.Record!.State);
        Assert.Equal(1, restart.Record!.DemandRound);
    }

    /// <summary>
    /// OC-73 settled drain fix, tightened. ReconcileLockdown's fresh engage branch used a Watched
    /// guard, not a Critical one: LockdownAnnouncements is minted only on a rising crossing into
    /// Critical (Release1ChiefLedger.Observe's RoseToCritical branch) and never resets, so a demand
    /// reopened after a payoff that only climbs back to Watched still carries a stale, already
    /// receipted announcement from an earlier cycle. The Watched guard let that stale receipt satisfy
    /// HasAnnouncementReceipt and engage the curfew with no announcement anywhere in this cycle,
    /// breaking the owner's rule that Chief Campbell must announce before he engages. This reproduces
    /// exactly that shape and confirms the tightened Critical guard holds it open: no engage while the
    /// observed tier sits at Watched, even with a nonzero LockdownAnnouncements and a receipt already
    /// on file.
    /// </summary>
    [Fact]
    public void A_reopened_demand_with_a_stale_announcement_does_not_engage_while_only_watched()
    {
        using var h = BuildReopenedDemandStaleAnnouncementAtWatchedHarness(out var engageCallsBeforeReopen, out var sentBeforeReopen);

        Assert.False(h.Record!.LockdownEngaged);
        Assert.Equal(0, h.World.EngageCalls - engageCallsBeforeReopen);
        Assert.Equal(1, h.Record!.LockdownAnnouncements); // still the stale value from the earlier cycle
        Assert.Equal(2, h.Record!.WatchLinesSent); // one from the earlier cycle, one from this reopened one

        var newSends = h.Native.SentMessages.Skip(sentBeforeReopen).ToArray();
        Assert.True(
            newSends.Length == 1 && newSends[0] == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.WatchListText),
            "Expected exactly one watch line and no engage while the reopened demand's tier only reached Watched, " +
            $"got {newSends.Length}: [{string.Join(" | ", newSends)}].");
    }

    /// <summary>
    /// The companion path to the test above, continued from the exact same reopened, Watched only
    /// shape: once the tier keeps climbing and actually crosses into Critical, Release1ChiefLedger.Observe
    /// mints a fresh announcement (LockdownAnnouncements 1 to 2) with its own new receipt, and only
    /// then does the tightened guard let the lockdown engage, on a later pass than the announcement,
    /// exactly once. This is the rule the fix exists to preserve: announce first, every cycle.
    /// </summary>
    [Fact]
    public void A_reopened_demand_that_climbs_back_to_critical_earns_a_fresh_announcement_before_engaging()
    {
        using var h = BuildReopenedDemandStaleAnnouncementAtWatchedHarness(out var engageCallsBeforeReopen, out _);
        var sentBeforeClimb = h.Native.SentMessages.Count;

        SetLedgerHeat(h, 85); // Watched -> Critical
        h.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        h.Tick(); // drains the arrest itself; the crossing is still queued behind it
        Assert.False(h.Record!.LockdownEngaged); // the stale chief-lockdown-1 receipt must not be reused here
        h.Tick(); // drains RoseToCritical: mints the fresh announcement this pass
        Assert.Equal(2, h.Record!.LockdownAnnouncements);
        Assert.False(h.Record!.LockdownEngaged); // not yet: the fresh receipt has not landed this pass

        h.Tick(); // engages, now that the fresh announcement's own receipt exists
        Assert.True(h.Record!.LockdownEngaged);
        Assert.Equal(1, h.World.EngageCalls - engageCallsBeforeReopen);

        var newSends = h.Native.SentMessages.Skip(sentBeforeClimb).ToArray();
        var announcementSends = newSends.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LockdownAnnouncementText));
        Assert.Equal(1, announcementSends);
    }

    /// <summary>
    /// Builds the stale announcement shape both tests above share: an earlier cycle climbs to Critical,
    /// earns a lockdown announcement, engages it, and pays it off (a paid lift, which wipes the Local
    /// Pressure ledger back to heat zero), then a fresh arrest reopens the demand and the tier climbs
    /// again but only as far as Watched. LockdownAnnouncements and its receipt are both left over from
    /// the earlier cycle when this returns; out parameters mark the engage call count and sent message
    /// count immediately after the reopen so callers can isolate what happens from there.
    /// </summary>
    private static Harness BuildReopenedDemandStaleAnnouncementAtWatchedHarness(out int engageCallsBeforeReopen, out int sentBeforeReopen)
    {
        var h = Harness.Create();
        h.Composition.Update(); // Adopt

        h.Arrest();
        h.Tick(); // Adopted + arrest -> DemandOpen round one

        SetLedgerHeat(h, 65); // Noticed -> Watched
        h.Arrest(LocalPressureTier.Noticed, LocalPressureTier.Watched);
        h.Tick(); // drains the arrest itself
        h.Tick(); // drains RoseToWatched
        Assert.Equal(1, h.Record!.WatchLinesSent);

        SetLedgerHeat(h, 85); // Watched -> Critical
        h.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        h.Tick(); // drains the arrest itself
        h.Tick(); // drains RoseToCritical: announces this pass
        Assert.Equal(1, h.Record!.LockdownAnnouncements);
        h.Tick(); // engages, now that the announcement's own receipt exists
        Assert.True(h.Record!.LockdownEngaged);
        Assert.Equal(1, h.World.EngageCalls);

        h.World.CashBalance = 100_000f;
        Assert.True(h.Composition.Service.TryPay());
        h.Tick(); // wipe, settle, and the paid lift all land on this pass
        Assert.Equal(Release1ChiefState.Settled, h.Record!.State);
        Assert.False(h.Record!.LockdownEngaged);
        Assert.Equal(1, h.Record!.PaidLifts);

        // Reopen: an arrest with no fresh tier crossing resets the ladder to round one. The stale
        // LockdownAnnouncements value (1) and its receipt both survive this reset untouched.
        h.Arrest();
        h.Tick();
        Assert.Equal(Release1ChiefState.DemandOpen, h.Record!.State);
        Assert.Equal(1, h.Record!.DemandRound);

        engageCallsBeforeReopen = h.World.EngageCalls;
        sentBeforeReopen = h.Native.SentMessages.Count;

        // Heat climbs again, this time only as far as Watched.
        SetLedgerHeat(h, 65); // -> Watched
        h.Arrest(LocalPressureTier.Noticed, LocalPressureTier.Watched);
        h.Tick(); // drains the arrest itself
        h.Tick(); // drains RoseToWatched
        return h;
    }

    /// <summary>The exact correlations Release1PresentationProjector.ReconcileMessages would try to send on the next pass.</summary>
    private static string[] UnreceiptedCorrelations(Harness restart)
    {
        var state = restart.Story.State;
        if (state is null) return Array.Empty<string>();
        return Release1ChiefPresentation.BuildPlan(state).Messages
            .Where(m => !state.PresentationReceipts.Any(r => r.CorrelationId == m.CorrelationId))
            .Select(m => m.CorrelationId)
            .ToArray();
    }

    private static void SetLedgerHeat(Harness h, int heat) =>
        h.Ledger.State = new LocalPressureState(
            heat, true, h.Ledger.State?.LastEvidenceGameTime, h.Ledger.State?.QuietGraceUntil, h.Ledger.State?.LastDecayEvaluation,
            h.PlayerId, h.Ledger.State?.Region, h.Ledger.State?.PropertyCode, (h.Ledger.State?.Revision ?? 0) + 1);

    private sealed class LedgerHarness
    {
        public LocalPressureState? State { get; set; }
        public int WipeCalls { get; private set; }
        public LocalPressureEvidenceWriteResult WipeResult { get; set; } = new(true, LocalPressureEvidenceWriteRejectReason.None, null, "wiped");
        public Guid SessionEpoch { get; set; }
        public long LoadEpoch { get; set; }

        public LocalPressureState? Read(string playerId) => State;

        /// <summary>Mirrors LocalPressureTransitions.ApplyRecordWipe: Local Heat to zero, Known Offender false.</summary>
        public LocalPressureEvidenceWriteResult Wipe(string playerId)
        {
            WipeCalls++;
            if (WipeResult.Accepted && State is not null)
                State = new LocalPressureState(
                    0, false, State.LastEvidenceGameTime, State.QuietGraceUntil, State.LastDecayEvaluation,
                    State.PlayerId, State.Region, State.PropertyCode, State.Revision + 1);
            return WipeResult;
        }

        public bool Epochs(out Guid sessionEpoch, out long loadEpoch)
        {
            sessionEpoch = SessionEpoch;
            loadEpoch = LoadEpoch;
            return true;
        }
    }

    /// <summary>Mirrors Release1ProductionOnlyWorld's own Chief-relevant subset exactly.</summary>
    private sealed class CompositionFakeWorld : Release1ProductionOnlyWorld
    {
        public float CashBalance { get; set; } = 100_000f;
        public double? TotalMinutes { get; set; } = 0d;
        public int DebitCalls { get; private set; }
        public float? LastDebitAmount { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus DebitResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;

        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) =>
            throw new NotSupportedException();

        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            if (TotalMinutes is { } minutes) { totalMinutes = minutes; return Release1SmallCourtesyWorldReadStatus.Ready; }
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

        public int EngageCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus EngageResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public Release1SmallCourtesyWorldMutationStatus ReleaseResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public string EngageReason { get; set; } = string.Empty;
        public string ReleaseReason { get; set; } = string.Empty;

        public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            EngageCalls++;
            reason = EngageReason;
            return EngageResult;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            ReleaseCalls++;
            reason = ReleaseReason;
            return ReleaseResult;
        }
    }

    /// <summary>
    /// A leaner native presentation fake than the projector's own (no quest support at all: quest
    /// methods throw, which doubles as a regression check that the Chief's projector never calls
    /// them, since it is constructed with reconcileQuests: false). Mirrors the real boundary's own
    /// TrySetDecision shape (bind existing vs send fresh) closely enough to exercise the "exactly
    /// once" guarantee the same way the real one does.
    /// </summary>
    private sealed class FakeNative : IRelease1NativePresentation
    {
        public List<string> SentMessages { get; } = new();
        public Release1ObservedDecision? ObservedDecision;
        public int SetDecisionCalls { get; private set; }
        public Action<Release1PresentationCommand>? LastOnChosen { get; private set; }

        public Release1NativePresentationStatus TryEnsureContact() => Release1NativePresentationStatus.Succeeded;

        public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts)
        {
            texts = SentMessages.ToArray();
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision)
        {
            decision = ObservedDecision;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySendMessage(string text)
        {
            SentMessages.Add(text);
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)
        {
            SetDecisionCalls++;
            var desiredLabels = decision.Options.Select(o => o.Label).ToArray();
            var existingLabels = ObservedDecision?.Labels ?? Array.Empty<string>();
            if (!Release1NativePresentationSupport.ShouldBindExistingResponses(existingLabels, desiredLabels))
                SentMessages.Add(decision.Prompt);
            ObservedDecision = new Release1ObservedDecision(decision.Id, desiredLabels, decision.Prompt);
            LastOnChosen = onChosen;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryClearDecision()
        {
            ObservedDecision = null;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest) =>
            throw new NotSupportedException("The Chief's own projector must never reach the catalog quest surface.");

        public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest) =>
            throw new NotSupportedException("The Chief's own projector must never reach the catalog quest surface.");

        public Release1NativePresentationStatus TryEndQuest(string key) =>
            throw new NotSupportedException("The Chief's own projector must never reach the catalog quest surface.");

        public void OnPreLoad() { }
        public void OnSaveStart() { }
    }

    private sealed class Harness : IDisposable
    {
        private Harness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyDepositTests.FakeContext context,
            Release1SmallCourtesyDepositTests.FakeRepository repository,
            CompositionFakeWorld world,
            Release1ChiefTierObserver observer,
            LedgerHarness ledger,
            FakeNative native,
            Release1ChiefComposition composition)
        {
            Story = story;
            Context = context;
            Repository = repository;
            World = world;
            Observer = observer;
            Ledger = ledger;
            Native = native;
            Composition = composition;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyDepositTests.FakeContext Context { get; }
        public Release1SmallCourtesyDepositTests.FakeRepository Repository { get; }
        public CompositionFakeWorld World { get; }
        public Release1ChiefTierObserver Observer { get; }
        public LedgerHarness Ledger { get; }
        public FakeNative Native { get; }
        public Release1ChiefComposition Composition { get; }

        public string PlayerId => Context.Snapshot.PlayerId;
        public Release1ChiefRecord? Record => Story.State?.ChiefRecord;

        public void Arrest(LocalPressureTier previousTier = LocalPressureTier.Quiet, LocalPressureTier currentTier = LocalPressureTier.Quiet) =>
            Observer.Publish(new(
                Context.Snapshot.SessionEpoch, Context.Snapshot.LoadEpoch, PlayerId, null,
                $"arrest-{Guid.NewGuid()}", previousTier, currentTier, null, null));

        /// <summary>Advances canonical game time by one minute and runs one convergence pass plus one projector reconcile.</summary>
        public void Tick()
        {
            World.TotalMinutes = (World.TotalMinutes ?? 0d) + 1d;
            Composition.Update();
        }

        /// <summary>
        /// A second composition over a fresh story runtime bound to the same repository (so it loads
        /// the persisted state), a fresh observer (an empty queue), a fresh world and a fresh native
        /// fake: exactly what a process restart hands the mod shell on the next load.
        /// </summary>
        public Harness Restart()
        {
            var story = new Release1StoryRuntimeService(Context, Repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            var world = new CompositionFakeWorld { CashBalance = World.CashBalance, TotalMinutes = World.TotalMinutes };
            var observer = new Release1ChiefTierObserver();
            var ledger = new LedgerHarness { SessionEpoch = Context.Snapshot.SessionEpoch, LoadEpoch = Context.Snapshot.LoadEpoch, State = Ledger.State };
            var native = new FakeNative();
            var composition = new Release1ChiefComposition(
                story, world, observer, ledger.Read, ledger.Wipe, ledger.Epochs,
                Release1PresentationMode.Native, native, message => throw new InvalidOperationException(message));
            return new Harness(story, Context, Repository, world, observer, ledger, native, composition);
        }

        public static Harness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            const string receipt = "chief-settled-drain-regression-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var world = new CompositionFakeWorld();
            var observer = new Release1ChiefTierObserver();
            var ledger = new LedgerHarness
            {
                SessionEpoch = context.Snapshot.SessionEpoch,
                LoadEpoch = context.Snapshot.LoadEpoch,
                State = LocalPressureState.Quiet(context.Snapshot.PlayerId)
            };
            var native = new FakeNative();
            var composition = new Release1ChiefComposition(
                story, world, observer, ledger.Read, ledger.Wipe, ledger.Epochs,
                Release1PresentationMode.Native, native, message => throw new InvalidOperationException(message));
            return new Harness(story, context, repository, world, observer, ledger, native, composition);
        }

        public void Dispose()
        {
            Composition.Dispose();
            Story.Dispose();
        }
    }
}
