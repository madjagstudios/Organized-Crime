namespace OrganizedCrime.Model;

/// <summary>
/// Tunable Chapter 3 make-good numbers, as named constants. Starting values, not final balance.
/// </summary>
public static class Chapter3MakeGoodTuning
{
    public const int DueDays = 2;
    public const int MissedCutPercent = 125;
    public const int WarningToBeatingDays = 2;
}

public sealed record Chapter3MakeGoodTransition(Chapter3MakeGoodLedger Ledger, IReadOnlyList<Chapter3MakeGoodEffect> Effects);

/// <summary>
/// Records what the player owes the family and moves each debt through Arthur's
/// escalation steps. Pure transitions: each takes a ledger and returns the new
/// ledger plus the effects it produced. Stale and repeated calls (an unknown id,
/// a delivered warning, a tick for a processed day) return the ledger unchanged
/// with no effects. Never charges money; later tickets do that.
/// </summary>
public static class Chapter3MakeGoods
{
    public static Chapter3MakeGoodTransition OpenForFailedJob(
        Chapter3MakeGoodLedger ledger, int jobId, Chapter3JobType jobType, int reward, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));
        if (jobId <= 0)
            throw new ArgumentOutOfRangeException(nameof(jobId), "Chapter 3 make-good job id was invalid.");
        if (!Enum.IsDefined(typeof(Chapter3JobType), jobType))
            throw new ArgumentException($"Chapter 3 make-good job type was unknown: {(int)jobType}.", nameof(jobType));
        if (reward < 0)
            throw new ArgumentOutOfRangeException(nameof(reward), "Chapter 3 make-good reward was negative.");
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay), "Chapter 3 make-good open day was negative.");
        return Open(ledger, new Chapter3FailedJobSource(jobId, jobType), reward, chapterDay);
    }

    public static Chapter3MakeGoodTransition OpenForMissedCut(
        Chapter3MakeGoodLedger ledger, int weekIndex, int cutAmount, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));
        if (weekIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(weekIndex), "Chapter 3 make-good week index was negative.");
        if (cutAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(cutAmount), "Chapter 3 make-good cut amount was negative.");
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay), "Chapter 3 make-good open day was negative.");
        // Integer math rounds down, keeping whole dollars.
        var amount = cutAmount * Chapter3MakeGoodTuning.MissedCutPercent / 100;
        return Open(ledger, new Chapter3MissedCutSource(weekIndex), amount, chapterDay);
    }

    public static Chapter3MakeGoodTransition OpenForMissedDemand(
        Chapter3MakeGoodLedger ledger, string demandId, int shortfallUnits, int unitValue, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));
        if (string.IsNullOrWhiteSpace(demandId))
            throw new ArgumentException("Chapter 3 make-good demand id was empty.", nameof(demandId));
        if (shortfallUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(shortfallUnits), "Chapter 3 make-good shortfall was negative.");
        if (unitValue < 0)
            throw new ArgumentOutOfRangeException(nameof(unitValue), "Chapter 3 make-good unit value was negative.");
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay), "Chapter 3 make-good open day was negative.");
        return Open(ledger, new Chapter3MissedDemandSource(demandId), shortfallUnits * unitValue, chapterDay);
    }

    public static Chapter3MakeGoodTransition OnDayTick(Chapter3MakeGoodLedger ledger, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));

        if (chapterDay <= ledger.LastProcessedDay)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());

        var entries = ledger.Entries.ToList();
        var effects = new List<Chapter3MakeGoodEffect>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Status == Chapter3MakeGoodStatus.Paid)
                continue;
            if (entry.Status == Chapter3MakeGoodStatus.Open && chapterDay > entry.DueDay)
            {
                entry = entry with
                {
                    Status = Chapter3MakeGoodStatus.Overdue,
                    Stage = Chapter3MakeGoodStage.WarningDue
                };
                entries[i] = entry;
                effects.Add(new Chapter3MakeGoodOverdueEffect(entry));
                effects.Add(new Chapter3WarningDueEffect(entry));
            }
            else if (
                entry.Status == Chapter3MakeGoodStatus.Overdue &&
                entry.Stage == Chapter3MakeGoodStage.None &&
                entry.WarningDeliveredDay.HasValue &&
                !entry.BeatingDeliveredDay.HasValue &&
                chapterDay >= entry.WarningDeliveredDay.Value + Chapter3MakeGoodTuning.WarningToBeatingDays)
            {
                entry = entry with { Stage = Chapter3MakeGoodStage.BeatingDue };
                entries[i] = entry;
                effects.Add(new Chapter3BeatingDueEffect(entry));
            }
        }

        var next = ledger with { Entries = entries, LastProcessedDay = chapterDay };
        return new Chapter3MakeGoodTransition(next, effects);
    }

    public static Chapter3MakeGoodTransition RecordWarningDelivered(Chapter3MakeGoodLedger ledger, int id, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));

        var index = FindIndex(ledger, id);
        if (index < 0)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());
        var entry = ledger.Entries[index];
        if (entry.Status == Chapter3MakeGoodStatus.Paid || entry.Stage != Chapter3MakeGoodStage.WarningDue)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());

        var entries = ledger.Entries.ToList();
        entries[index] = entry with { Stage = Chapter3MakeGoodStage.None, WarningDeliveredDay = chapterDay };
        return new Chapter3MakeGoodTransition(ledger with { Entries = entries }, Array.Empty<Chapter3MakeGoodEffect>());
    }

    public static Chapter3MakeGoodTransition RecordBeatingDelivered(Chapter3MakeGoodLedger ledger, int id, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));

        var index = FindIndex(ledger, id);
        if (index < 0)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());
        var entry = ledger.Entries[index];
        if (entry.Status == Chapter3MakeGoodStatus.Paid || entry.Stage != Chapter3MakeGoodStage.BeatingDue)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());

        var entries = ledger.Entries.ToList();
        entries[index] = entry with { Stage = Chapter3MakeGoodStage.None, BeatingDeliveredDay = chapterDay };
        return new Chapter3MakeGoodTransition(ledger with { Entries = entries }, Array.Empty<Chapter3MakeGoodEffect>());
    }

    /// <summary>
    /// OC-135: an attack-triggered beating counts as the beating for
    /// every unpaid overdue make-good at once: the warning is marked
    /// delivered where missing, the beating is marked delivered
    /// today, and the stage clears. Open (not yet overdue) and paid
    /// entries are untouched, and entries already beaten keep their
    /// original day. Like the other record transitions this returns
    /// no effects; the runtime logs the beating itself.
    /// </summary>
    public static Chapter3MakeGoodTransition RecordBeatingByAttack(Chapter3MakeGoodLedger ledger, int chapterDay)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));

        var entries = ledger.Entries.ToList();
        var changed = false;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Status != Chapter3MakeGoodStatus.Overdue)
                continue;
            if (entry.BeatingDeliveredDay.HasValue)
                continue;
            entries[i] = entry with
            {
                Stage = Chapter3MakeGoodStage.None,
                WarningDeliveredDay = entry.WarningDeliveredDay ?? chapterDay,
                BeatingDeliveredDay = chapterDay
            };
            changed = true;
        }
        if (!changed)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());
        return new Chapter3MakeGoodTransition(ledger with { Entries = entries }, Array.Empty<Chapter3MakeGoodEffect>());
    }

    public static Chapter3MakeGoodTransition Pay(Chapter3MakeGoodLedger ledger, int id)
    {
        if (ledger is null)
            throw new ArgumentNullException(nameof(ledger));

        var index = FindIndex(ledger, id);
        if (index < 0)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());
        var entry = ledger.Entries[index];
        if (entry.Status == Chapter3MakeGoodStatus.Paid)
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());

        // Payment cancels anything pending: a due warning or a due beating.
        var paid = entry with { Status = Chapter3MakeGoodStatus.Paid, Stage = Chapter3MakeGoodStage.None };
        var entries = ledger.Entries.ToList();
        entries[index] = paid;
        var next = ledger with { Entries = entries };
        return new Chapter3MakeGoodTransition(next, new[] { new Chapter3MakeGoodPaidEffect(paid) });
    }

    /// <summary>
    /// One-line source label shared by the runtime log lines and the QA status.
    /// </summary>
    public static string DescribeSource(Chapter3MakeGoodSource source) => source switch
    {
        Chapter3FailedJobSource failed => $"failed {failed.JobType} job",
        Chapter3MissedCutSource cut => $"missed week {cut.WeekIndex} cut",
        Chapter3MissedDemandSource demand => $"missed demand {demand.DemandId}",
        _ => "unknown source"
    };

    private static Chapter3MakeGoodTransition Open(
        Chapter3MakeGoodLedger ledger, Chapter3MakeGoodSource source, int amount, int chapterDay)
    {
        if (ledger.Entries.Any(entry => entry.Source == source))
            return new Chapter3MakeGoodTransition(ledger, Array.Empty<Chapter3MakeGoodEffect>());

        var entry = new Chapter3MakeGood(
            ledger.NextId, source, amount, chapterDay, chapterDay + Chapter3MakeGoodTuning.DueDays,
            Chapter3MakeGoodStatus.Open, Chapter3MakeGoodStage.None, null, null);
        var next = ledger with
        {
            Entries = ledger.Entries.Append(entry).ToArray(),
            NextId = ledger.NextId + 1
        };
        return new Chapter3MakeGoodTransition(next, new[] { new Chapter3MakeGoodOpenedEffect(entry) });
    }

    private static int FindIndex(Chapter3MakeGoodLedger ledger, int id)
    {
        for (var i = 0; i < ledger.Entries.Count; i++)
        {
            if (ledger.Entries[i].Id == id)
                return i;
        }
        return -1;
    }
}
