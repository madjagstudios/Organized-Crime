using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1ShortNoticeOfferStatus
{
    Inactive, Available, Ineligible, Pending, Saving,
    NoDiscoveredProduct, NoEmptyDeadDrop, Unavailable, Dismissed, Disposed
}

public enum Release1ShortNoticeReviewStatus
{
    Ready, Inactive, Ineligible, Pending, Saving,
    NoDiscoveredProduct, NoEmptyDeadDrop, Unavailable, Dismissed, Disposed
}

public enum Release1ShortNoticeDecisionStatus
{
    Accepted, Deferred, Dismissed, ReviewRequired, QuoteChanged,
    NoEmptyDeadDrop, PersistenceDeferred, Rejected, Inactive, Disposed
}

public enum Release1ShortNoticeObservationStatus { NoWork, Shortfall, Spread, Ready, Unavailable, Rejected }

public enum Release1ShortNoticeDepositStatus { NoWork, Paid, AwaitingAppliedSave, Committed, Ambiguous, Rejected }

public sealed record Release1ShortNoticeQuote(
    Release1ShortNoticeAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1ShortNoticeAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 12d)
            throw new ArgumentException("Timed Short Notice stages require a 12-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1ShortNoticeReviewResult(
    Release1ShortNoticeReviewStatus Status,
    Release1ShortNoticeQuote? Quote,
    string Message);

public sealed record Release1ShortNoticeDecisionResult(
    Release1ShortNoticeDecisionStatus Status,
    string Message);
