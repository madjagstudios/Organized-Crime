using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1KeepTheLightsOffOfferStatus
{
    Inactive, Available, Ineligible, Pending, Saving,
    Unavailable, Dismissed, Disposed
}

public enum Release1KeepTheLightsOffReviewStatus
{
    Ready, Inactive, Ineligible, Pending, Saving,
    Unavailable, Dismissed, Disposed
}

public enum Release1KeepTheLightsOffDecisionStatus
{
    Accepted, Deferred, Dismissed, ReviewRequired, QuoteChanged,
    PersistenceDeferred, Rejected, Inactive, Disposed
}

// The presentation layer's Ambiguous card stage (Release1KeepTheLightsOffPresentation.Build) is
// driven directly off Release1StoryState.NativeEffects filtered to this mission's own key
// (Release1KeepTheLightsOffPresentation.cs:74-77), never off a value ReconcileCensus returns, so
// this enum carries no Ambiguous member: ReconcileCensus has nothing to report for a card state it
// never decides.
public enum Release1KeepTheLightsOffCensusStatus
{
    NoWork, NotReady, Holding, WindowStarted, BreachRecorded, Failed,
    Completed, Rejected
}

public sealed record Release1KeepTheLightsOffQuote(
    Release1KeepTheLightsOffAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1KeepTheLightsOffAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 72d)
            throw new ArgumentException("Timed Keep the Lights Off stages require a 72-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1KeepTheLightsOffReviewResult(
    Release1KeepTheLightsOffReviewStatus Status,
    Release1KeepTheLightsOffQuote? Quote,
    string Message);

public sealed record Release1KeepTheLightsOffDecisionResult(
    Release1KeepTheLightsOffDecisionStatus Status,
    string Message);
