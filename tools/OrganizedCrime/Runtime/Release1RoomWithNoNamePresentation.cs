using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1RoomWithNoNameCardStage
{
    Hidden, Offer, Review, InsufficientDrops, PersistenceDeferred, DecisionRejected,
    AwaitingActivation, Active, InCustody, Stowed, Released, Ambiguous, Completed
}

public sealed record Release1RoomWithNoNameViewModel(
    bool Visible,
    Release1RoomWithNoNameCardStage Stage,
    string Title,
    string Body,
    bool CanReview,
    bool CanAccept,
    bool CanDefer);

public static class Release1RoomWithNoNamePresentation
{
    public const string Title = "A Room With No Name";
    public const string OfferText = "I have shit that needs to sit somewhere quiet for a day. Don't even think about selling it, or you will hear from Arthur.";
    public const string RefusalText = "I need two clear drops before I can set this up. Try me again when the town has room.";
    public const string QuestionText = "Are you taking it?";
    public const string PaymentText = "I pay 150 percent of the value the moment you hand it over. Fuck it up and you get nothing.";

    public static Release1RoomWithNoNameViewModel Build(
        Release1StoryState? state,
        Release1RoomWithNoNameOfferStatus offerStatus,
        Release1RoomWithNoNameQuote? reviewedQuote,
        Release1RoomWithNoNameProgress? progress,
        string? feedback = null,
        Release1RoomWithNoNameCardStage? feedbackStage = null)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
            return Visible(feedbackStage ?? Release1RoomWithNoNameCardStage.PersistenceDeferred, Copy(feedback));

        if (offerStatus == Release1RoomWithNoNameOfferStatus.InsufficientEmptyDeadDrops)
            return new(true, Release1RoomWithNoNameCardStage.InsufficientDrops, Title, Copy(RefusalText), true, false, false);

        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return Hidden();

        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];
        var expectedMode = Release1RoomWithNoNameMissionService.ExpectedAssignmentMode(mission.State);
        var assignment = state.RoomWithNoNameAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == mission.Attempt && (expectedMode is null || candidate.Mode == expectedMode.Value));

        if (assignment is null)
        {
            if (reviewedQuote is not null) return Terms(reviewedQuote.Assignment, reviewedQuote.DeadlineDurationHours);
            if (offerStatus == Release1RoomWithNoNameOfferStatus.Available)
                return new(true, Release1RoomWithNoNameCardStage.Offer, Title, Copy(OfferText), true, false, false);
            return Hidden();
        }

        if (state.NativeEffects.Any(effect =>
                effect.MissionKey == Release1MissionCatalog.RoomWithNoName &&
                effect.Attempt == mission.Attempt &&
                effect.ExecutionBlocked))
            return Active(assignment, mission, Release1RoomWithNoNameCardStage.Ambiguous,
                "Stopped on conflicting evidence. It will not retry.");

        if (mission.State == Release1MissionState.Satisfied)
            return Active(assignment, mission, Release1RoomWithNoNameCardStage.Completed, "Complete. Delivered and paid.");

        if (mission.State == Release1MissionState.Accepted)
            return Active(assignment, mission, Release1RoomWithNoNameCardStage.AwaitingActivation,
                "Collect it from the pick up.");

        if (progress?.HoldSatisfied == true)
            return Active(assignment, mission, Release1RoomWithNoNameCardStage.Released,
                "The hold is done. Take it to the drop off.");
        if (progress?.Stowed == true)
            return Active(assignment, mission, Release1RoomWithNoNameCardStage.Stowed,
                "It stays in the room. One day.");
        if (progress?.Custody == true)
            return Active(assignment, mission, Release1RoomWithNoNameCardStage.InCustody,
                "Take it to the room and leave it there. No fucking around on the way there.");
        return Active(assignment, mission, Release1RoomWithNoNameCardStage.Active,
            "Collect it from the pick up.");
    }

    private static Release1RoomWithNoNameViewModel Terms(Release1RoomWithNoNameAssignment assignment, double? deadlineHours) =>
        new(true, Release1RoomWithNoNameCardStage.Review, Title, Copy(Release1PlayerCopy.JoinLines(
            Release1PlayerCopy.AttemptHeader(assignment.Mode, assignment.Attempt),
            $"1 {assignment.PackagingName} of {assignment.ProductName}",
            $"Pick up: {assignment.SourceDropName}. Drop off: {assignment.HandoffDropName}.",
            "Leave it alone in the storage room at HQ for one day.",
            $"Deadline: {Release1PlayerCopy.Deadline(deadlineHours)}",
            PaymentText,
            QuestionText)), false, true, true);

    private static Release1RoomWithNoNameViewModel Active(
        Release1RoomWithNoNameAssignment assignment,
        Release1MissionRecord mission,
        Release1RoomWithNoNameCardStage stage,
        string status) =>
        new(true, stage, Title, Copy(string.Join("\n",
            $"Status: {status}",
            $"1 {assignment.PackagingName} of {assignment.ProductName}",
            "Room: the storage room at HQ",
            $"Drop off: {assignment.HandoffDropName}",
            $"Deadline: {Release1PlayerCopy.Deadline(DeadlineDuration(mission))}",
            PaymentText)), false, false, false);

    private static double? DeadlineDuration(Release1MissionRecord mission) =>
        mission.DeadlineGameTimeHours is null || mission.AcceptedGameTimeHours is null
            ? null
            : mission.DeadlineGameTimeHours.Value - mission.AcceptedGameTimeHours.Value;

    private static Release1RoomWithNoNameViewModel Visible(Release1RoomWithNoNameCardStage stage, string body) =>
        new(true, stage, Title, body, false, false, false);

    private static Release1RoomWithNoNameViewModel Hidden() =>
        new(false, Release1RoomWithNoNameCardStage.Hidden, Title, string.Empty, false, false, false);

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}

public sealed class Release1RoomWithNoNamePresenter : IDisposable
{
    private readonly Release1RoomWithNoNameMissionService _service;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private string? _feedback;
    private Release1RoomWithNoNameCardStage? _feedbackStage;
    private bool _disposed;

    public Release1RoomWithNoNamePresenter(
        Release1RoomWithNoNameMissionService service,
        Release1StoryRuntimeService story,
        Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _log = log;
    }

    public Release1RoomWithNoNameViewModel View => _disposed
        ? Release1RoomWithNoNamePresentation.Build(null, Release1RoomWithNoNameOfferStatus.Disposed, null, null)
        : Release1RoomWithNoNamePresentation.Build(
            _story.State,
            _service.OfferStatus,
            _service.ReviewedQuote,
            CurrentProgress(),
            _feedback,
            _feedbackStage);

    public Release1RoomWithNoNameReviewResult TryReview()
    {
        var result = _service.TryReview();
        _feedback = result.Status == Release1RoomWithNoNameReviewStatus.Ready ? null : result.Message;
        _feedbackStage = _feedback is null
            ? null
            : result.Status == Release1RoomWithNoNameReviewStatus.InsufficientEmptyDeadDrops
                ? Release1RoomWithNoNameCardStage.InsufficientDrops
                : Release1RoomWithNoNameCardStage.DecisionRejected;
        if (_feedback is not null) _log?.Invoke($"Room With No Name review failed: {result.Status}, {result.Message}");
        return result;
    }

    public Release1RoomWithNoNameDecisionResult TryAccept() => Handle(_service.TryAccept());
    public Release1RoomWithNoNameDecisionResult TryDefer() => Handle(_service.TryDefer());

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

    private Release1RoomWithNoNameProgress? CurrentProgress()
    {
        var state = _story.State;
        if (state is null) return null;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];
        return state.RoomWithNoNameProgress.SingleOrDefault(progress => progress.Attempt == mission.Attempt);
    }

    private Release1RoomWithNoNameDecisionResult Handle(Release1RoomWithNoNameDecisionResult result)
    {
        if (result.Status is Release1RoomWithNoNameDecisionStatus.Accepted or
            Release1RoomWithNoNameDecisionStatus.Deferred or
            Release1RoomWithNoNameDecisionStatus.Dismissed)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else if (result.Status == Release1RoomWithNoNameDecisionStatus.InsufficientEmptyDeadDrops)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else
        {
            _feedback = result.Message;
            _feedbackStage = result.Status == Release1RoomWithNoNameDecisionStatus.PersistenceDeferred
                ? Release1RoomWithNoNameCardStage.PersistenceDeferred
                : Release1RoomWithNoNameCardStage.DecisionRejected;
            _log?.Invoke($"Room With No Name decision failed: {result.Status}, {result.Message}");
        }
        return result;
    }
}
