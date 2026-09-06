using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1WrongAddressOfferStatus
{
    Inactive, Available, Ineligible, Pending, Saving,
    NoDiscoveredProduct, InsufficientEmptyDeadDrops, Unavailable, Dismissed, Disposed
}

public enum Release1WrongAddressReviewStatus
{
    Ready, Inactive, Ineligible, Pending, Saving,
    NoDiscoveredProduct, InsufficientEmptyDeadDrops, Unavailable, Dismissed, Disposed
}

public enum Release1WrongAddressDecisionStatus
{
    Accepted, Deferred, Dismissed, ReviewRequired, QuoteChanged,
    InsufficientEmptyDeadDrops, PersistenceDeferred, Rejected, Inactive, Disposed
}

public enum Release1WrongAddressStageStatus
{
    NoWork, Staged, AlreadyStaged, CustodyInferred, Held, Unavailable, Rejected
}

public enum Release1WrongAddressDeliveryStatus
{
    NoWork, Paid, AwaitingAppliedSave, Committed, Ambiguous, Rejected
}

public sealed record Release1WrongAddressQuote(
    Release1WrongAddressAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1WrongAddressAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 24d)
            throw new ArgumentException("Timed Wrong Address stages require a 24-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1WrongAddressReviewResult(
    Release1WrongAddressReviewStatus Status,
    Release1WrongAddressQuote? Quote,
    string Message);

public sealed record Release1WrongAddressDecisionResult(
    Release1WrongAddressDecisionStatus Status,
    string Message);
