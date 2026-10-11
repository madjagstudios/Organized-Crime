namespace OrganizedCrime.Model;

/// <summary>
/// Tunable Chapter 3 weekly-cut numbers, as named constants. Owner-approved
/// starting values (OC-11 step 3 economy decisions), not final balance.
/// </summary>
public static class Chapter3WeeklyCutTuning
{
    public const int CutAmount = 30_000;
    public const int GraceDays = 2;
}

public enum Chapter3WeeklyCutStatus
{
    Due,
    Paid,
    Missed
}

/// <summary>
/// One week's family cut: the week it fell due, its flat amount, the chapter
/// day it fell due, the last chapter day it can still be paid, and its status.
/// Once Missed, the cut itself closes; OC-115's ledger holds what's owed.
/// </summary>
public sealed record Chapter3WeeklyCut(
    int WeekIndex,
    int Amount,
    int DueDay,
    int GraceUntilDay,
    Chapter3WeeklyCutStatus Status)
{
    public void Validate()
    {
        if (WeekIndex < 0)
            throw new ArgumentException("Chapter 3 cut week index was negative.", nameof(WeekIndex));
        if (Amount < 0)
            throw new ArgumentException("Chapter 3 cut amount was negative.", nameof(Amount));
        if (DueDay < 0)
            throw new ArgumentException("Chapter 3 cut due day was negative.", nameof(DueDay));
        if (GraceUntilDay < DueDay)
            throw new ArgumentException("Chapter 3 cut grace day was before its due day.", nameof(GraceUntilDay));
        if (!Enum.IsDefined(typeof(Chapter3WeeklyCutStatus), Status))
            throw new ArgumentException($"Chapter 3 cut status was unknown: {(int)Status}.", nameof(Status));
    }
}

/// <summary>
/// Chapter 3's weekly cuts: every cut the family has taken, each independent,
/// so several weeks can be outstanding at once. Pure data; all transitions
/// live in <see cref="Chapter3WeeklyCutTransitions"/>.
/// </summary>
public sealed record Chapter3WeeklyCuts(
    IReadOnlyList<Chapter3WeeklyCut> Entries,
    int LastProcessedDay)
{
    public static Chapter3WeeklyCuts Empty => new(Array.Empty<Chapter3WeeklyCut>(), -1);

    public bool Equals(Chapter3WeeklyCuts? other) =>
        other is not null &&
        Entries.SequenceEqual(other.Entries) &&
        LastProcessedDay == other.LastProcessedDay;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
            hash.Add(entry);
        hash.Add(LastProcessedDay);
        return hash.ToHashCode();
    }

    public void Validate()
    {
        if (Entries is null)
            throw new ArgumentException("Chapter 3 cut entries were missing.", nameof(Entries));
        var weeks = new HashSet<int>();
        foreach (var entry in Entries)
        {
            if (entry is null)
                throw new ArgumentException("Chapter 3 cut entry was missing.", nameof(Entries));
            entry.Validate();
            if (!weeks.Add(entry.WeekIndex))
                throw new ArgumentException($"Chapter 3 cut week index was duplicated: {entry.WeekIndex}.", nameof(Entries));
        }
        if (LastProcessedDay < -1)
            throw new ArgumentException("Chapter 3 cuts last processed day was invalid.", nameof(LastProcessedDay));
    }
}
