using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1SmallCourtesyCardStage
{
    Hidden,
    Offer,
    Review,
    NoEmptyDrop,
    PersistenceDeferred,
    DecisionRejected,
    AwaitingActivation,
    Active,
    AwaitingFirstSave,
    AwaitingSecondSave,
    AwaitingFinalSave,
    Ambiguous,
    Completed
}

public sealed record Release1SmallCourtesyViewModel(
    bool Visible,
    Release1SmallCourtesyCardStage Stage,
    string Title,
    string Body,
    bool CanReview,
    bool CanAccept,
    bool CanDefer);

public static class Release1SmallCourtesyPresentation
{
    public const string Title = "Small Courtesy";
    public const string OfferText = "One package, one drop, one time. Nothing you have not done before. Don't mess it up";
    public const string NoEmptyDropText = "Nowhere clean to put it right now. Try me again when the town has room.";
    public const string QuestionText = "Are you in?";
    public const string PaymentText = "I pay 125 percent of what you actually leave, not the asking price. Consider this good faith and don't make me regret it.";

    public static Release1SmallCourtesyViewModel Build(
        Release1StoryState? state,
        long lastPersistedRevision,
        Release1SmallCourtesyOfferStatus offerStatus,
        Release1SmallCourtesyQuote? reviewedQuote,
        string? feedback = null,
        Release1SmallCourtesyCardStage? feedbackStage = null)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
            return Visible(feedbackStage ?? Release1SmallCourtesyCardStage.PersistenceDeferred, feedback);

        if (offerStatus == Release1SmallCourtesyOfferStatus.NoEmptyDeadDrop)
            return Visible(Release1SmallCourtesyCardStage.NoEmptyDrop, NoEmptyDropText);

        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Hidden();

        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var assignment = state.SmallCourtesyAssignments
            .Where(candidate => candidate.Attempt == mission.Attempt)
            .OrderByDescending(candidate => candidate.Attempt)
            .FirstOrDefault();

        if (assignment is null)
        {
            if (reviewedQuote is not null)
                return Terms(reviewedQuote.Assignment, reviewedQuote.DeadlineDurationHours, true);
            if (offerStatus == Release1SmallCourtesyOfferStatus.Available)
                return new(true, Release1SmallCourtesyCardStage.Offer, Title, OfferText, true, false, true);
            return Hidden();
        }

        if (state.NativeEffects.Any(effect =>
                effect.MissionKey == Release1MissionCatalog.SmallCourtesy &&
                effect.Attempt == mission.Attempt &&
                effect.ExecutionBlocked))
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.Ambiguous,
                "Stopped on conflicting evidence. It will not retry.");

        if (mission.State == Release1MissionState.Accepted)
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.AwaitingActivation,
                "Accepted.");

        var cargo = state.NativeEffects.FirstOrDefault(effect =>
            effect.Attempt == mission.Attempt && string.Equals(effect.EffectKind, "CargoTransfer", StringComparison.Ordinal));
        var reward = state.NativeEffects.FirstOrDefault(effect =>
            effect.Attempt == mission.Attempt && string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal));

        if (cargo is null)
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.Active,
                "Deliver the assigned package.");

        if (cargo.Phase == Release1NativeEffectPhase.Prepared)
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.AwaitingFirstSave,
                cargo.PreparedStoryRevision > lastPersistedRevision
                    ? "Deposit recognized. Save once to complete pickup."
                    : "Deposit prepared. Waiting for reconciliation.");

        if (cargo.Phase == Release1NativeEffectPhase.Applied ||
            reward is { Phase: Release1NativeEffectPhase.Prepared })
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.AwaitingSecondSave,
                "Item accepted. Save again to authorize payment.");

        if (reward is { Phase: Release1NativeEffectPhase.Applied })
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.AwaitingFinalSave,
                "Payment delivered. Save once more to finalize.");

        if (reward is { Phase: Release1NativeEffectPhase.Committed } ||
            mission.State == Release1MissionState.Satisfied && cargo.Phase == Release1NativeEffectPhase.Committed)
            return Terms(assignment, DeadlineDuration(mission), false,
                Release1SmallCourtesyCardStage.Completed,
                "Complete. Delivery and payment are saved.");

        return Terms(assignment, DeadlineDuration(mission), false,
            Release1SmallCourtesyCardStage.Active,
            "Delivery active.");
    }

    private static Release1SmallCourtesyViewModel Terms(
        Release1SmallCourtesyAssignment assignment,
        double? deadlineHours,
        bool review,
        Release1SmallCourtesyCardStage? stage = null,
        string? status = null)
    {
        var deadline = Release1PlayerCopy.Deadline(deadlineHours);
        var header = Release1PlayerCopy.AttemptHeader(assignment.Mode, assignment.Attempt);
        var body = review
            ? Copy(Release1PlayerCopy.JoinLines(
                header,
                $"1 {assignment.PackagingName} of {assignment.ProductName}",
                $"Drop: {assignment.DeadDropName}, empty when I picked it.",
                $"Deadline: {deadline}",
                PaymentText,
                QuestionText))
            : Copy(Release1PlayerCopy.JoinLines(
                $"Status: {status ?? "Delivery active."}",
                $"Deliver: 1 {assignment.PackagingName} of {assignment.ProductName}",
                $"Drop: {assignment.DeadDropName}",
                $"Deadline: {deadline}",
                PaymentText));
        return new(true, stage ?? Release1SmallCourtesyCardStage.Review, Title, body,
            false, review, review);
    }

    private static double? DeadlineDuration(Release1MissionRecord mission) =>
        mission.DeadlineGameTimeHours is null || mission.AcceptedGameTimeHours is null
            ? null
            : mission.DeadlineGameTimeHours.Value - mission.AcceptedGameTimeHours.Value;

    private static Release1SmallCourtesyViewModel Visible(Release1SmallCourtesyCardStage stage, string body) =>
        new(true, stage, Title, body, false, false, false);

    private static Release1SmallCourtesyViewModel Hidden() =>
        new(false, Release1SmallCourtesyCardStage.Hidden, Title, string.Empty, false, false, false);

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}

public sealed class Release1SmallCourtesyPresenter : IDisposable
{
    private readonly Release1SmallCourtesyMissionService _service;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private string? _feedback;
    private Release1SmallCourtesyCardStage? _feedbackStage;
    private bool _disposed;

    public Release1SmallCourtesyPresenter(
        Release1SmallCourtesyMissionService service,
        Release1StoryRuntimeService story,
        Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _log = log;
    }

    public Release1SmallCourtesyViewModel View => _disposed
        ? Release1SmallCourtesyPresentation.Build(null, -1, Release1SmallCourtesyOfferStatus.Disposed, null)
        : Release1SmallCourtesyPresentation.Build(
            _story.State,
            _story.LastPersistedRevision,
            _service.OfferStatus,
            _service.ReviewedQuote,
            _feedback,
            _feedbackStage);

    public Release1DecisionPromptCard? DecisionPrompt
    {
        get
        {
            var view = View;
            if (!view.Visible) return null;
            if (view.CanReview)
            {
                return new(
                    "release1-small-courtesy-review",
                    "Small Courtesy is ready for review.",
                    view.Title,
                    view.Body,
                    "Review terms",
                    () => TryReview());
            }

            if (view.CanAccept)
            {
                return new(
                    "release1-small-courtesy-decision",
                    "Small Courtesy is waiting for your decision.",
                    view.Title,
                    view.Body,
                    "Accept",
                    () => TryAccept(),
                    "Not now",
                    () => TryDefer());
            }

            return null;
        }
    }

    public Release1SmallCourtesyReviewResult TryReview()
    {
        var result = _service.TryReview();
        _feedback = result.Status == Release1SmallCourtesyReviewStatus.Ready ? null : result.Message;
        _feedbackStage = _feedback is null
            ? null
            : result.Status == Release1SmallCourtesyReviewStatus.NoEmptyDeadDrop
                ? Release1SmallCourtesyCardStage.NoEmptyDrop
                : Release1SmallCourtesyCardStage.DecisionRejected;
        if (_feedback is not null) _log?.Invoke($"Small Courtesy review failed: {result.Status} — {result.Message}");
        return result;
    }

    public Release1SmallCourtesyDecisionResult TryAccept() => Handle(_service.TryAccept());
    public Release1SmallCourtesyDecisionResult TryDefer() => Handle(_service.TryDefer());

    public void ClearFeedback()
    {
        _feedback = null;
        _feedbackStage = null;
    }

    public void Dispose()
    {
        _feedback = null;
        _feedbackStage = null;
        _disposed = true;
    }

    private Release1SmallCourtesyDecisionResult Handle(Release1SmallCourtesyDecisionResult result)
    {
        if (result.Status is Release1SmallCourtesyDecisionStatus.Accepted or
            Release1SmallCourtesyDecisionStatus.Deferred or
            Release1SmallCourtesyDecisionStatus.Dismissed)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else
        {
            _feedback = result.Message;
            _feedbackStage = result.Status == Release1SmallCourtesyDecisionStatus.PersistenceDeferred
                ? Release1SmallCourtesyCardStage.PersistenceDeferred
                : Release1SmallCourtesyCardStage.DecisionRejected;
            _log?.Invoke($"Small Courtesy decision failed: {result.Status} — {result.Message}");
        }
        return result;
    }
}
