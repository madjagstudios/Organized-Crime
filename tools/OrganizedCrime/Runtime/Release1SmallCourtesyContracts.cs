using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Release1.World;

namespace OrganizedCrime.Runtime;

public enum Release1SmallCourtesyWorldReadStatus
{
    Ready,
    Pending,
    NotAuthoritative,
    Unavailable,
    Faulted
}

public enum Release1SmallCourtesyPackageKind
{
    Brick,
    Jar
}

public interface IRelease1SmallCourtesyWorld : IRelease1ProductionWorld, IChiefWorld, IChapter3World, IDeadDropCashWorld, IFieldContactWorld
{
}

public enum Release1SmallCourtesyWorldMutationStatus
{
    Succeeded,
    Rejected,
    Unavailable,
    Ambiguous
}

public interface IRelease1SmallCourtesyDropSubscription : IDisposable
{
    string DeadDropGuid { get; }
}

public enum Release1SmallCourtesyDepositStatus
{
    NoWork,
    AwaitingPreparedSave,
    Applied,
    AwaitingAppliedSave,
    Committed,
    Ambiguous,
    Rejected
}

public enum Release1SmallCourtesyRewardStatus
{
    NoWork,
    AwaitingPreparedSave,
    Applied,
    AwaitingAppliedSave,
    Committed,
    Ambiguous,
    Rejected
}

public enum Release1SmallCourtesyOfferStatus
{
    Inactive,
    Available,
    Ineligible,
    Pending,
    Saving,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    Unavailable,
    Dismissed,
    Disposed
}

public enum Release1SmallCourtesyReviewStatus
{
    Ready,
    Inactive,
    Ineligible,
    Pending,
    Saving,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    Unavailable,
    Dismissed,
    Disposed
}

public enum Release1SmallCourtesyDecisionStatus
{
    Accepted,
    Deferred,
    Dismissed,
    ReviewRequired,
    QuoteChanged,
    PersistenceDeferred,
    Rejected,
    Inactive,
    Disposed
}

public sealed record Release1SmallCourtesyQuote(
    Release1SmallCourtesyAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1SmallCourtesyAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 24d)
            throw new ArgumentException("Timed Small Courtesy stages require a 24-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1SmallCourtesyReviewResult(
    Release1SmallCourtesyReviewStatus Status,
    Release1SmallCourtesyQuote? Quote,
    string Message);

public sealed record Release1SmallCourtesyDecisionResult(
    Release1SmallCourtesyDecisionStatus Status,
    string Message);
