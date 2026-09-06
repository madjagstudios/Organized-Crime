using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1WrongAddressCardStage
{
    Hidden, Offer, Review, InsufficientDrops, PersistenceDeferred, DecisionRejected,
    AwaitingActivation, Active, InCustody, Ambiguous, Completed
}

public sealed record Release1WrongAddressViewModel(
    bool Visible,
    Release1WrongAddressCardStage Stage,
    string Title,
    string Body,
    bool CanReview,
    bool CanAccept,
    bool CanDefer);

public static class Release1WrongAddressPresentation
{
    public const string Title = "Wrong Address";
    public const string OfferText = "Some fucking moron delivered to the wrong address. They have been taken care of, of course. I need you to fix his fuckup.";
    public const string RefusalText = "I need two clear drops before I can point you at one. Try me again when the town has room.";
    public const string QuestionText = "Do you have the balls?";
    public const string PaymentText = "We pay 125 percent of the package value the moment you hand it over.";

    public static Release1WrongAddressViewModel Build(
        Release1StoryState? state,
        Release1WrongAddressOfferStatus offerStatus,
        Release1WrongAddressQuote? reviewedQuote,
        Release1WrongAddressProgress? progress,
        string? feedback = null,
        Release1WrongAddressCardStage? feedbackStage = null)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
            return Visible(feedbackStage ?? Release1WrongAddressCardStage.PersistenceDeferred, Copy(feedback));

        if (offerStatus == Release1WrongAddressOfferStatus.InsufficientEmptyDeadDrops)
            return new(true, Release1WrongAddressCardStage.InsufficientDrops, Title, Copy(RefusalText), true, false, false);

        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return Hidden();

        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
        var expectedMode = Release1WrongAddressMissionService.ExpectedAssignmentMode(mission.State);
        var assignment = state.WrongAddressAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == mission.Attempt && (expectedMode is null || candidate.Mode == expectedMode.Value));

        if (assignment is null)
        {
            if (reviewedQuote is not null) return Terms(reviewedQuote.Assignment, reviewedQuote.DeadlineDurationHours);
            if (offerStatus == Release1WrongAddressOfferStatus.Available)
                return new(true, Release1WrongAddressCardStage.Offer, Title, Copy(OfferText), true, false, false);
            return Hidden();
        }

        if (state.NativeEffects.Any(effect =>
                effect.MissionKey == Release1MissionCatalog.WrongAddress &&
                effect.Attempt == mission.Attempt &&
                effect.ExecutionBlocked))
            return Active(assignment, mission, Release1WrongAddressCardStage.Ambiguous,
                "Stopped on conflicting evidence. It will not retry.");

        if (mission.State == Release1MissionState.Satisfied)
            return Active(assignment, mission, Release1WrongAddressCardStage.Completed, "You found a way to not fuck it up. Delivered and paid.");

        if (mission.State == Release1MissionState.Accepted)
            return Active(assignment, mission, Release1WrongAddressCardStage.AwaitingActivation,
                "You twat. Collect the package from the wrong address.");

        return progress?.Custody == true
            ? Active(assignment, mission, Release1WrongAddressCardStage.InCustody,
                "Finish the fucking job and take it to the right address.")
            : Active(assignment, mission, Release1WrongAddressCardStage.Active,
                "You twat. Collect the package from the wrong address.");
    }

    private static Release1WrongAddressViewModel Terms(Release1WrongAddressAssignment assignment, double? deadlineHours) =>
        new(true, Release1WrongAddressCardStage.Review, Title, Copy(Release1PlayerCopy.JoinLines(
            Release1PlayerCopy.AttemptHeader(assignment.Mode, assignment.Attempt),
            $"1 {assignment.PackagingName} of {assignment.ProductName}",
            $"Pick up: {assignment.SourceDropName}. Drop off: {assignment.HandoffDropName}.",
            $"Deadline: {Release1PlayerCopy.Deadline(deadlineHours)}",
            PaymentText,
            QuestionText)), false, true, true);

    private static Release1WrongAddressViewModel Active(
        Release1WrongAddressAssignment assignment,
        Release1MissionRecord mission,
        Release1WrongAddressCardStage stage,
        string status) =>
        new(true, stage, Title, Copy(string.Join("\n",
            $"Status: {status}",
            $"1 {assignment.PackagingName} of {assignment.ProductName}",
            $"Pick up: {assignment.SourceDropName}",
            $"Drop off: {assignment.HandoffDropName}",
            $"Deadline: {Release1PlayerCopy.Deadline(DeadlineDuration(mission))}",
            PaymentText)), false, false, false);

    private static double? DeadlineDuration(Release1MissionRecord mission) =>
        mission.DeadlineGameTimeHours is null || mission.AcceptedGameTimeHours is null
            ? null
            : mission.DeadlineGameTimeHours.Value - mission.AcceptedGameTimeHours.Value;

    private static Release1WrongAddressViewModel Visible(Release1WrongAddressCardStage stage, string body) =>
        new(true, stage, Title, body, false, false, false);

    private static Release1WrongAddressViewModel Hidden() =>
        new(false, Release1WrongAddressCardStage.Hidden, Title, string.Empty, false, false, false);

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}

public sealed class Release1WrongAddressPresenter : IDisposable
{
    private readonly Release1WrongAddressMissionService _service;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private string? _feedback;
    private Release1WrongAddressCardStage? _feedbackStage;
    private bool _disposed;

    public Release1WrongAddressPresenter(
        Release1WrongAddressMissionService service,
        Release1StoryRuntimeService story,
        Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _log = log;
    }

    public Release1WrongAddressViewModel View => _disposed
        ? Release1WrongAddressPresentation.Build(null, Release1WrongAddressOfferStatus.Disposed, null, null)
        : Release1WrongAddressPresentation.Build(
            _story.State,
            _service.OfferStatus,
            _service.ReviewedQuote,
            CurrentProgress(),
            _feedback,
            _feedbackStage);

    public Release1WrongAddressReviewResult TryReview()
    {
        var result = _service.TryReview();
        _feedback = result.Status == Release1WrongAddressReviewStatus.Ready ? null : result.Message;
        _feedbackStage = _feedback is null
            ? null
            : result.Status == Release1WrongAddressReviewStatus.InsufficientEmptyDeadDrops
                ? Release1WrongAddressCardStage.InsufficientDrops
                : Release1WrongAddressCardStage.DecisionRejected;
        if (_feedback is not null) _log?.Invoke($"Wrong Address review failed: {result.Status}, {result.Message}");
        return result;
    }

    public Release1WrongAddressDecisionResult TryAccept() => Handle(_service.TryAccept());
    public Release1WrongAddressDecisionResult TryDefer() => Handle(_service.TryDefer());

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

    private Release1WrongAddressProgress? CurrentProgress()
    {
        var state = _story.State;
        if (state is null) return null;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
        return state.WrongAddressProgress.SingleOrDefault(progress => progress.Attempt == mission.Attempt);
    }

    private Release1WrongAddressDecisionResult Handle(Release1WrongAddressDecisionResult result)
    {
        if (result.Status is Release1WrongAddressDecisionStatus.Accepted or
            Release1WrongAddressDecisionStatus.Deferred or
            Release1WrongAddressDecisionStatus.Dismissed)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else if (result.Status == Release1WrongAddressDecisionStatus.InsufficientEmptyDeadDrops)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else
        {
            _feedback = result.Message;
            _feedbackStage = result.Status == Release1WrongAddressDecisionStatus.PersistenceDeferred
                ? Release1WrongAddressCardStage.PersistenceDeferred
                : Release1WrongAddressCardStage.DecisionRejected;
            _log?.Invoke($"Wrong Address decision failed: {result.Status}, {result.Message}");
        }
        return result;
    }
}
