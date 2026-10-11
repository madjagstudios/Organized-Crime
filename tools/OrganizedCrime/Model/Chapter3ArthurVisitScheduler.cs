namespace OrganizedCrime.Model;

/// <summary>
/// OC-134: what the visit scheduler wants done on a pass. The runtime
/// owns the visit itself (the pump, the lines, the phone); this only
/// names the decision so it stays pure and unit-testable.
/// </summary>
public enum Chapter3ArthurVisitAction
{
    None,
    StartVisit,
    PhoneFallback
}

public sealed record Chapter3ArthurVisitDecision(
    Chapter3ArthurVisitKind PendingKind,
    int MissedEvenings,
    int LastAttemptedDay,
    Chapter3ArthurVisitAction Action,
    Chapter3ArthurVisitKind ActionKind);

/// <summary>
/// OC-134: pure scheduling for Arthur's Chapter 3 visits, OC-135 adds
/// the beating on top. The evening
/// window is 18:00-23:00 in-game; each chapter day in the window is one
/// attempt. A series starts when something becomes due and resets when
/// the pending kind changes or clears, so a new warning never inherits
/// an old series' missed evenings. After <see cref="MissedPhoneThreshold"/>
/// missed evenings the next in-window pass phones instead of visiting.
/// A beating series never phones: after <see cref="BeatingWidenedThreshold"/>
/// missed evenings its window widens to any time of day instead.
/// </summary>
public static class Chapter3ArthurVisitScheduler
{
    /// <summary>Inclusive, in minutes past midnight: 18:00.</summary>
    public const int WindowStartMinutes = 18 * 60;

    /// <summary>
    /// Exclusive, in minutes past midnight: the 23:00 minute is still
    /// in the window, 23:01 is out.
    /// </summary>
    public const int WindowEndMinutesExclusive = 23 * 60 + 1;

    /// <summary>
    /// Evenings missed before the phone fallback replaces the visit.
    /// </summary>
    public const int MissedPhoneThreshold = 2;
    /// <summary>
    /// Evenings missed before a beating series drops the evening
    /// window and may start at any time of day. The conditions still
    /// apply, and there is never a phone fallback.
    /// </summary>
    public const int BeatingWidenedThreshold = 2;

    public static bool IsInWindow(int minuteOfDay) =>
        minuteOfDay >= WindowStartMinutes && minuteOfDay < WindowEndMinutesExclusive;

    /// <summary>
    /// Whether a beating series has missed enough evenings to drop
    /// the evening window. Only beating series widen; the missed
    /// count alone carries the widened state, so no extra field is saved.
    /// </summary>
    public static bool IsWidened(Chapter3ArthurVisitKind pending, int missedEvenings) =>
        pending == Chapter3ArthurVisitKind.Beating && missedEvenings >= BeatingWidenedThreshold;

    /// <summary>
    /// Which visit the board and ledger want: a beating whenever any
    /// make-good is BeatingDue, else a warning whenever any is
    /// WarningDue, else the cooperation visit while its streak issue
    /// is undelivered. The beating always wins, then the warning.
    /// </summary>
    public static Chapter3ArthurVisitKind DesiredPending(Chapter3MakeGoodLedger ledger, bool cooperationDue)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));
        if (ledger.Entries.Any(entry => entry.Stage == Chapter3MakeGoodStage.BeatingDue))
            return Chapter3ArthurVisitKind.Beating;
        if (ledger.Entries.Any(entry => entry.Stage == Chapter3MakeGoodStage.WarningDue))
            return Chapter3ArthurVisitKind.Warning;
        return cooperationDue ? Chapter3ArthurVisitKind.Cooperation : Chapter3ArthurVisitKind.None;
    }

    /// <summary>
    /// One scheduling pass. Carries the saved series (pending kind,
    /// missed evenings, last attempted day) forward to the current
    /// chapter day and minute, starting a visit or the phone fallback
    /// at most once per day. Evenings already past when a series
    /// starts never count as missed; every evening past since the
    /// last attempt counts, so sleeping through days misses each one.
    /// </summary>
    public static Chapter3ArthurVisitDecision Decide(
        Chapter3ArthurVisitKind savedPending,
        int missedEvenings,
        int lastAttemptedDay,
        Chapter3ArthurVisitKind desiredPending,
        int chapterDay,
        int minuteOfDay,
        bool conditionsMet)
    {
        if (!Enum.IsDefined(savedPending))
            throw new ArgumentOutOfRangeException(nameof(savedPending));
        if (!Enum.IsDefined(desiredPending))
            throw new ArgumentOutOfRangeException(nameof(desiredPending));
        if (missedEvenings < 0)
            throw new ArgumentOutOfRangeException(nameof(missedEvenings));
        if (lastAttemptedDay < -1)
            throw new ArgumentOutOfRangeException(nameof(lastAttemptedDay));
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay));
        if (minuteOfDay < 0 || minuteOfDay >= 24 * 60)
            throw new ArgumentOutOfRangeException(nameof(minuteOfDay));

        if (desiredPending == Chapter3ArthurVisitKind.None)
            return new Chapter3ArthurVisitDecision(
                Chapter3ArthurVisitKind.None, 0, -1, Chapter3ArthurVisitAction.None, Chapter3ArthurVisitKind.None);

        // A new or changed series starts anchored at the latest past
        // evening, so evenings before anything was due never count.
        var pending = desiredPending;
        var missed = missedEvenings;
        var last = lastAttemptedDay;
        if (pending != savedPending)
        {
            missed = 0;
            last = LatestPastEvening(chapterDay, minuteOfDay);
        }

        var windowOpen = IsInWindow(minuteOfDay) || IsWidened(pending, missed);
        if (windowOpen)
        {
            if (chapterDay <= last)
                return new Chapter3ArthurVisitDecision(pending, missed, last, Chapter3ArthurVisitAction.None, Chapter3ArthurVisitKind.None);
            if (missed >= MissedPhoneThreshold && pending != Chapter3ArthurVisitKind.Beating)
                return new Chapter3ArthurVisitDecision(pending, missed, chapterDay, Chapter3ArthurVisitAction.PhoneFallback, pending);
            if (conditionsMet)
                return new Chapter3ArthurVisitDecision(pending, missed, chapterDay, Chapter3ArthurVisitAction.StartVisit, pending);
            return new Chapter3ArthurVisitDecision(pending, missed, last, Chapter3ArthurVisitAction.None, Chapter3ArthurVisitKind.None);
        }

        var latestPast = LatestPastEvening(chapterDay, minuteOfDay);
        if (latestPast > last)
        {
            missed += latestPast - last;
            last = latestPast;
        }
        return new Chapter3ArthurVisitDecision(pending, missed, last, Chapter3ArthurVisitAction.None, Chapter3ArthurVisitKind.None);
    }

    private static int LatestPastEvening(int chapterDay, int minuteOfDay) =>
        minuteOfDay >= WindowEndMinutesExclusive ? chapterDay : chapterDay - 1;
}
