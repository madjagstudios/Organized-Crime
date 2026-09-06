using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Reconciles a <see cref="Release1PresentationPlan"/> (built from live inputs on every pass)
/// against the native presentation boundary, applying only the difference. The story runtime
/// remains the sole authority: every durable receipt is recorded through
/// <see cref="Release1StoryRuntimeService.TryRecordPresentationReceipt"/>, and this class never
/// mutates story state directly. No Unity or S1API references.
/// </summary>
public sealed class Release1PresentationProjector : IDisposable
{
    private readonly Release1StoryRuntimeService _story;
    private readonly Func<Release1PresentationPlan> _plan;
    private readonly IRelease1NativePresentation _native;
    private readonly IRelease1PresentationCommandSink _commands;
    private readonly Action<string> _log;
    private readonly bool _reconcileQuests;
    private bool _saveSuspended;
    private bool _disposed;

    /// <summary>
    /// OC-73. <paramref name="reconcileQuests"/> defaults to true, so Nell's own projector (the only
    /// instance that existed before OC-73) is unchanged: it still applies every desired quest and
    /// ends every catalog quest the plan no longer wants. Pass false only for a second, contact
    /// scoped projector instance (Chief Campbell's own) sharing the same native game world: S1API's
    /// <c>QuestManager.GetQuestByName</c> is a single global registry keyed by title, not scoped to
    /// which contact applied the quest, so a second projector whose own plan never desires any
    /// catalog quest would otherwise read every one of the six shipped mission quests as "not
    /// desired by this plan" and end each of them out from under Nell's own projector on every pass.
    /// The Chief adds no quest of his own (his own plan's <c>Quests</c> is always empty), so skipping
    /// this sweep entirely for him loses nothing his own plan would ever have projected.
    /// </summary>
    public Release1PresentationProjector(
        Release1StoryRuntimeService story,
        Func<Release1PresentationPlan> plan,
        IRelease1NativePresentation native,
        IRelease1PresentationCommandSink commands,
        Action<string>? log = null,
        bool reconcileQuests = true)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _log = log ?? (_ => { });
        _reconcileQuests = reconcileQuests;
    }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        Reconcile();
    }

    public void OnPreLoad()
    {
        _saveSuspended = false;
        try { _native.OnPreLoad(); }
        catch (Exception exception) { _log($"Release 1 native presentation OnPreLoad faulted: {exception.GetType().Name}"); }
    }

    public void OnSaveStart()
    {
        if (_disposed) return;
        try { _native.OnSaveStart(); }
        catch (Exception exception) { _log($"Release 1 native presentation OnSaveStart faulted: {exception.GetType().Name}"); }
        _saveSuspended = true;
    }

    public void OnSaveComplete()
    {
        if (_disposed) return;
        _saveSuspended = false;
        Reconcile();
    }

    /// <summary>
    /// Diffs the current plan against observed native state and applies only the difference.
    /// Safe to call repeatedly in one frame: every step re-reads live state, so a pass with
    /// nothing left to do performs only read calls. Any boundary exception, or a returned
    /// <see cref="Release1NativePresentationStatus.Faulted"/>, is logged once and ends the pass;
    /// the next pass retries the diff from scratch. <see cref="Release1NativePresentationStatus.Unavailable"/>
    /// and <see cref="Release1NativePresentationStatus.Rejected"/> also end the pass, without logging,
    /// since they are ordinary recoverable conditions. The one exception is the decision-set/clear
    /// calls: a non-<see cref="Release1NativePresentationStatus.Faulted"/>,
    /// non-<see cref="Release1NativePresentationStatus.Succeeded"/> status there is logged once and
    /// the pass continues on to the quest loop instead of ending, so a decision bind that legitimately
    /// needs another pass to settle never stalls quest reconciliation behind it.
    ///
    /// A desired message is sent when its correlation has no matching entry in
    /// <see cref="Release1StoryState.PresentationReceipts"/>, in memory or persisted; the receipt is
    /// the sole record of at-most-once delivery. This pass no longer reads native message history to
    /// guard sends: native history and receipts revert together with the save, so a receipt already
    /// carries the guarantee a text match used to approximate, and every desired message now carries
    /// its own correlation, so identical milestone text sent again under a new correlation (a
    /// make-good or recovery attempt) is meant to be sent again, not swallowed because it happens to
    /// read the same as an earlier one. <see cref="IRelease1NativePresentation.TryReadSentMessages"/>
    /// is therefore not called from here; it remains on the interface for the boundary implementation
    /// and its own tests.
    /// </summary>
    public void Reconcile()
    {
        if (_disposed || _saveSuspended) return;
        if (!_story.TryGetActiveContext(out _, out _)) return;
        if (_story.Phase != Release1StoryRuntimePhase.Active) return;

        var loggedThisPass = false;
        void LogOnce(string message)
        {
            if (loggedThisPass) return;
            loggedThisPass = true;
            _log(message);
        }

        if (!TryStatus(() => _native.TryEnsureContact(), LogOnce, "Release 1 native presentation contact check", out _)) return;

        Release1PresentationPlan plan;
        try { plan = _plan(); }
        catch (Exception exception) { LogOnce($"Release 1 presentation planning faulted: {exception.GetType().Name}"); return; }

        if (!ReconcileMessages(plan.Messages, LogOnce)) return;

        if (!TryRead(() => { var s = _native.TryReadDecision(out var d); return (s, d); }, LogOnce, "Release 1 native presentation read decision", "reading the decision", out var observedDecision)) return;

        // When the plan reports DecisionUndetermined, intro eligibility is still being observed after
        // a reload: skip the decision reconcile entirely rather than clearing native responses S1API
        // may have just restored (they would otherwise read as "unwanted" and get cleared, only for the
        // same offer to be re-sent once eligibility resolves, producing a duplicate message).
        if (!plan.DecisionUndetermined && !ReconcileDecision(plan.Decision, observedDecision, LogOnce)) return;

        if (_reconcileQuests && !ReconcileQuests(plan.Quests, LogOnce)) return;
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
    }

    private bool ReconcileMessages(IReadOnlyList<Release1DesiredMessage> messages, Action<string> logOnce)
    {
        foreach (var message in messages)
        {
            var state = _story.State;
            var hasReceipt = state is not null && state.PresentationReceipts.Any(receipt => receipt.CorrelationId == message.CorrelationId);
            if (hasReceipt) continue;

            if (!TryStatus(() => _native.TrySendMessage(message.Text), logOnce, "Release 1 native presentation send message", out _)) return false;

            if (state is not null && Release1LogicalCorrelation.TryParse(message.CorrelationId, out _))
                _story.TryRecordPresentationReceipt(message.CorrelationId);
        }
        return true;
    }

    private bool ReconcileDecision(Release1DesiredDecision? desired, Release1ObservedDecision? observed, Action<string> logOnce)
    {
        if (desired is null)
        {
            if (observed is null) return true;
            return ApplyDecisionStatus(() => _native.TryClearDecision(), logOnce, "Release 1 native presentation clear decision");
        }

        if (observed is not null && DecisionsMatch(desired, observed))
        {
            // A non-null observed id means this session already bound the callback onto native state
            // (either by sending it fresh or by binding it in an earlier pass); nothing left to do. A
            // null observed id means the labels were restored natively across a load with no session
            // binding yet, so the boundary must still be asked to bind the callback onto those already-
            // showing responses (TrySetDecision's bind-existing branch), even though the labels match.
            if (observed.Id is not null) return true;
            return ApplyDecisionStatus(() => _native.TrySetDecision(desired, OnDecisionChosen), logOnce, "Release 1 native presentation set decision");
        }

        if (observed is not null)
        {
            if (!ApplyDecisionStatus(() => _native.TryClearDecision(), logOnce, "Release 1 native presentation clear decision")) return false;
        }

        return ApplyDecisionStatus(() => _native.TrySetDecision(desired, OnDecisionChosen), logOnce, "Release 1 native presentation set decision");
    }

    /// <summary>
    /// Applies one decision-related native call. A <see cref="Release1NativePresentationStatus.Faulted"/>
    /// result (or a thrown exception) stops the whole reconcile pass, like every other boundary call.
    /// <see cref="Release1NativePresentationStatus.Unavailable"/> or <see cref="Release1NativePresentationStatus.Rejected"/>
    /// (for example, a partial response bind still waiting on responses S1API has not restored yet) is
    /// logged once but treated as non-fatal for this pass: quest reconciliation must not stall behind a
    /// decision bind that legitimately needs another pass to settle.
    /// </summary>
    private static bool ApplyDecisionStatus(Func<Release1NativePresentationStatus> call, Action<string> logOnce, string description)
    {
        Release1NativePresentationStatus status;
        try { status = call(); }
        catch (Exception exception)
        {
            logOnce($"{description} faulted: {exception.GetType().Name}");
            return false;
        }
        if (status == Release1NativePresentationStatus.Faulted)
        {
            logOnce($"{description} reported a fault.");
            return false;
        }
        if (status != Release1NativePresentationStatus.Succeeded)
            logOnce($"{description} did not complete this pass ({status}); continuing.");
        return true;
    }

    /// <summary>
    /// Applies every desired quest, then ends any native quest that is present but no longer desired
    /// (for example, a mission chapter the plan has moved past). Checked against every catalog mission
    /// key not already covered by <paramref name="quests"/>, since a plan can drop a quest entirely
    /// rather than ever including it with different entries.
    /// </summary>
    private bool ReconcileQuests(IReadOnlyList<Release1DesiredQuest> quests, Action<string> logOnce)
    {
        foreach (var quest in quests)
        {
            if (!ReconcileQuest(quest, logOnce)) return false;
        }

        var desiredKeys = new HashSet<string>(quests.Select(quest => quest.Key), StringComparer.Ordinal);
        foreach (var definition in Release1MissionCatalog.All)
        {
            if (desiredKeys.Contains(definition.MissionKey)) continue;
            if (!ReconcileUndesiredQuest(definition.MissionKey, logOnce)) return false;
        }
        return true;
    }

    private bool ReconcileQuest(Release1DesiredQuest quest, Action<string> logOnce)
    {
        if (!TryRead(() => { var s = _native.TryReadQuest(quest.Key, out var q); return (s, q); }, logOnce, "Release 1 native presentation read quest", "reading a quest", out var observed)) return false;

        var desiredStates = quest.Entries.Select(entry => entry.State).ToArray();
        if (observed is not null && observed.EntryStates.SequenceEqual(desiredStates)) return true;

        // Once every desired entry is Complete, S1API auto-completes and deregisters the quest on the
        // pass that finishes it, so TryReadQuest reports it absent on every later load. Applying here
        // would only recreate it for S1API to complete and deregister again next pass, so once native
        // has already dropped a fully-complete quest, leave it dropped rather than recreate it. When
        // native still has the quest (mid-completion this pass, or not yet auto-deregistered), keep
        // applying so it actually reaches Complete.
        if (observed is null && desiredStates.All(state => state == Release1DesiredEntryState.Complete))
            return true;

        return TryStatus(() => _native.TryApplyQuest(quest), logOnce, "Release 1 native presentation apply quest", out _);
    }

    private bool ReconcileUndesiredQuest(string key, Action<string> logOnce)
    {
        if (!TryRead(() => { var s = _native.TryReadQuest(key, out var q); return (s, q); }, logOnce, "Release 1 native presentation read quest", "reading a quest", out var observed)) return false;
        if (observed is null) return true;

        return TryStatus(() => _native.TryEndQuest(key), logOnce, "Release 1 native presentation end quest", out _);
    }

    private void OnDecisionChosen(Release1PresentationCommand command)
    {
        _commands.TryInvoke(command);
        Reconcile();
    }

    /// <summary>
    /// A desired decision matches what is observed natively when the option labels agree, in order.
    /// When the boundary also reports an id (same-session continuity), that id must agree too, and
    /// labels are sufficient beyond that. A null observed id (post-reload, before this session has set
    /// anything) cannot be compared by id, so the prompt must also match: two different decisions can
    /// share the same label set (the intro and Small Courtesy accept decisions both offer
    /// "Accept"/"Not now"), and label-only matching would otherwise treat one as if it were the other.
    /// </summary>
    private static bool DecisionsMatch(Release1DesiredDecision desired, Release1ObservedDecision observed)
    {
        var labelsMatch = desired.Options.Select(option => option.Label).SequenceEqual(observed.Labels, StringComparer.Ordinal);
        if (observed.Id is not null)
            return labelsMatch && string.Equals(desired.Id, observed.Id, StringComparison.Ordinal);
        return labelsMatch && string.Equals(CollapseWhitespace(desired.Prompt), CollapseWhitespace(observed.Prompt), StringComparison.Ordinal);
    }

    private static string CollapseWhitespace(string? text)
    {
        if (text is null) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
    }

    private static bool TryStatus(
        Func<Release1NativePresentationStatus> call,
        Action<string> logOnce,
        string description,
        out Release1NativePresentationStatus status)
    {
        try { status = call(); }
        catch (Exception exception)
        {
            logOnce($"{description} faulted: {exception.GetType().Name}");
            status = Release1NativePresentationStatus.Faulted;
            return false;
        }
        if (status == Release1NativePresentationStatus.Faulted)
        {
            logOnce($"{description} reported a fault.");
            return false;
        }
        return status == Release1NativePresentationStatus.Succeeded;
    }

    private static bool TryRead<T>(
        Func<(Release1NativePresentationStatus Status, T Value)> call,
        Action<string> logOnce,
        string exceptionDescription,
        string faultDescription,
        out T value)
    {
        value = default!;
        Release1NativePresentationStatus status;
        try { (status, value) = call(); }
        catch (Exception exception)
        {
            logOnce($"{exceptionDescription} faulted: {exception.GetType().Name}");
            return false;
        }
        if (status == Release1NativePresentationStatus.Faulted)
        {
            logOnce($"Release 1 native presentation reported a fault {faultDescription}.");
            return false;
        }
        return status == Release1NativePresentationStatus.Succeeded;
    }
}

/// <summary>
/// Routes a chosen decision command to exactly one real handler: the intro transition publisher
/// or the Small Courtesy presenter. Never touches story state directly; each handler is
/// responsible for its own durable authority. Returns false for a rejected outcome or an unknown
/// command value.
/// </summary>
public sealed class Release1PresentationCommandRouter : IRelease1PresentationCommandSink
{
    private readonly Release1TransitionPublisherService _publisher;
    private readonly Release1SmallCourtesyPresenter _presenter;
    private readonly Release1WrongAddressPresenter? _wrongAddress;
    private readonly Release1RoomWithNoNamePresenter? _roomWithNoName;
    private readonly Release1ShortNoticePresenter? _shortNotice;
    private readonly Release1KeepTheLightsOffPresenter? _keepTheLightsOff;
    private readonly Release1TheEnvelopePresenter? _theEnvelope;

    public Release1PresentationCommandRouter(
        Release1TransitionPublisherService publisher,
        Release1SmallCourtesyPresenter presenter,
        Release1WrongAddressPresenter? wrongAddress = null,
        Release1RoomWithNoNamePresenter? roomWithNoName = null,
        Release1ShortNoticePresenter? shortNotice = null,
        Release1KeepTheLightsOffPresenter? keepTheLightsOff = null,
        Release1TheEnvelopePresenter? theEnvelope = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _wrongAddress = wrongAddress;
        _roomWithNoName = roomWithNoName;
        _shortNotice = shortNotice;
        _keepTheLightsOff = keepTheLightsOff;
        _theEnvelope = theEnvelope;
    }

    public bool TryInvoke(Release1PresentationCommand command) => command switch
    {
        Release1PresentationCommand.IntroAccept => _publisher.TryAccept(),
        Release1PresentationCommand.IntroDefer => _publisher.TryDefer(),
        Release1PresentationCommand.SmallCourtesyReview => _presenter.TryReview().Status == Release1SmallCourtesyReviewStatus.Ready,
        Release1PresentationCommand.SmallCourtesyAccept => _presenter.TryAccept().Status == Release1SmallCourtesyDecisionStatus.Accepted,
        Release1PresentationCommand.SmallCourtesyDefer => _presenter.TryDefer().Status == Release1SmallCourtesyDecisionStatus.Deferred,
        Release1PresentationCommand.WrongAddressReview => _wrongAddress?.TryReview().Status == Release1WrongAddressReviewStatus.Ready,
        Release1PresentationCommand.WrongAddressAccept => _wrongAddress?.TryAccept().Status == Release1WrongAddressDecisionStatus.Accepted,
        Release1PresentationCommand.WrongAddressDefer => _wrongAddress?.TryDefer().Status == Release1WrongAddressDecisionStatus.Deferred,
        Release1PresentationCommand.RoomWithNoNameReview => _roomWithNoName?.TryReview().Status == Release1RoomWithNoNameReviewStatus.Ready,
        Release1PresentationCommand.RoomWithNoNameAccept => _roomWithNoName?.TryAccept().Status == Release1RoomWithNoNameDecisionStatus.Accepted,
        Release1PresentationCommand.RoomWithNoNameDefer => _roomWithNoName?.TryDefer().Status == Release1RoomWithNoNameDecisionStatus.Deferred,
        Release1PresentationCommand.ShortNoticeReview => _shortNotice?.TryReview().Status == Release1ShortNoticeReviewStatus.Ready,
        Release1PresentationCommand.ShortNoticeAccept => _shortNotice?.TryAccept().Status == Release1ShortNoticeDecisionStatus.Accepted,
        Release1PresentationCommand.ShortNoticeDefer => _shortNotice?.TryDefer().Status == Release1ShortNoticeDecisionStatus.Deferred,
        Release1PresentationCommand.KeepTheLightsOffReview => _keepTheLightsOff?.TryReview().Status == Release1KeepTheLightsOffReviewStatus.Ready,
        Release1PresentationCommand.KeepTheLightsOffAccept => _keepTheLightsOff?.TryAccept().Status == Release1KeepTheLightsOffDecisionStatus.Accepted,
        Release1PresentationCommand.KeepTheLightsOffDefer => _keepTheLightsOff?.TryDefer().Status == Release1KeepTheLightsOffDecisionStatus.Deferred,
        Release1PresentationCommand.TheEnvelopeReview => _theEnvelope?.TryReview().Status == Release1TheEnvelopeReviewStatus.Ready,
        Release1PresentationCommand.TheEnvelopeAccept => _theEnvelope?.TryAccept().Status == Release1TheEnvelopeDecisionStatus.Accepted,
        Release1PresentationCommand.TheEnvelopeDefer => _theEnvelope?.TryDefer().Status == Release1TheEnvelopeDecisionStatus.Deferred,
        _ => false
    };
}
