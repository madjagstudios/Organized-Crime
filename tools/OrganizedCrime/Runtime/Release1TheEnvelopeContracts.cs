using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1TheEnvelopeOfferStatus { Inactive, Available, Ineligible, Pending, Saving, Unavailable, Dismissed, Disposed }
public enum Release1TheEnvelopeReviewStatus { Ready, Inactive, Ineligible, Pending, Saving, Unavailable, Dismissed, Disposed }
public enum Release1TheEnvelopeDecisionStatus { Accepted, Deferred, Dismissed, ReviewRequired, QuoteChanged, PersistenceDeferred, Rejected, Inactive, Disposed }
public enum Release1TheEnvelopeDepositStatus { NoWork, Unavailable, Held, Ambiguous, Rejected, AwaitingAppliedSave, Committed }

public sealed record Release1TheEnvelopeQuote(
    Release1TheEnvelopeAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1TheEnvelopeAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 24d)
            throw new ArgumentException("Timed Envelope stages require a 24-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1TheEnvelopeReviewResult(
    Release1TheEnvelopeReviewStatus Status,
    Release1TheEnvelopeQuote? Quote,
    string Message);

public sealed record Release1TheEnvelopeDecisionResult(
    Release1TheEnvelopeDecisionStatus Status,
    string Message);
