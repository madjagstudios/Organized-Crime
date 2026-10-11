using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface ILockdownWorld
{
    /// <summary>
    /// OC-73 spec decision 17. Holds the town's own curfew active at every hour through the seam
    /// already proven under this key, <see cref="Release1LockdownGatePatch"/>. Idempotent: an already
    /// engaged lockdown reports Succeeded and changes nothing. Reports Unavailable, never a failure,
    /// when the gate reports NeedsOptionB (curfew is not enabled on this save and this session is not
    /// the authoritative host, or no local player connection is available) so the caller can hold and
    /// retry. <paramref name="reason"/> carries the gate's own diagnostic lines for the one line per
    /// load the caller logs. No new owner QA key: the shipped End key becomes a second caller of this
    /// same member.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason);

    /// <summary>
    /// The mirror of <see cref="TryEngageLockdown"/>. Clears the lockdown flag so the very next native
    /// per minute recompute restores the vanilla schedule, and disables curfew on the save through the
    /// game's own Disable path only when this gate itself enabled it. Idempotent on an already
    /// released lockdown.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason);
}
