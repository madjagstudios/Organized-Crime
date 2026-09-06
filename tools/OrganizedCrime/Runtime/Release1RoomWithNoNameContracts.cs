using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1RoomWithNoNameOfferStatus
{
    Inactive, Available, Ineligible, Pending, Saving,
    NoDiscoveredProduct, InsufficientEmptyDeadDrops, Unavailable, Dismissed, Disposed
}

public enum Release1RoomWithNoNameReviewStatus
{
    Ready, Inactive, Ineligible, Pending, Saving,
    NoDiscoveredProduct, InsufficientEmptyDeadDrops, Unavailable, Dismissed, Disposed
}

public enum Release1RoomWithNoNameDecisionStatus
{
    Accepted, Deferred, Dismissed, ReviewRequired, QuoteChanged,
    InsufficientEmptyDeadDrops, PersistenceDeferred, Rejected, Inactive, Disposed
}

public enum Release1RoomWithNoNameStageStatus { NoWork, Staged, AlreadyStaged, CustodyInferred, Held, Unavailable, Rejected }

public enum Release1RoomWithNoNameHoldStatus { NoWork, RoomNotReady, Stowed, Holding, Released, Missing, Failed, Ambiguous, Rejected }

public enum Release1RoomWithNoNameDeliveryStatus { NoWork, Paid, AwaitingAppliedSave, Committed, Ambiguous, Rejected }

public sealed record Release1RoomWithNoNameQuote(
    Release1RoomWithNoNameAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1RoomWithNoNameAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 72d)
            throw new ArgumentException("Timed Room With No Name stages require a 72-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1RoomWithNoNameReviewResult(
    Release1RoomWithNoNameReviewStatus Status,
    Release1RoomWithNoNameQuote? Quote,
    string Message);

public sealed record Release1RoomWithNoNameDecisionResult(
    Release1RoomWithNoNameDecisionStatus Status,
    string Message);
