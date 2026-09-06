using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1ShortNoticeCardStage
{
    Hidden, Offer, Review, NoDrop, PersistenceDeferred, DecisionRejected,
    AwaitingActivation, Active, Shortfall, Spread, Ambiguous, Completed
}

public sealed record Release1ShortNoticeViewModel(
    bool Visible,
    Release1ShortNoticeCardStage Stage,
    string Title,
    string Body,
    bool CanReview,
    bool CanAccept,
    bool CanDefer);

public static class Release1ShortNoticePresentation
{
    public const string Title = "Short Notice";
    public const string OfferText = "I need product tonight, not next week. One drop, one slot, and a window that closes fast. Do you have the balls?";
    public const string RefusalText = "I need one clear drop before I can set this up. Try me again when the town has room.";
    public const string QuestionText = "Are you taking it?";
    public const string SourceText = "It comes out of your own stock. I am not sending you any. Prove your own worth, I'm not holding your hand like a baby.";
    public const string PaymentText = "I pay 175 percent of what you leave, the moment it lands. Be happy I give you fucking anything.";
    public const string ActiveStatusText = "Leave the whole order in one slot at the drop before the window closes.";
    public const string ShortfallStatusManyFormat = "Part of the order is there. {0} more still needed.";
    public const string ShortfallStatusOneText = "Part of the order is there. One more still needed.";
    public const string SpreadStatusText = "The order is in more than one slot. Put it together.";
    public const string CompletedStatusText = "Complete. Delivered and paid.";
    public const string AmbiguousStatusText = "Stopped on conflicting evidence. It will not retry.";

    /// <summary>Renders a manifest count with its packaging name, pluralized by count only.</summary>
    public static string Units(int count, string packagingName) =>
        count == 1 ? $"{count} {packagingName}" : $"{count} {packagingName}s";

    public static Release1ShortNoticeViewModel Build(
        Release1StoryState? state,
        Release1ShortNoticeOfferStatus offerStatus,
        Release1ShortNoticeQuote? reviewedQuote,
        Release1ShortNoticeProgress? progress,
        string? feedback = null,
        Release1ShortNoticeCardStage? feedbackStage = null)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
            return Visible(feedbackStage ?? Release1ShortNoticeCardStage.PersistenceDeferred, Copy(feedback));

        if (offerStatus == Release1ShortNoticeOfferStatus.NoEmptyDeadDrop)
            return new(true, Release1ShortNoticeCardStage.NoDrop, Title, Copy(RefusalText), true, false, false);

        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return Hidden();

        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
        var expectedMode = Release1ShortNoticeMissionService.ExpectedAssignmentMode(mission.State);
        var assignment = state.ShortNoticeAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == mission.Attempt && (expectedMode is null || candidate.Mode == expectedMode.Value));

        if (assignment is null)
        {
            if (reviewedQuote is not null) return Terms(reviewedQuote.Assignment, reviewedQuote.DeadlineDurationHours);
            if (offerStatus == Release1ShortNoticeOfferStatus.Available)
                return new(true, Release1ShortNoticeCardStage.Offer, Title, Copy(OfferText), true, false, false);
            return Hidden();
        }

        if (state.NativeEffects.Any(effect =>
                effect.MissionKey == Release1MissionCatalog.ShortNotice &&
                effect.Attempt == mission.Attempt &&
                effect.ExecutionBlocked))
            return Active(assignment, mission, Release1ShortNoticeCardStage.Ambiguous, AmbiguousStatusText);

        if (mission.State == Release1MissionState.Satisfied)
            return Active(assignment, mission, Release1ShortNoticeCardStage.Completed, CompletedStatusText);

        if (mission.State == Release1MissionState.Accepted)
            return Active(assignment, mission, Release1ShortNoticeCardStage.AwaitingActivation, ActiveStatusText);

        if (progress?.SpreadNoticed == true)
            return Active(assignment, mission, Release1ShortNoticeCardStage.Spread, SpreadStatusText);

        if (progress?.LastShortfallNoticed is { } remaining)
            return Active(assignment, mission, Release1ShortNoticeCardStage.Shortfall,
                remaining == 1 ? ShortfallStatusOneText : string.Format(ShortfallStatusManyFormat, remaining));

        return Active(assignment, mission, Release1ShortNoticeCardStage.Active, ActiveStatusText);
    }

    private static Release1ShortNoticeViewModel Terms(Release1ShortNoticeAssignment assignment, double? deadlineHours) =>
        new(true, Release1ShortNoticeCardStage.Review, Title, Copy(Release1PlayerCopy.JoinLines(
            Release1PlayerCopy.AttemptHeader(assignment.Mode, assignment.Attempt),
            $"{Units(assignment.RequiredQuantity, assignment.PackagingName)} of {assignment.ProductName}",
            $"Drop: {assignment.HandoffDropName}. All of it in one slot. It's not that complicated.",
            $"Deadline: {Release1PlayerCopy.Deadline(deadlineHours)}",
            PaymentText,
            SourceText,
            QuestionText)), false, true, true);

    private static Release1ShortNoticeViewModel Active(
        Release1ShortNoticeAssignment assignment,
        Release1MissionRecord mission,
        Release1ShortNoticeCardStage stage,
        string status) =>
        new(true, stage, Title, Copy(string.Join("\n",
            $"Status: {status}",
            $"{Units(assignment.RequiredQuantity, assignment.PackagingName)} of {assignment.ProductName}",
            $"Drop: {assignment.HandoffDropName}",
            $"Deadline: {Release1PlayerCopy.Deadline(DeadlineDuration(mission))}",
            PaymentText)), false, false, false);

    private static double? DeadlineDuration(Release1MissionRecord mission) =>
        mission.DeadlineGameTimeHours is null || mission.AcceptedGameTimeHours is null
            ? null
            : mission.DeadlineGameTimeHours.Value - mission.AcceptedGameTimeHours.Value;

    private static Release1ShortNoticeViewModel Visible(Release1ShortNoticeCardStage stage, string body) =>
        new(true, stage, Title, body, false, false, false);

    private static Release1ShortNoticeViewModel Hidden() =>
        new(false, Release1ShortNoticeCardStage.Hidden, Title, string.Empty, false, false, false);

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}

public sealed class Release1ShortNoticePresenter : IDisposable
{
    private readonly Release1ShortNoticeMissionService _service;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private string? _feedback;
    private Release1ShortNoticeCardStage? _feedbackStage;
    private bool _disposed;

    public Release1ShortNoticePresenter(
        Release1ShortNoticeMissionService service,
        Release1StoryRuntimeService story,
        Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _log = log;
    }

    public Release1ShortNoticeViewModel View => _disposed
        ? Release1ShortNoticePresentation.Build(null, Release1ShortNoticeOfferStatus.Disposed, null, null)
        : Release1ShortNoticePresentation.Build(
            _story.State,
            _service.OfferStatus,
            _service.ReviewedQuote,
            CurrentProgress(),
            _feedback,
            _feedbackStage);

    public Release1ShortNoticeReviewResult TryReview()
    {
        var result = _service.TryReview();
        _feedback = result.Status == Release1ShortNoticeReviewStatus.Ready ? null : result.Message;
        _feedbackStage = _feedback is null
            ? null
            : result.Status == Release1ShortNoticeReviewStatus.NoEmptyDeadDrop
                ? Release1ShortNoticeCardStage.NoDrop
                : Release1ShortNoticeCardStage.DecisionRejected;
        if (_feedback is not null) _log?.Invoke($"Short Notice review failed: {result.Status}, {result.Message}");
        return result;
    }

    public Release1ShortNoticeDecisionResult TryAccept() => Handle(_service.TryAccept());
    public Release1ShortNoticeDecisionResult TryDefer() => Handle(_service.TryDefer());

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

    private Release1ShortNoticeProgress? CurrentProgress()
    {
        var state = _story.State;
        if (state is null) return null;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
        return state.ShortNoticeProgress.SingleOrDefault(progress => progress.Attempt == mission.Attempt);
    }

    private Release1ShortNoticeDecisionResult Handle(Release1ShortNoticeDecisionResult result)
    {
        if (result.Status is Release1ShortNoticeDecisionStatus.Accepted or
            Release1ShortNoticeDecisionStatus.Deferred or
            Release1ShortNoticeDecisionStatus.Dismissed)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else if (result.Status == Release1ShortNoticeDecisionStatus.NoEmptyDeadDrop)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else
        {
            _feedback = result.Message;
            _feedbackStage = result.Status == Release1ShortNoticeDecisionStatus.PersistenceDeferred
                ? Release1ShortNoticeCardStage.PersistenceDeferred
                : Release1ShortNoticeCardStage.DecisionRejected;
            _log?.Invoke($"Short Notice decision failed: {result.Status}, {result.Message}");
        }
        return result;
    }
}
