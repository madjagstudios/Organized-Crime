namespace OrganizedCrime.Model;

public enum Chapter3MakeGoodStatus
{
    Open,
    Overdue,
    Paid
}

public enum Chapter3MakeGoodStage
{
    None,
    WarningDue,
    BeatingDue
}

public enum Chapter3MakeGoodUrgency
{
    None,
    Open,
    WarningDue,
    BeatingDue
}

/// <summary>
/// What opened a make-good: a failed accepted job, a cut missed past its grace,
/// or a missed production demand. Value equality makes the openers idempotent
/// per source.
/// </summary>
public abstract record Chapter3MakeGoodSource;

public sealed record Chapter3FailedJobSource(int JobId, Chapter3JobType JobType) : Chapter3MakeGoodSource;

public sealed record Chapter3MissedCutSource(int WeekIndex) : Chapter3MakeGoodSource;

public sealed record Chapter3MissedDemandSource(string DemandId) : Chapter3MakeGoodSource;

/// <summary>
/// One debt to the family: its source, amount, deadline, payment status and
/// escalation stage, plus the days Arthur's warning and beating were delivered.
/// </summary>
public sealed record Chapter3MakeGood(
    int Id,
    Chapter3MakeGoodSource Source,
    int Amount,
    int OpenedDay,
    int DueDay,
    Chapter3MakeGoodStatus Status,
    Chapter3MakeGoodStage Stage,
    int? WarningDeliveredDay,
    int? BeatingDeliveredDay)
{
    public void Validate()
    {
        if (Id <= 0)
            throw new ArgumentException("Chapter 3 make-good id was invalid.", nameof(Id));
        if (Source is null)
            throw new ArgumentException("Chapter 3 make-good source was missing.", nameof(Source));
        ValidateSource(Source);
        if (Amount < 0)
            throw new ArgumentException("Chapter 3 make-good amount was negative.", nameof(Amount));
        if (OpenedDay < 0)
            throw new ArgumentException("Chapter 3 make-good open day was negative.", nameof(OpenedDay));
        if (DueDay < OpenedDay)
            throw new ArgumentException("Chapter 3 make-good due day was before its open day.", nameof(DueDay));
        if (!Enum.IsDefined(typeof(Chapter3MakeGoodStatus), Status))
            throw new ArgumentException($"Chapter 3 make-good status was unknown: {(int)Status}.", nameof(Status));
        if (!Enum.IsDefined(typeof(Chapter3MakeGoodStage), Stage))
            throw new ArgumentException($"Chapter 3 make-good stage was unknown: {(int)Stage}.", nameof(Stage));
        if (WarningDeliveredDay.HasValue && WarningDeliveredDay.Value < 0)
            throw new ArgumentException("Chapter 3 warning day was negative.", nameof(WarningDeliveredDay));
        if (BeatingDeliveredDay.HasValue && BeatingDeliveredDay.Value < 0)
            throw new ArgumentException("Chapter 3 beating day was negative.", nameof(BeatingDeliveredDay));
    }

    private static void ValidateSource(Chapter3MakeGoodSource source)
    {
        switch (source)
        {
            case Chapter3FailedJobSource failed when failed.JobId <= 0:
                throw new ArgumentException("Chapter 3 make-good job id was invalid.", nameof(Source));
            case Chapter3FailedJobSource failed when !Enum.IsDefined(typeof(Chapter3JobType), failed.JobType):
                throw new ArgumentException($"Chapter 3 make-good job type was unknown: {(int)failed.JobType}.", nameof(Source));
            case Chapter3MissedCutSource cut when cut.WeekIndex < 0:
                throw new ArgumentException("Chapter 3 make-good week index was negative.", nameof(Source));
            case Chapter3MissedDemandSource demand when string.IsNullOrWhiteSpace(demand.DemandId):
                throw new ArgumentException("Chapter 3 make-good demand id was empty.", nameof(Source));
            case Chapter3FailedJobSource:
            case Chapter3MissedCutSource:
            case Chapter3MissedDemandSource:
                break;
            default:
                throw new ArgumentException($"Chapter 3 make-good source was unknown: {source.GetType().Name}.", nameof(Source));
        }
    }
}

/// <summary>
/// Chapter 3's make-good ledger: every debt to the family, each escalating on
/// its own. Pure data; all transitions live in <see cref="Chapter3MakeGoods"/>.
/// </summary>
public sealed record Chapter3MakeGoodLedger(
    IReadOnlyList<Chapter3MakeGood> Entries,
    int LastProcessedDay,
    int NextId)
{
    public static Chapter3MakeGoodLedger Empty => new(Array.Empty<Chapter3MakeGood>(), -1, 1);

    /// <summary>
    /// Any unpaid make-good, whether open or overdue. Feeds the warehouse-offer
    /// eligibility query.
    /// </summary>
    public bool HasOpenMakeGood => Entries.Any(entry => entry.Status != Chapter3MakeGoodStatus.Paid);

    /// <summary>
    /// The worst stage across unpaid entries, for the overnight line and Arthur.
    /// </summary>
    public Chapter3MakeGoodUrgency MostUrgentStage
    {
        get
        {
            var urgency = Chapter3MakeGoodUrgency.None;
            foreach (var entry in Entries)
            {
                if (entry.Status == Chapter3MakeGoodStatus.Paid)
                    continue;
                var candidate = entry.Stage switch
                {
                    Chapter3MakeGoodStage.BeatingDue => Chapter3MakeGoodUrgency.BeatingDue,
                    Chapter3MakeGoodStage.WarningDue => Chapter3MakeGoodUrgency.WarningDue,
                    _ => Chapter3MakeGoodUrgency.Open
                };
                if (candidate > urgency)
                    urgency = candidate;
            }
            return urgency;
        }
    }

    public bool Equals(Chapter3MakeGoodLedger? other) =>
        other is not null &&
        Entries.SequenceEqual(other.Entries) &&
        LastProcessedDay == other.LastProcessedDay &&
        NextId == other.NextId;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
            hash.Add(entry);
        hash.Add(LastProcessedDay);
        hash.Add(NextId);
        return hash.ToHashCode();
    }

    public void Validate()
    {
        if (Entries is null)
            throw new ArgumentException("Chapter 3 make-good entries were missing.", nameof(Entries));
        var ids = new HashSet<int>();
        foreach (var entry in Entries)
        {
            if (entry is null)
                throw new ArgumentException("Chapter 3 make-good entry was missing.", nameof(Entries));
            entry.Validate();
            if (!ids.Add(entry.Id))
                throw new ArgumentException($"Chapter 3 make-good id was duplicated: {entry.Id}.", nameof(Entries));
        }
        if (LastProcessedDay < -1)
            throw new ArgumentException("Chapter 3 ledger last processed day was invalid.", nameof(LastProcessedDay));
        if (NextId <= 0)
            throw new ArgumentException("Chapter 3 next make-good id was invalid.", nameof(NextId));
    }
}
