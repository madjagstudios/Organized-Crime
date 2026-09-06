namespace OrganizedCrime.Runtime;

/// <summary>
/// What one pass reads the condition as. Deliberately three valued and deliberately narrower than any
/// mission's own classifier enum: a condition that cannot be read this pass is Unreadable and always
/// holds, so an unready world can never fail the player. Each mission's classifier keeps its own richer
/// enum for its status line and maps into this one at the call site.
/// </summary>
public enum Release1WindowObservation
{
    Unreadable,
    Clean,
    Breached
}

/// <summary>
/// The one policy dial. It exists only to preserve the single genuine divergence between the two
/// shipped conditions: on a clean pass that carries a stale breach mark and an already elapsed window,
/// HealBeforeElapse heals this pass and completes on the next one, while ElapseBeforeHeal completes now
/// and heals in the same write. Collapsing the two would change one shipped mission's live proven
/// behavior and is its own ticket.
/// </summary>
public enum Release1WindowHealPolicy
{
    HealBeforeElapse,
    ElapseBeforeHeal
}

/// <summary>
/// What one pass should do. Pure; the calling mission service performs the effect, and what Elapsed
/// means is the mission's own switch arm rather than a hook on the engine.
/// </summary>
public enum Release1WindowDecision
{
    Hold,
    OpenWindow,
    ClearBreach,
    RecordBreach,
    Elapsed,
    Fail,
    None
}

/// <summary>
/// The read only projection of a mission's own progress record. The engine owns no progress fields and
/// there is no shared progress record: a shared record would be a schema change, and schema v9 is the
/// version the whole live proven arc runs on. Each mission maps its own record into this shape at the
/// call site and keeps its own condition specific side data where its own validation already lives.
/// </summary>
public sealed record Release1WindowProgress(
    double? OpenedAtGameMinutes,
    double? BreachSinceGameMinutes,
    bool Satisfied);

/// <summary>
/// The condition's tuning. The window duration is read from here and nowhere else, so this file
/// contains no window constant of its own and never re-validates one: each caller reads the validated
/// duration off its own assignment and constructs the profile at the call site.
/// </summary>
public sealed record Release1WindowProfile(
    double WindowGameMinutes,
    double GraceGameMinutes,
    Release1WindowHealPolicy HealPolicy);

/// <summary>
/// The pure decision table shared by every windowed condition: a condition must hold clean for a run of
/// game minutes, a single dirty pass is forgiven for a grace period because the world can read dirty for
/// one frame while the player is mid action, a second dirty pass past the grace fails, and the elapsed
/// window fires a completion.
///
/// Safe at any pass cadence by construction: every threshold is a comparison of the current game minute
/// against a stored game minute, so two passes inside the same game minute can only reach the same
/// decision and a pass skipped inside a minute is never a decision missed. That is the property the
/// host mission service's once per game minute throttle rests on.
///
/// This file names nothing about what any condition observes, and no mission by name. Adding a
/// condition needs a classifier, an adapter read member, and a copy record, and never an edit here; a
/// change that adds a condition and also edits this file is a signal the engine is wrong.
/// </summary>
public static class Release1ConditionWindow
{
    public static Release1WindowDecision Decide(
        Release1WindowObservation observation,
        Release1WindowProgress progress,
        double nowGameMinutes,
        Release1WindowProfile profile)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(profile);

        // Rule 1. A bad clock, a bad window, or a bad grace all refuse to decide rather than letting the
        // raw arithmetic produce a surprising result: a negative grace would let a fail fire on the very
        // next pass after a mark, and a NaN grace would make the grace comparison always false and
        // silently degrade to never failing. Both shipped conditions hardcode a positive grace, so this
        // is never live today; it exists so a future condition with a bad profile fails safe.
        if (!double.IsFinite(nowGameMinutes) || nowGameMinutes < 0d ||
            !double.IsFinite(profile.WindowGameMinutes) || profile.WindowGameMinutes <= 0d ||
            !double.IsFinite(profile.GraceGameMinutes) || profile.GraceGameMinutes <= 0d)
            return Release1WindowDecision.Hold;

        // Rule 2.
        if (observation == Release1WindowObservation.Unreadable) return Release1WindowDecision.Hold;

        if (observation == Release1WindowObservation.Clean)
        {
            // Rule 3.
            if (progress.OpenedAtGameMinutes is not { } openedAt) return Release1WindowDecision.OpenWindow;

            // Rule 4.
            if (profile.HealPolicy == Release1WindowHealPolicy.HealBeforeElapse &&
                progress.BreachSinceGameMinutes is not null)
                return Release1WindowDecision.ClearBreach;

            // Rule 5. Inclusive at the threshold and exclusive below it.
            if (!progress.Satisfied && nowGameMinutes >= openedAt + profile.WindowGameMinutes)
                return Release1WindowDecision.Elapsed;

            // Rule 6.
            if (progress.BreachSinceGameMinutes is not null) return Release1WindowDecision.ClearBreach;

            // Rule 7.
            return Release1WindowDecision.None;
        }

        // Breached.
        // Rule 8.
        if (progress.OpenedAtGameMinutes is null) return Release1WindowDecision.None;

        // Rule 9.
        if (progress.Satisfied) return Release1WindowDecision.None;

        // Rule 10.
        if (progress.BreachSinceGameMinutes is not { } breachSince) return Release1WindowDecision.RecordBreach;

        // Rules 11 and 12. Inclusive at the threshold and exclusive below it.
        return nowGameMinutes >= breachSince + profile.GraceGameMinutes
            ? Release1WindowDecision.Fail
            : Release1WindowDecision.Hold;
    }
}
