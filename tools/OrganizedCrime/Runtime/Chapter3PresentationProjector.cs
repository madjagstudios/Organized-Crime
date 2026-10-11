using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Reconciles a <see cref="Chapter3PresentationPlan"/> (built from live state
/// on every pass) against the shared native presentation boundary, applying
/// only the difference. Mirrors <see cref="Release1PresentationProjector"/>
/// minus the quest sweep, which Chapter 3 has no quests for: every durable
/// receipt is recorded through
/// <see cref="Chapter3RuntimeService.TryRecordPresentationReceipt"/>, and this
/// class never mutates chapter state directly. No Unity or S1API references.
/// </summary>
public sealed class Chapter3PresentationProjector : IDisposable
{
    private readonly Chapter3RuntimeService _service;
    private readonly Func<Chapter3PresentationPlan> _plan;
    private readonly IRelease1NativePresentation _native;
    private readonly IChapter3PresentationCommandSink _commands;
    private readonly Action<string> _log;
    private readonly Func<bool>? _decisionOwnedElsewhere;
    private bool _saveSuspended;
    private bool _disposed;
    private bool _questOutstanding;
    /// <summary>
    /// OC-136: entry texts from the last applied quest. The native read
    /// reports states only, so job texts (frozen per job) reconcile on
    /// states alone, but the demand's progress lines move under steady
    /// states and need the text remembered to know when to re-apply.
    /// </summary>
    private IReadOnlyList<string>? _lastQuestEntryTexts;
    private bool _sceneChanging;

    /// <summary>
    /// Fault messages already reported to the log. Reconcile() runs every
    /// frame, so a fault that persists across passes logs once per distinct
    /// message until it clears, not once per pass. Reset on a fully successful
    /// pass and on pre-load, so a fault that clears and later recurs is
    /// reported again.
    /// </summary>
    private readonly OnceLog _reportedFaults;

    /// <summary>
    /// <paramref name="decisionOwnedElsewhere"/> reports whether Release 1's
    /// projector currently owns Nell's reply buttons; while it does, this
    /// projector skips the decision step entirely (no clear, no set) but still
    /// sends its messages. Release 1's projector takes the mirror check over
    /// <see cref="LastPlan"/>, so whichever chapter's buttons are bound stays
    /// bound until its own tap resolves the tie.
    /// </summary>
    public Chapter3PresentationProjector(
        Chapter3RuntimeService service,
        Func<Chapter3PresentationPlan> plan,
        IRelease1NativePresentation native,
        IChapter3PresentationCommandSink commands,
        Action<string>? log = null,
        Func<bool>? decisionOwnedElsewhere = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _log = log ?? (_ => { });
        _decisionOwnedElsewhere = decisionOwnedElsewhere;
        _reportedFaults = new OnceLog(_log);
    }

    /// <summary>
    /// The plan from the most recent reconcile pass, or null before the first
    /// pass. Release 1 reads this to yield Nell's reply buttons while Chapter 3
    /// wants them.
    /// </summary>
    public Chapter3PresentationPlan? LastPlan { get; private set; }

    public void OnLoadComplete()
    {
        _sceneChanging = false;
        if (_disposed) return;
        Reconcile();
    }

    public void OnPreLoad()
    {
        _saveSuspended = false;
        _sceneChanging = false;
        LastPlan = null;
        _reportedFaults.ClearAll();
        _lastQuestEntryTexts = null;
        try { _native.OnPreLoad(); }
        catch (Exception exception) { _log($"Chapter 3 native presentation OnPreLoad faulted: {exception.GetType().Name}"); }
    }

    /// <summary>
    /// A scene change or unload has begun: skip every reconcile until the
    /// next load boundary, so no native call runs against a world being
    /// torn down (quit-to-menu faulted here).
    /// </summary>
    public void OnPreSceneChange()
    {
        _sceneChanging = true;
    }

    public void OnSaveStart()
    {
        if (_disposed) return;
        try { _native.OnSaveStart(); }
        catch (Exception exception) { _log($"Chapter 3 native presentation OnSaveStart faulted: {exception.GetType().Name}"); }
        _saveSuspended = true;
    }

    public void OnSaveComplete()
    {
        if (_disposed) return;
        _saveSuspended = false;
        Reconcile();
    }

    /// <summary>
    /// Diffs the current plan against observed native state and applies only
    /// the difference. Safe to call repeatedly in one frame: every step
    /// re-reads live state, so a pass with nothing left to do performs only
    /// read calls. Any boundary exception, or a returned
    /// <see cref="Release1NativePresentationStatus.Faulted"/>, ends the pass;
    /// the next pass retries the diff from scratch. Each distinct fault message
    /// is logged once until it clears. <see cref="Release1NativePresentationStatus.Unavailable"/>
    /// and <see cref="Release1NativePresentationStatus.Rejected"/> also end the
    /// pass, without logging, since they are ordinary recoverable conditions.
    /// The one exception is the decision-set/clear calls: a non-<see cref="Release1NativePresentationStatus.Faulted"/>,
    /// non-<see cref="Release1NativePresentationStatus.Succeeded"/> status there (also without
    /// logging, for the same reason) lets the pass continue instead of ending, so
    /// a decision bind that legitimately needs another pass to settle never
    /// stalls anything behind it.
    ///
    /// A desired message is sent when its correlation has no matching entry in
    /// <see cref="Chapter3State.PresentationReceipts"/>, in memory or persisted;
    /// the receipt is the sole record of at-most-once delivery.
    /// </summary>
    public void Reconcile()
    {
        if (_disposed || _saveSuspended || _sceneChanging) return;
        if (_service.Phase != Chapter3RuntimePhase.Active)
        {
            // A decision left in LastPlan would make Release 1 yield Nell's
            // buttons forever, so leaving Active clears the claim. A quest this
            // projector applied is ended on the way out; nothing outstanding
            // means no native calls at all.
            LastPlan = null;
            if (_questOutstanding)
                EndOutstandingQuest();
            return;
        }

        var loggedThisPass = false;
        void LogOnce(string message)
        {
            if (loggedThisPass) return;
            loggedThisPass = true;
            _reportedFaults.Report(message, message);
        }

        if (!TryStatus(() => _native.TryEnsureContact(), LogOnce, "Chapter 3 native presentation contact check", out _)) return;

        Chapter3PresentationPlan plan;
        try { plan = _plan(); LastPlan = plan; }
        catch (Exception exception) { LogOnce($"Chapter 3 presentation planning faulted: {exception.GetType().Name}"); return; }

        if (!ReconcileMessages(plan.Messages, LogOnce)) return;

        if (!TryRead(() => { var s = _native.TryReadDecision(out var d); return (s, d); }, LogOnce, "Chapter 3 native presentation read decision", "reading the decision", out var observedDecision)) return;

        var decisionOwnedElsewhere = false;
        if (_decisionOwnedElsewhere is not null)
        {
            try { decisionOwnedElsewhere = _decisionOwnedElsewhere(); }
            catch (Exception exception) { LogOnce($"Chapter 3 decision ownership check faulted: {exception.GetType().Name}"); }
        }

        if (!decisionOwnedElsewhere && !ReconcileDecision(plan.Decision, observedDecision, LogOnce)) return;

        // The quest is never yielded: journals don't conflict, so this step
        // runs even while Release 1 owns Nell's decision.
        if (!ReconcileQuest(plan.Quest, LogOnce)) return;

        // Every step succeeded: the boundary is healthy, so a fault that recurs later is new
        // and must be reported again.
        _reportedFaults.ClearAll();
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
    }

    private bool ReconcileMessages(IReadOnlyList<Chapter3DesiredMessage> messages, Action<string> logOnce)
    {
        foreach (var message in messages)
        {
            var state = _service.State;
            var hasReceipt = state is not null && state.PresentationReceipts.Any(receipt => receipt.CorrelationId == message.CorrelationId);
            if (hasReceipt) continue;

            if (!TryStatus(() => _native.TrySendMessage(message.Text), logOnce, "Chapter 3 native presentation send message", out _)) return false;

            if (state is not null && Chapter3PresentationReceipt.TryParse(message.CorrelationId, out _, out var scope))
            {
                _service.TryRecordPresentationReceipt(message.CorrelationId);
                _log($"Chapter 3 Nell: sent {scope}");
            }
        }
        return true;
    }

    private bool ReconcileDecision(Chapter3DesiredDecision? desired, Release1ObservedDecision? observed, Action<string> logOnce)
    {
        if (desired is null)
        {
            if (observed is null) return true;
            if (!ApplyDecisionStatus(() => _native.TryClearDecision(), logOnce, "Chapter 3 native presentation clear decision")) return false;
            OrganizedCrimeLog.Diagnostic("Chapter 3 Nell: cleared buttons", _log);
            return true;
        }

        if (observed is not null && DecisionsMatch(desired, observed))
        {
            if (observed.Id is not null)
            {
                // The buttons already show with live callbacks: only a newly
                // relevant prompt scope still needs delivering (its scope
                // flipped, e.g. to overdue, while the buttons stayed the same).
                return EnsurePromptDelivered(desired, logOnce);
            }
            // Labels restored across a reload with no session binding yet:
            // deliver a newly relevant prompt scope first, then bind our
            // callbacks onto the already-showing responses. The bind passes the
            // reprompt text: when the native side binds existing responses
            // nothing is sent, and if the responses vanished mid-pass the
            // resend still cannot repeat a subject.
            if (!EnsurePromptDelivered(desired, logOnce)) return false;
            if (!TryStatus(() => _native.TrySetLabeledDecision(ToLabeled(desired, desired.RepromptText)), logOnce, "Chapter 3 native presentation rebind decision", out _)) return false;
            _log($"Chapter 3 Nell: rebound {DescribeLabels(desired)}");
            return true;
        }

        if (observed is not null)
        {
            if (!ApplyDecisionStatus(() => _native.TryClearDecision(), logOnce, "Chapter 3 native presentation clear decision")) return false;
        }

        // The set is strict (unlike the clears): the prompt receipt below may
        // only be recorded when the native side confirms the send, so a
        // deferred bind retries on the next pass instead of losing the text.
        var promptReceipted = IsReceipted(desired.PromptCorrelationId);
        // S1API (tested on 3.2.1-beta.7) does not restore Nell's saved responses on load.
        // Do not wait for them; reprompt once instead.
        var prompt = promptReceipted ? desired.RepromptText : desired.Prompt;
        if (!TryStatus(() => _native.TrySetLabeledDecision(ToLabeled(desired, prompt)), logOnce, "Chapter 3 native presentation set decision", out _)) return false;
        if (promptReceipted)
            _log($"Chapter 3 Nell: reprompted {DescribeLabels(desired)}");
        else
            RecordPromptReceipt(desired);
        return true;
    }

    /// <summary>
    /// Delivers the decision's prompt scope when the buttons already show
    /// without it: sends the subject as a plain message and records its
    /// receipt, so the text reaches the player exactly once.
    /// </summary>
    private bool EnsurePromptDelivered(Chapter3DesiredDecision desired, Action<string> logOnce)
    {
        if (desired.PromptCorrelationId is null || IsReceipted(desired.PromptCorrelationId)) return true;
        if (!TryStatus(() => _native.TrySendMessage(desired.Prompt), logOnce, "Chapter 3 native presentation send prompt", out _)) return false;
        RecordPromptReceipt(desired);
        return true;
    }

    private bool IsReceipted(string? correlationId)
    {
        if (correlationId is null) return true;
        var state = _service.State;
        return state is not null && state.PresentationReceipts.Any(receipt => receipt.CorrelationId == correlationId);
    }

    private void RecordPromptReceipt(Chapter3DesiredDecision desired)
    {
        if (desired.PromptCorrelationId is null) return;
        if (!Chapter3PresentationReceipt.TryParse(desired.PromptCorrelationId, out _, out var scope)) return;
        _service.TryRecordPresentationReceipt(desired.PromptCorrelationId);
        _log($"Chapter 3 Nell: sent {scope}");
    }

    private static string DescribeLabels(Chapter3DesiredDecision desired) =>
        string.Join(" | ", desired.Options.Select(option => option.Label));

    private Release1LabeledDecision ToLabeled(Chapter3DesiredDecision desired, string prompt) =>
        new(desired.Id, prompt, desired.Options.Select(option =>
            new Release1LabeledDecisionOption(option.Label, () => OnDecisionChosen(option))).ToArray());

    private void OnDecisionChosen(Chapter3DecisionOption option)
    {
        var result = _commands.TryInvoke(option.Command);
        _log($"Chapter 3 Nell: {option.Label} -> {result.Detail}");
        Reconcile();
    }

    /// <summary>
    /// A desired decision matches what is observed natively when the option labels agree, in order.
    /// When the boundary also reports an id (same-session continuity), that id must agree too, and
    /// labels are sufficient beyond that. A null observed id (post-reload, before this session has set
    /// anything) cannot be compared by id, so labels alone decide: Chapter 3 labels fully describe
    /// what each button does ("Pay $17,000", "Accept"), so binding the current command onto a
    /// restored same-label button is correct even when the restored prompt differs (it will, once
    /// this decision has been reprompted).
    /// </summary>
    private static bool DecisionsMatch(Chapter3DesiredDecision desired, Release1ObservedDecision observed)
    {
        var labelsMatch = desired.Options.Select(option => option.Label).SequenceEqual(observed.Labels, StringComparer.Ordinal);
        if (observed.Id is not null)
            return labelsMatch && string.Equals(desired.Id, observed.Id, StringComparison.Ordinal);
        return labelsMatch;
    }

    /// <summary>
    /// Diffs the one Chapter 3 quest against the observed native quest and
    /// applies only the difference, mirroring Release 1's per-quest reconcile:
    /// a pass with nothing to change performs only the read. Unlike the
    /// decision, this step is never skipped while Release 1 owns Nell's
    /// buttons.
    /// </summary>
    private bool ReconcileQuest(Release1DesiredQuest? desired, Action<string> logOnce)
    {
        if (!TryRead(() => { var s = _native.TryReadQuest(Chapter3QuestCopy.QuestKey, out var q); return (s, q); }, logOnce, "Chapter 3 native presentation read quest", "reading the quest", out var observed))
            return false;
        if (desired is null)
        {
            if (observed is null)
            {
                _questOutstanding = false;
                return true;
            }
            if (!TryStatus(() => _native.TryEndQuest(Chapter3QuestCopy.QuestKey), logOnce, "Chapter 3 native presentation end quest", out _))
                return false;
            _questOutstanding = false;
            OrganizedCrimeLog.Diagnostic($"Chapter 3 quest: ended {Chapter3QuestCopy.QuestTitle}.", _log);
            return true;
        }
        var desiredStates = desired.Entries.Select(entry => entry.State).ToArray();
        var desiredTexts = desired.Entries.Select(entry => entry.Text ?? string.Empty).ToArray();
        if (observed is not null && observed.EntryStates.SequenceEqual(desiredStates) &&
            _lastQuestEntryTexts is not null && _lastQuestEntryTexts.SequenceEqual(desiredTexts, StringComparer.Ordinal))
        {
            _questOutstanding = true;
            return true;
        }
        if (!TryStatus(() => _native.TryApplyQuest(desired), logOnce, "Chapter 3 native presentation apply quest", out _))
            return false;
        _questOutstanding = true;
        _lastQuestEntryTexts = desiredTexts;
        OrganizedCrimeLog.Diagnostic($"Chapter 3 quest: applied {desired.Title}.", _log);
        return true;
    }

    /// <summary>
    /// Best-effort end when Chapter 3 leaves Active with a quest outstanding.
    /// Never throws; anything short of success leaves the flag set so a later
    /// pass retries.
    /// </summary>
    private void EndOutstandingQuest()
    {
        Release1ObservedQuest? observed = null;
        try
        {
            if (_native.TryReadQuest(Chapter3QuestCopy.QuestKey, out observed) != Release1NativePresentationStatus.Succeeded)
                return;
        }
        catch { return; }
        if (observed is null)
        {
            _questOutstanding = false;
            return;
        }
        try
        {
            if (_native.TryEndQuest(Chapter3QuestCopy.QuestKey) != Release1NativePresentationStatus.Succeeded)
                return;
        }
        catch { return; }
        _questOutstanding = false;
        OrganizedCrimeLog.Diagnostic($"Chapter 3 quest: ended {Chapter3QuestCopy.QuestTitle}.", _log);
    }

    /// <summary>
    /// Applies one decision-related native call. A <see cref="Release1NativePresentationStatus.Faulted"/>
    /// result (or a thrown exception) stops the whole reconcile pass, like every other boundary call,
    /// and is logged once. <see cref="Release1NativePresentationStatus.Unavailable"/> or
    /// <see cref="Release1NativePresentationStatus.Rejected"/> (for example, a partial response bind
    /// still waiting on responses S1API has not restored yet) is treated as non-fatal for this pass
    /// and, like every other recoverable status on this boundary (see <see cref="TryStatus"/> and the
    /// class doc), is never logged: Reconcile() runs every frame, so logging a
    /// condition expected to persist across many passes would flood the log once per pass instead of
    /// once per session.
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
        return true;
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
            logOnce($"Chapter 3 native presentation reported a fault {faultDescription}.");
            return false;
        }
        return status == Release1NativePresentationStatus.Succeeded;
    }
}

/// <summary>
/// What one tapped reply did: whether the live state honored it, and a short
/// detail for the projector's reply line ("job 2", "make-good 1 (short)").
/// </summary>
public sealed record Chapter3ReplyResult(bool Handled, string Detail);

/// <summary>Sink for commands bound to a chosen Chapter 3 decision option.</summary>
public interface IChapter3PresentationCommandSink
{
    Chapter3ReplyResult TryInvoke(Chapter3PresentationCommand command);
}

/// <summary>
/// Routes a chosen Chapter 3 reply command to the runtime service method that
/// owns it. Never touches chapter state directly. Reports unhandled for a
/// stale tap (the live state no longer honors the button) or an unknown
/// command.
/// </summary>
public sealed class Chapter3PresentationCommandRouter : IChapter3PresentationCommandSink
{
    private readonly Chapter3RuntimeService _service;

    public Chapter3PresentationCommandRouter(Chapter3RuntimeService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public Chapter3ReplyResult TryInvoke(Chapter3PresentationCommand command) => command switch
    {
        Chapter3AcceptCommand => Accept(),
        Chapter3DeclineCommand => Decline(),
        Chapter3BuyTimeCommand => BuyTime(),
        Chapter3BuyWarehouseCommand => BuyWarehouse(),
        Chapter3BuyWarehouseCapacityCommand => BuyWarehouseCapacity(),
        Chapter3PayMakeGoodCommand pay => PayMakeGood(pay.MakeGoodId),
        Chapter3PayCutCommand pay => PayCut(pay.WeekIndex),
        _ => new Chapter3ReplyResult(false, "stale"),
    };

    private Chapter3ReplyResult Accept()
    {
        var result = _service.TryAcceptOffer();
        return result.Applied && result.JobId.HasValue
            ? new Chapter3ReplyResult(true, $"job {result.JobId.Value}")
            : new Chapter3ReplyResult(false, "stale");
    }

    private Chapter3ReplyResult Decline()
    {
        var result = _service.TryDeclineOffer();
        return result.Applied && result.OfferId.HasValue
            ? new Chapter3ReplyResult(true, $"offer {result.OfferId.Value} declined")
            : new Chapter3ReplyResult(false, "stale");
    }

    private Chapter3ReplyResult BuyTime()
    {
        return _service.TryBuyTime().Outcome switch
        {
            Chapter3MoneyOutcome.Applied => new Chapter3ReplyResult(true, "extension"),
            Chapter3MoneyOutcome.Short => new Chapter3ReplyResult(false, "extension (short)"),
            Chapter3MoneyOutcome.Unavailable => new Chapter3ReplyResult(false, "extension (unavailable)"),
            _ => new Chapter3ReplyResult(false, "stale"),
        };
    }

    private Chapter3ReplyResult PayMakeGood(int id)
    {
        return _service.TryPayMakeGood(id).Outcome switch
        {
            Chapter3MoneyOutcome.Applied => new Chapter3ReplyResult(true, $"make-good {id}"),
            Chapter3MoneyOutcome.Short => new Chapter3ReplyResult(false, $"make-good {id} (short)"),
            Chapter3MoneyOutcome.Unavailable => new Chapter3ReplyResult(false, $"make-good {id} (unavailable)"),
            _ => new Chapter3ReplyResult(false, "stale"),
        };
    }

    private Chapter3ReplyResult PayCut(int weekIndex)
    {
        return _service.TryPayCut(weekIndex).Outcome switch
        {
            Chapter3MoneyOutcome.Applied => new Chapter3ReplyResult(true, $"cut week {weekIndex}"),
            Chapter3MoneyOutcome.Short => new Chapter3ReplyResult(false, $"cut week {weekIndex} (short)"),
            Chapter3MoneyOutcome.Unavailable => new Chapter3ReplyResult(false, $"cut week {weekIndex} (unavailable)"),
            _ => new Chapter3ReplyResult(false, "stale"),
        };
    }

    private Chapter3ReplyResult BuyWarehouse()
    {
        return _service.TryBuyWarehouse().Outcome switch
        {
            Chapter3MoneyOutcome.Applied => new Chapter3ReplyResult(true, "warehouse bought"),
            Chapter3MoneyOutcome.Short => new Chapter3ReplyResult(false, "warehouse (short)"),
            Chapter3MoneyOutcome.Unavailable => new Chapter3ReplyResult(false, "warehouse (unavailable)"),
            _ => new Chapter3ReplyResult(false, "stale"),
        };
    }

    private Chapter3ReplyResult BuyWarehouseCapacity()
    {
        return _service.TryBuyWarehouseCapacity().Outcome switch
        {
            Chapter3MoneyOutcome.Applied => new Chapter3ReplyResult(true, "warehouse capacity bought"),
            Chapter3MoneyOutcome.Short => new Chapter3ReplyResult(false, "warehouse capacity (short)"),
            Chapter3MoneyOutcome.Unavailable => new Chapter3ReplyResult(false, "warehouse capacity (unavailable)"),
            _ => new Chapter3ReplyResult(false, "stale"),
        };
    }
}
