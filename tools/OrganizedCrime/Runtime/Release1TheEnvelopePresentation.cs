using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1TheEnvelopeCardStage
{
    Hidden, Offer, Review, PersistenceDeferred, DecisionRejected,
    AwaitingActivation, Active, Shortfall, Spread, Ambiguous, Completed
}

public sealed record Release1TheEnvelopeViewModel(
    bool Visible,
    Release1TheEnvelopeCardStage Stage,
    string Title,
    string Body,
    bool CanReview,
    bool CanAccept,
    bool CanDefer);

public static class Release1TheEnvelopePresentation
{
    public const string Title = "The Envelope";
    public const string OfferText = "One last thing before the family meets to discuss your future. Payment. You think we just allow anyone off the street in our family for free?";
    public const string QuestionText = "Are you taking it?";
    /// <summary>Whole cash stacks in the amount, at 1000 dollars per stack (20, 10, 5 at the three tiers).</summary>
    public static int StackCount(double amountWholeDollars) =>
        (int)Math.Round(amountWholeDollars / 1000d, MidpointRounding.AwayFromZero);

    public static string ClosetTermsText(double amountWholeDollars) =>
        $"Closet: one closet in the storage room at HQ. All {StackCount(amountWholeDollars)} stacks in it.";
    public const string SourceText = "Your own fucking cash. You have to prove to us you are worth the effort.";
    public const string PaymentText = "We don't pay you. You pay us. You want in? This is the cost.";
    public const string ClosetStatusText = "Closet: any one storage at HQ";
    public const string ActiveStatusText = "Leave the whole amount in the closet before the window closes.";
    public const string ShortfallStatusFormat = "Part of the envelope is there. ${0} more still needed.";
    public const string SpreadStatusText = "The amount is spread across more than one closet. Put it together.";
    public const string CompletedStatusText = "Complete. The envelope is delivered.";
    public const string AmbiguousStatusText = "Stopped on conflicting evidence. It will not retry.";

    /// <summary>Renders a whole-dollar amount with no decimal, regardless of culture.</summary>
    public static string Dollars(double amount) => amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    public static Release1TheEnvelopeViewModel Build(
        Release1StoryState? state,
        Release1TheEnvelopeOfferStatus offerStatus,
        Release1TheEnvelopeQuote? reviewedQuote,
        Release1TheEnvelopeProgress? progress,
        string? feedback = null,
        Release1TheEnvelopeCardStage? feedbackStage = null)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
            return Visible(feedbackStage ?? Release1TheEnvelopeCardStage.PersistenceDeferred, Copy(feedback));

        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return Hidden();

        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];
        var expectedMode = Release1TheEnvelopeMissionService.ExpectedAssignmentMode(mission.State);
        var assignment = state.TheEnvelopeAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == mission.Attempt && (expectedMode is null || candidate.Mode == expectedMode.Value));

        if (assignment is null)
        {
            if (reviewedQuote is not null) return Terms(reviewedQuote.Assignment, reviewedQuote.DeadlineDurationHours);
            if (offerStatus == Release1TheEnvelopeOfferStatus.Available)
                return new(true, Release1TheEnvelopeCardStage.Offer, Title, Copy(OfferText), true, false, false);
            return Hidden();
        }

        if (state.NativeEffects.Any(effect =>
                effect.MissionKey == Release1MissionCatalog.TheEnvelope &&
                effect.Attempt == mission.Attempt &&
                effect.ExecutionBlocked))
            return Active(assignment, mission, Release1TheEnvelopeCardStage.Ambiguous, AmbiguousStatusText);

        if (mission.State == Release1MissionState.Satisfied)
            return Active(assignment, mission, Release1TheEnvelopeCardStage.Completed, CompletedStatusText);

        if (mission.State == Release1MissionState.Accepted)
            return Active(assignment, mission, Release1TheEnvelopeCardStage.AwaitingActivation, ActiveStatusText);

        if (progress?.SpreadNoticed == true)
            return Active(assignment, mission, Release1TheEnvelopeCardStage.Spread, SpreadStatusText);

        if (progress?.LastShortfallNoticed is { } remaining)
            return Active(assignment, mission, Release1TheEnvelopeCardStage.Shortfall,
                string.Format(ShortfallStatusFormat, Dollars(remaining)));

        return Active(assignment, mission, Release1TheEnvelopeCardStage.Active, ActiveStatusText);
    }

    private static Release1TheEnvelopeViewModel Terms(Release1TheEnvelopeAssignment assignment, double? deadlineHours) =>
        new(true, Release1TheEnvelopeCardStage.Review, Title, Copy(Release1PlayerCopy.JoinLines(
            Release1PlayerCopy.AttemptHeader(assignment.Mode, assignment.Attempt),
            $"${Dollars(assignment.AmountWholeDollars)} in cash, {StackCount(assignment.AmountWholeDollars)} stacks.",
            ClosetTermsText(assignment.AmountWholeDollars),
            $"Deadline: {Release1PlayerCopy.Deadline(deadlineHours)}",
            PaymentText,
            SourceText,
            QuestionText)), false, true, true);

    private static Release1TheEnvelopeViewModel Active(
        Release1TheEnvelopeAssignment assignment,
        Release1MissionRecord mission,
        Release1TheEnvelopeCardStage stage,
        string status) =>
        new(true, stage, Title, Copy(string.Join("\n",
            $"Status: {status}",
            $"Amount: ${Dollars(assignment.AmountWholeDollars)} in cash",
            ClosetStatusText,
            $"Deadline: {Release1PlayerCopy.Deadline(DeadlineDuration(mission))}",
            PaymentText)), false, false, false);

    private static double? DeadlineDuration(Release1MissionRecord mission) =>
        mission.DeadlineGameTimeHours is null || mission.AcceptedGameTimeHours is null
            ? null
            : mission.DeadlineGameTimeHours.Value - mission.AcceptedGameTimeHours.Value;

    private static Release1TheEnvelopeViewModel Visible(Release1TheEnvelopeCardStage stage, string body) =>
        new(true, stage, Title, body, false, false, false);

    private static Release1TheEnvelopeViewModel Hidden() =>
        new(false, Release1TheEnvelopeCardStage.Hidden, Title, string.Empty, false, false, false);

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}

public sealed class Release1TheEnvelopePresenter : IDisposable
{
    private readonly Release1TheEnvelopeMissionService _service;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private string? _feedback;
    private Release1TheEnvelopeCardStage? _feedbackStage;
    private bool _disposed;

    public Release1TheEnvelopePresenter(
        Release1TheEnvelopeMissionService service,
        Release1StoryRuntimeService story,
        Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _log = log;
    }

    public Release1TheEnvelopeViewModel View => _disposed
        ? Release1TheEnvelopePresentation.Build(null, Release1TheEnvelopeOfferStatus.Disposed, null, null)
        : Release1TheEnvelopePresentation.Build(
            _story.State,
            _service.OfferStatus,
            _service.ReviewedQuote,
            CurrentProgress(),
            _feedback,
            _feedbackStage);

    public Release1TheEnvelopeReviewResult TryReview()
    {
        var result = _service.TryReview();
        _feedback = result.Status == Release1TheEnvelopeReviewStatus.Ready ? null : result.Message;
        _feedbackStage = _feedback is null ? null : Release1TheEnvelopeCardStage.DecisionRejected;
        if (_feedback is not null) _log?.Invoke($"The Envelope review failed: {result.Status}, {result.Message}");
        return result;
    }

    public Release1TheEnvelopeDecisionResult TryAccept() => Handle(_service.TryAccept());
    public Release1TheEnvelopeDecisionResult TryDefer() => Handle(_service.TryDefer());

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

    private Release1TheEnvelopeProgress? CurrentProgress()
    {
        var state = _story.State;
        if (state is null) return null;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];
        return state.TheEnvelopeProgress.SingleOrDefault(progress => progress.Attempt == mission.Attempt);
    }

    private Release1TheEnvelopeDecisionResult Handle(Release1TheEnvelopeDecisionResult result)
    {
        if (result.Status is Release1TheEnvelopeDecisionStatus.Accepted or
            Release1TheEnvelopeDecisionStatus.Deferred or
            Release1TheEnvelopeDecisionStatus.Dismissed)
        {
            _feedback = null;
            _feedbackStage = null;
        }
        else
        {
            _feedback = result.Message;
            _feedbackStage = result.Status == Release1TheEnvelopeDecisionStatus.PersistenceDeferred
                ? Release1TheEnvelopeCardStage.PersistenceDeferred
                : Release1TheEnvelopeCardStage.DecisionRejected;
            _log?.Invoke($"The Envelope decision failed: {result.Status}, {result.Message}");
        }
        return result;
    }
}
