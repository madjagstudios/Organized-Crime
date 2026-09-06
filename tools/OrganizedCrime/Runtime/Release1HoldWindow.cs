using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>What one hold check pass should do. Pure; the mission service performs the effect.</summary>
public enum Release1HoldDecision
{
    Hold,
    RecordStow,
    ClearMissing,
    SatisfyHold,
    RecordMissing,
    FailHold,
    None
}

/// <summary>
/// A Room With No Name's view of the shared <see cref="Release1ConditionWindow"/>: a thin mapping from
/// the room vocabulary and the mission's own progress record into the engine's projection, and back from
/// the engine decision into this mission's decision enum. The grace exists because the player
/// legitimately moves the consignment between lockers: a single pass that finds nothing is never a
/// failure, only a second consecutive pass at least MissingGraceGameMinutes later is.
///
/// This mission is ElapseBeforeHeal: an elapsed hold is satisfied on the same pass that heals a stale
/// missing mark, which is exactly what it shipped and what passed live on 2026-09-03. It also carries a
/// real satisfaction latch, so once HoldSatisfied is set the room can never elapse or fail again, and
/// the mission service's delivery reconciler takes over. Ambiguous, a second matching consignment
/// anywhere in the room, folds into Unreadable and always holds, exactly as it shipped; the mission
/// service keeps the richer observation for its own status line.
/// </summary>
public static class Release1HoldWindow
{
    public const double MissingGraceGameMinutes = 60d;

    public static Release1HoldDecision Decide(
        Release1HoldObservation observation,
        Release1RoomWithNoNameProgress progress,
        double nowGameMinutes,
        double holdDurationGameMinutes)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var decision = Release1ConditionWindow.Decide(
            Observe(observation),
            // Stow details never exist before a stow (the progress record's own Validate refuses), so
            // gating the open time on Stowed is exact rather than defensive.
            new Release1WindowProgress(
                progress.Stowed ? progress.StowedAtGameMinutes : null,
                progress.MissingSincePassGameMinutes,
                progress.HoldSatisfied),
            nowGameMinutes,
            new Release1WindowProfile(
                holdDurationGameMinutes,
                MissingGraceGameMinutes,
                Release1WindowHealPolicy.ElapseBeforeHeal));

        return decision switch
        {
            Release1WindowDecision.OpenWindow => Release1HoldDecision.RecordStow,
            Release1WindowDecision.ClearBreach => Release1HoldDecision.ClearMissing,
            Release1WindowDecision.RecordBreach => Release1HoldDecision.RecordMissing,
            Release1WindowDecision.Elapsed => Release1HoldDecision.SatisfyHold,
            Release1WindowDecision.Fail => Release1HoldDecision.FailHold,
            Release1WindowDecision.None => Release1HoldDecision.None,
            _ => Release1HoldDecision.Hold
        };
    }

    private static Release1WindowObservation Observe(Release1HoldObservation observation) => observation switch
    {
        Release1HoldObservation.Held => Release1WindowObservation.Clean,
        Release1HoldObservation.Missing => Release1WindowObservation.Breached,
        _ => Release1WindowObservation.Unreadable
    };
}
