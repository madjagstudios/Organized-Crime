using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-73 Task 5. Drives Chief Campbell's whole beat end to end through
/// <see cref="Release1ChiefComposition"/> against the real story runtime, the real
/// <see cref="Release1ChiefService"/>, a fake world and a fake ledger, with a native presentation
/// fake standing in for S1API so the composition's own projector is exercised too, not just the
/// service underneath it.
/// </summary>
public sealed class Release1ChiefCompositionTests
{
    [Fact]
    public void The_chief_composition_drives_the_whole_beat_end_to_end()
    {
        using var h = Harness.Create();
        var standingBefore = h.Story.State!.Standing;
        var missionsBefore = h.Story.State!.Missions;

        // 1. Adoption: the first pass against a record free save writes Adopted and sends nothing.
        h.Composition.Update();
        Assert.Equal(Release1ChiefState.Adopted, h.Record!.State);
        Assert.Empty(h.Native.SentMessages);
        Assert.Null(h.Native.ObservedDecision);

        // 2. First arrest: the first contact prompt is sent exactly once, as the decision's own
        // prompt, never also as a separate passive message (OC-73 review fix: the boundary already
        // sends a decision prompt as a message on its own behalf, so queuing DemandText a second time
        // as a passive message would show it in the phone twice). Counted, not merely Contains, so a
        // regression that reintroduces the duplicate fails this assertion.
        h.Arrest();
        h.Tick();
        Assert.Equal(Release1ChiefState.DemandOpen, h.Record!.State);
        Assert.Equal(1, h.Record!.DemandRound);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText)));
        Assert.NotNull(h.Native.ObservedDecision);
        Assert.Equal(2, h.Native.ObservedDecision!.Labels.Count);
        Assert.Contains(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PayLabel(h.Record!.Demand)), h.Native.ObservedDecision!.Labels);
        Assert.Contains(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineLabel), h.Native.ObservedDecision!.Labels);
        Assert.Equal(1, h.Native.SetDecisionCalls);
        var sentAfterFirstDemand = h.Native.SentMessages.Count;

        // A further pass with nothing new never resends the demand or resets the decision: "exactly once".
        h.Tick();
        h.Tick();
        Assert.Equal(sentAfterFirstDemand, h.Native.SentMessages.Count);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText)));
        Assert.Equal(1, h.Native.SetDecisionCalls);

        // 3. Decline: the reply, exactly once, and the prompt clears.
        Assert.True(h.Composition.Service.TryDecline());
        h.Tick();
        Assert.Equal(Release1ChiefState.Declined, h.Record!.State);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText)));
        Assert.Null(h.Native.ObservedDecision);

        // 4. A second arrest raises the price to 20000, delivered exactly once, in both the prompt
        // and the option label.
        h.Arrest();
        h.Tick();
        Assert.Equal(Release1ChiefState.DemandOpen, h.Record!.State);
        Assert.Equal(2, h.Record!.DemandRound);
        Assert.Equal(20_000, h.Record!.Demand);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.SecondDemandText)));
        Assert.NotNull(h.Native.ObservedDecision);
        Assert.Contains(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PayLabel(20_000)), h.Native.ObservedDecision!.Labels);

        // 5. Pay: the wallet falls by exactly the demand, the ledger reads Known Offender false and
        // Local Heat 0 after the wipe, and the effect reaches Committed after the next save.
        h.World.CashBalance = 100_000f;
        var balanceBeforePay = h.World.CashBalance;
        Assert.True(h.Composition.Service.TryPay());
        h.Tick(); // wipe and settle land on this pass (Task 4 also releases the world here, but no lockdown is engaged yet)
        Assert.Equal(Release1ChiefState.Settled, h.Record!.State);
        Assert.Equal(1, h.World.DebitCalls);
        Assert.Equal(-20_000f, h.World.LastDebitAmount);
        Assert.Equal(balanceBeforePay - 20_000f, h.World.CashBalance);
        Assert.False(h.Ledger.State!.KnownOffender);
        Assert.Equal(0, h.Ledger.State!.LocalHeat);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PaidText)));

        h.Story.OnSaveStart();
        h.Story.OnSaveComplete();
        h.Composition.OnSaveComplete();
        h.Tick();
        var paidEffectId = h.Record!.NativeEffectIds[^1];
        Assert.Equal(Release1NativeEffectPhase.Committed, h.Story.State!.NativeEffects.Single(e => e.EffectId == paidEffectId).Phase);

        // 6. Four arrests from the now clean ledger (Moderate profile, arrest delta 20, Known
        // Offender floor 25) drive Watched (heat 65) and then Critical (heat 85), with exactly one
        // watch line and one lockdown announcement.
        Assert.Equal(0, h.Record!.WatchLinesSent);
        Assert.Equal(0, h.Record!.LockdownAnnouncements);

        SetLedgerHeat(h, 25); // arrest 1: Quiet -> Noticed, no crossing
        h.Arrest(LocalPressureTier.Quiet, LocalPressureTier.Noticed);
        h.Tick();

        SetLedgerHeat(h, 45); // arrest 2: Noticed -> Noticed, no crossing
        h.Arrest(LocalPressureTier.Noticed, LocalPressureTier.Noticed);
        h.Tick();

        SetLedgerHeat(h, 65); // arrest 3: Noticed -> Watched, the watch line crossing
        h.Arrest(LocalPressureTier.Noticed, LocalPressureTier.Watched);
        h.Tick(); // drains ArrestObserved
        h.Tick(); // drains RoseToWatched
        Assert.Equal(1, h.Record!.WatchLinesSent);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.WatchListText)));

        SetLedgerHeat(h, 85); // arrest 4: Watched -> Critical, the lockdown announcement crossing
        h.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        h.Tick(); // drains ArrestObserved
        h.Tick(); // drains RoseToCritical: announces this pass, does not yet engage
        Assert.Equal(1, h.Record!.LockdownAnnouncements);
        Assert.False(h.Record!.LockdownEngaged);
        Assert.Equal(0, h.World.EngageCalls);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LockdownAnnouncementText)));

        // 7. The engage happens on a later pass, once the announcement's own receipt exists.
        h.Tick();
        Assert.True(h.Record!.LockdownEngaged);
        Assert.Equal(1, h.World.EngageCalls);

        // 8. The paid lift: paying while under lockdown releases it through the world.
        h.World.CashBalance = 100_000f;
        Assert.True(h.Composition.Service.TryPay());
        h.Tick();
        Assert.Equal(Release1ChiefState.Settled, h.Record!.State);
        Assert.False(h.Record!.LockdownEngaged);
        Assert.Equal(1, h.Record!.PaidLifts);
        Assert.Equal(1, h.World.ReleaseCalls);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftPaidText)));

        // Empty Standing, mission and unlock deltas across all of it: the Chief is composed beside
        // the mission ladder, never inside it.
        Assert.Equal(standingBefore, h.Story.State!.Standing);
        Assert.Equal(missionsBefore.Count, h.Story.State!.Missions.Count);
        for (var i = 0; i < missionsBefore.Count; i++)
            Assert.True(missionsBefore[i].ValueEquals(h.Story.State!.Missions[i]));
    }

    [Fact]
    public void A_full_reconstruction_from_the_persisted_sidecar_produces_no_duplicate_message_and_no_second_announcement()
    {
        using var h = Harness.Create();
        h.Ledger.State = new LocalPressureState(80, true, null, null, null, h.PlayerId, null, null, 0);
        h.Composition.Update();
        h.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        h.Tick(); // drains ArrestObserved
        h.Tick(); // drains RoseToCritical: announces this pass
        Assert.Equal(1, h.Record!.LockdownAnnouncements);
        h.Tick(); // engages, now that the announcement's own receipt exists
        Assert.True(h.Record!.LockdownEngaged);

        // A real restart only ever finds what the sidecar last saved: Release1ChiefRecord writes are
        // in memory only until a save persists them (Release1StoryRuntimeService.TrySetChiefRecord's
        // own doc comment), exactly like every other mission record.
        h.Story.OnSaveStart();
        h.Story.OnSaveComplete();
        h.Composition.OnSaveComplete();
        var sentBeforeRestart = h.Native.SentMessages.Count;

        // Simulate a full restart: a new story runtime over the same persisted repository, a fresh
        // observer, a fresh world (zero call counters, the native gate not engaged this session) and
        // a fresh native fake (nothing sent yet this session either).
        using var restart = h.Restart();
        restart.Composition.OnLoadComplete();
        restart.Tick();

        // The lockdown re engages against the fresh native gate...
        Assert.Equal(1, restart.World.EngageCalls);
        Assert.True(restart.Record!.LockdownEngaged);
        // ...but no passive message is sent twice: every message correlation this record has ever
        // earned already has a receipt from before the restart (receipts persist with the save), so
        // ReconcileMessages sends none of them again. The one exception is the decision prompt: this
        // fake, unlike real S1API, restores no native response history across a session boundary, so
        // the demand prompt is (accurately for this fake, not for the real boundary, which binds onto
        // restored responses instead) sent once more as its own fresh decision. The record itself
        // gained no second announcement: LockdownAnnouncements is still 1, not 2.
        Assert.Equal(0, restart.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LockdownAnnouncementText)));
        Assert.Equal(1, restart.Record!.LockdownAnnouncements);
        Assert.Equal(1, h.Record!.LockdownAnnouncements);
        Assert.Equal(0, h.Native.SentMessages.Count - sentBeforeRestart);
    }

    [Fact]
    public void A_decline_after_a_ladder_restart_is_delivered_with_a_distinct_correlation()
    {
        // OC-73 review fix (finding 1), the core regression, driven end to end through the real
        // composition and projector rather than just the pure planner. DeclineReceipt used to fold in
        // record.DemandRound alone: decline, pay, settle, arrest (Release1ChiefLedger.Observe's
        // Adopted-or-Settled branch restarts the ladder at round 1), decline again minted the identical
        // chief-decline-r1 correlation the first cycle's decline already earned a receipt for, so the
        // real projector silently suppressed the second reply and the player who picked "Not today" got
        // nothing back. DeclineRepliesSent (a non-resetting counter, mirroring WatchLinesSent and
        // PaidLifts) is the fix: both declines below now get sent, and this test would have failed
        // before the fix (the second SentMessages count would have stayed at one).
        using var h = Harness.Create();
        h.Composition.Update();
        h.Arrest();
        h.Tick();
        Assert.Equal(1, h.Record!.DemandRound);

        // First decline: sent once, its own correlation receipted.
        Assert.True(h.Composition.Service.TryDecline());
        h.Tick();
        Assert.Equal(Release1ChiefState.Declined, h.Record!.State);
        Assert.Equal(1, h.Record!.DeclineRepliesSent);
        Assert.Equal(1, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText)));
        var firstReplyCorrelation = DeclineCorrelation(h.PlayerId, 1);
        Assert.Contains(h.Story.State!.PresentationReceipts, r => r.CorrelationId == firstReplyCorrelation);

        // Pay and settle: the ladder restarts at round 1 on the next arrest (Settled-plus-arrest).
        h.Arrest();
        h.Tick();
        Assert.Equal(Release1ChiefState.DemandOpen, h.Record!.State);
        Assert.Equal(2, h.Record!.DemandRound);
        h.World.CashBalance = 100_000f;
        Assert.True(h.Composition.Service.TryPay());
        h.Tick();
        Assert.Equal(Release1ChiefState.Settled, h.Record!.State);

        h.Story.OnSaveStart();
        h.Story.OnSaveComplete();
        h.Composition.OnSaveComplete();
        h.Tick();
        Assert.Equal(Release1NativeEffectPhase.Committed, h.Story.State!.NativeEffects.Single().Phase);

        h.Arrest();
        h.Tick();
        Assert.Equal(Release1ChiefState.DemandOpen, h.Record!.State);
        Assert.Equal(1, h.Record!.DemandRound);

        // Second decline, in the new cycle, at the identical round the first decline used: sent again,
        // with a correlation distinct from the first reply's, and DeclineRepliesSent still climbing.
        Assert.True(h.Composition.Service.TryDecline());
        h.Tick();
        Assert.Equal(Release1ChiefState.Declined, h.Record!.State);
        Assert.Equal(2, h.Record!.DeclineRepliesSent);
        Assert.Equal(2, h.Native.SentMessages.Count(m => m == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText)));
        var secondReplyCorrelation = DeclineCorrelation(h.PlayerId, 2);
        Assert.Contains(h.Story.State!.PresentationReceipts, r => r.CorrelationId == secondReplyCorrelation);
        Assert.NotEqual(firstReplyCorrelation, secondReplyCorrelation);
    }

    /// <summary>
    /// Reconstructs the exact correlation Release1ChiefPresentation.CounterMessage mints for the
    /// decline reply: round pinned to 1 (CounterCorrelation), receipt id "chief-decline-{count}".
    /// </summary>
    private static string DeclineCorrelation(string playerId, int declineRepliesSent) =>
        Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.MissionOffered,
            $"chief-decline-{declineRepliesSent}").Value;

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

        /// <summary>Advances canonical game time by one minute and runs one convergence pass.</summary>
        public void Tick()
        {
            World.TotalMinutes = (World.TotalMinutes ?? 0d) + 1d;
            Composition.Update();
        }

        /// <summary>
        /// A second composition over a fresh story runtime bound to the same repository (so it loads
        /// the persisted state), a fresh observer, a fresh world and a fresh native fake: exactly what
        /// a process restart hands the mod shell on the next load.
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

            const string receipt = "chief-composition-test-intro";
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
