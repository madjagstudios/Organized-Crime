using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1KeepTheLightsOffCardStage
{
    Hidden, Offer, Review, PersistenceDeferred, DecisionRejected,
    AwaitingActivation, Running, Holding, Breach, Ambiguous, Completed
}

public sealed record Release1KeepTheLightsOffViewModel(
    bool Visible,
    Release1KeepTheLightsOffCardStage Stage,
    string Title,
    string Body,
    bool CanReview,
    bool CanAccept,
    bool CanDefer);

/// <summary>
/// Builds the Keep the Lights Off card view model, mirroring <see cref="Release1ShortNoticePresentation"/>
/// exactly in shape, with the census state (rather than a measured shortfall count) driving the active
/// stages: Running before the first all-clear pass, Holding once the clear is confirmed and the window
/// is running clean, and Breach when product has been seen again with the grace window running. Nothing
/// is ever selected at any drop or closet for this mission, so unlike Short Notice there is no NoDrop
/// refusal stage and no location line anywhere in the card.
/// </summary>
public static class Release1KeepTheLightsOffPresentation
{
    public const string Title = "Keep the Lights Off";
    public const string OfferText = "You need to go quiet. Period. This is for your own good, be grateful we are warning you.";
    public const string QuestionText = "Are you taking it?";
    public const string ActionText = "Nobody works. Stop all production. Every property, no noise, no grunts working. Nothing.";
    public const string HoldText = "Then nothing moves for one day.";
    public const string OwnershipText = "Don't trash product. Just stop working.";
    public const string PaymentText = "I may help you after. I'll think about it.";
    public const string RunningStatusText = "You and your employees all need to stop working.";
    public const string HoldingStatusText = "Hold quiet. Nothing moves until the day is up.";
    public const string BreachStatusText = "You messed that up. We pulled some strings to give you another chance. Stop production now.";
    public const string CompletedText = "One quiet day. Thank god, you dodged a bullet there.";
    public const string AmbiguousStatusText = "Stopped on conflicting evidence. It will not retry.";

    public static Release1KeepTheLightsOffViewModel Build(
        Release1StoryState? state,
        Release1KeepTheLightsOffOfferStatus offerStatus,
        Release1KeepTheLightsOffQuote? reviewedQuote,
        Release1KeepTheLightsOffProgress? progress,
        string? feedback = null,
        Release1KeepTheLightsOffCardStage? feedbackStage = null)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
            return Visible(feedbackStage ?? Release1KeepTheLightsOffCardStage.PersistenceDeferred, Copy(feedback));

        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return Hidden();

        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        // Decision 14: the v9 to v10 migration empties the assignment collection, so a Satisfied attempt
        // has no assignment to look up. This case never reads one.
        if (mission.State == Release1MissionState.Satisfied)
            return new(true, Release1KeepTheLightsOffCardStage.Completed, Title, Copy(CompletedText), false, false, false);

        var expectedMode = Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(mission.State);
        var assignment = state.KeepTheLightsOffAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == mission.Attempt && (expectedMode is null || candidate.Mode == expectedMode.Value));

        if (assignment is null)
        {
            if (reviewedQuote is not null) return Terms(reviewedQuote.Assignment, reviewedQuote.DeadlineDurationHours);
            if (offerStatus == Release1KeepTheLightsOffOfferStatus.Available)
                return new(true, Release1KeepTheLightsOffCardStage.Offer, Title, Copy(OfferText), true, false, false);
            return Hidden();
        }

        if (state.NativeEffects.Any(effect =>
                effect.MissionKey == Release1MissionCatalog.KeepTheLightsOff &&
                effect.Attempt == mission.Attempt &&
                effect.ExecutionBlocked))
            return Active(assignment, mission, Release1KeepTheLightsOffCardStage.Ambiguous, AmbiguousStatusText);

        if (mission.State == Release1MissionState.Accepted)
            return Active(assignment, mission, Release1KeepTheLightsOffCardStage.AwaitingActivation, RunningStatusText);

        if (progress?.BreachSincePassGameMinutes is not null)
            return Active(assignment, mission, Release1KeepTheLightsOffCardStage.Breach, BreachStatusText);

        if (progress?.ClearConfirmedAtGameMinutes is not null)
            return Active(assignment, mission, Release1KeepTheLightsOffCardStage.Holding, HoldingStatusText);

        return Active(assignment, mission, Release1KeepTheLightsOffCardStage.Running, RunningStatusText);
    }

    private static Release1KeepTheLightsOffViewModel Terms(Release1KeepTheLightsOffAssignment assignment, double? deadlineHours) =>
        new(true, Release1KeepTheLightsOffCardStage.Review, Title, Copy(Release1PlayerCopy.JoinLines(
            Release1PlayerCopy.AttemptHeader(assignment.Mode, assignment.Attempt),
            ActionText,
            HoldText,
            $"Deadline: {Release1PlayerCopy.Deadline(deadlineHours)}",
            PaymentText,
            OwnershipText,
            QuestionText)), false, true, true);

    private static Release1KeepTheLightsOffViewModel Active(
        Release1KeepTheLightsOffAssignment assignment,
        Release1MissionRecord mission,
        Release1KeepTheLightsOffCardStage stage,
        string status) =>
        new(true, stage, Title, Copy(string.Join("\n",
            $"Status: {status}",
            ActionText,
            $"Deadline: {Release1PlayerCopy.Deadline(DeadlineDuration(mission))}",
            PaymentText)), false, false, false);

    private static double? DeadlineDuration(Release1MissionRecord mission) =>
        mission.DeadlineGameTimeHours is null || mission.AcceptedGameTimeHours is null
            ? null
            : mission.DeadlineGameTimeHours.Value - mission.AcceptedGameTimeHours.Value;

    private static Release1KeepTheLightsOffViewModel Visible(Release1KeepTheLightsOffCardStage stage, string body) =>
        new(true, stage, Title, body, false, false, false);

    private static Release1KeepTheLightsOffViewModel Hidden() =>
        new(false, Release1KeepTheLightsOffCardStage.Hidden, Title, string.Empty, false, false, false);

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}

public sealed class Release1KeepTheLightsOffPresenter : IDisposable
{
    private readonly Release1KeepTheLightsOffMissionService _service;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private string? _feedback;
    private Release1KeepTheLightsOffCardStage? _feedbackStage;
    private bool _disposed;

    public Release1KeepTheLightsOffPresenter(
        Release1KeepTheLightsOffMissionService service,
        Release1StoryRuntimeService story,
        Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _log = log;
    }

    public Release1KeepTheLightsOffViewModel View => _disposed
        ? Release1KeepTheLightsOffPresentation.Build(null, Release1KeepTheLightsOffOfferStatus.Disposed, null, null)
        : Release1KeepTheLightsOffPresentation.Build(
            _story.State,
            _service.OfferStatus,
            _service.ReviewedQuote,
            CurrentProgress(),
            _feedback,
            _feedbackStage);

    public Release1KeepTheLightsOffReviewResult TryReview()
    {
        var result = _service.TryReview();
        _feedback = result.Status == Release1KeepTheLightsOffReviewStatus.Ready ? null : result.Message;
        _feedbackStage = _feedback is null ? null : Release1KeepTheLightsOffCardStage.DecisionRejected;
        if (_feedback is not null) _log?.Invoke($"Keep the Lights Off review failed: {result.Status}, {result.Message}");
        return result;
    }

    public Release1KeepTheLightsOffDecisionResult TryAccept() => Handle(_service.TryAccept());
    public Release1KeepTheLightsOffDecisionResult TryDefer() => Handle(_service.TryDefer());

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

    private Release1KeepTheLightsOffProgress? CurrentProgress()
    {
        var state = _story.State;
        if (state is null) return null;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        return state.KeepTheLightsOffProgress.SingleOrDefault(progress => progress.Attempt == mission.Attempt);
    }

    private Release1KeepTheLightsOffDecisionResult Handle(Release1KeepTheLightsOffDecisionResult result)
    {
        if (result.Status is Release1KeepTheLightsOffDecisionStatus.Accepted or
            Release1KeepTheLightsOffDecisionStatus.Deferred or
            Release1KeepTheLightsOffDecisionStatus.Dismissed)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else
        {
            _feedback = result.Message;
            _feedbackStage = result.Status == Release1KeepTheLightsOffDecisionStatus.PersistenceDeferred
                ? Release1KeepTheLightsOffCardStage.PersistenceDeferred
                : Release1KeepTheLightsOffCardStage.DecisionRejected;
            _log?.Invoke($"Keep the Lights Off decision failed: {result.Status}, {result.Message}");
        }
        return result;
    }
}
