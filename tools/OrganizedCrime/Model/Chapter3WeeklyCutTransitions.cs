namespace OrganizedCrime.Model;

public sealed record Chapter3WeeklyCutTransition(Chapter3WeeklyCuts Cuts, IReadOnlyList<Chapter3WeeklyCutEffect> Effects);

/// <summary>
/// Takes the family's weekly cut while the player owns the Fish Warehouse and
/// closes each cut as paid or missed. Pure transitions: each takes the cuts
/// record and returns the new record plus the effects it produced. One cut per
/// week, keyed by week index, so a repeated or stale week pass returns the
/// record unchanged with no effects. Never charges money; later tickets do that.
/// </summary>
public static class Chapter3WeeklyCutTransitions
{
    public static Chapter3WeeklyCutTransition OnWeekPass(
        Chapter3WeeklyCuts cuts, int weekIndex, int chapterDay, bool warehouseOwned)
    {
        if (cuts is null)
            throw new ArgumentNullException(nameof(cuts));
        if (weekIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(weekIndex), "Chapter 3 cut week index was negative.");
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay), "Chapter 3 cut due day was negative.");
        if (!warehouseOwned)
            return new Chapter3WeeklyCutTransition(cuts, Array.Empty<Chapter3WeeklyCutEffect>());
        if (cuts.Entries.Any(entry => entry.WeekIndex == weekIndex))
            return new Chapter3WeeklyCutTransition(cuts, Array.Empty<Chapter3WeeklyCutEffect>());

        var entry = new Chapter3WeeklyCut(
            weekIndex, Chapter3WeeklyCutTuning.CutAmount, chapterDay,
            chapterDay + Chapter3WeeklyCutTuning.GraceDays, Chapter3WeeklyCutStatus.Due);
        var next = cuts with { Entries = cuts.Entries.Append(entry).ToArray() };
        return new Chapter3WeeklyCutTransition(next, new[] { new Chapter3WeeklyCutDueEffect(entry) });
    }

    public static Chapter3WeeklyCutTransition OnDayTick(Chapter3WeeklyCuts cuts, int chapterDay)
    {
        if (cuts is null)
            throw new ArgumentNullException(nameof(cuts));

        if (chapterDay <= cuts.LastProcessedDay)
            return new Chapter3WeeklyCutTransition(cuts, Array.Empty<Chapter3WeeklyCutEffect>());

        var entries = cuts.Entries.ToList();
        var effects = new List<Chapter3WeeklyCutEffect>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Status != Chapter3WeeklyCutStatus.Due || chapterDay <= entry.GraceUntilDay)
                continue;
            entry = entry with { Status = Chapter3WeeklyCutStatus.Missed };
            entries[i] = entry;
            effects.Add(new Chapter3WeeklyCutMissedEffect(entry));
        }

        var next = cuts with { Entries = entries, LastProcessedDay = chapterDay };
        return new Chapter3WeeklyCutTransition(next, effects);
    }

    public static Chapter3WeeklyCutTransition Pay(Chapter3WeeklyCuts cuts, int weekIndex, int chapterDay)
    {
        if (cuts is null)
            throw new ArgumentNullException(nameof(cuts));
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay), "Chapter 3 cut payment day was negative.");

        var index = FindIndex(cuts, weekIndex);
        if (index < 0)
            return new Chapter3WeeklyCutTransition(cuts, Array.Empty<Chapter3WeeklyCutEffect>());
        var entry = cuts.Entries[index];
        if (entry.Status != Chapter3WeeklyCutStatus.Due)
            return new Chapter3WeeklyCutTransition(cuts, Array.Empty<Chapter3WeeklyCutEffect>());

        var paid = entry with { Status = Chapter3WeeklyCutStatus.Paid };
        var entries = cuts.Entries.ToList();
        entries[index] = paid;
        return new Chapter3WeeklyCutTransition(
            cuts with { Entries = entries }, new[] { new Chapter3WeeklyCutPaidEffect(paid) });
    }

    private static int FindIndex(Chapter3WeeklyCuts cuts, int weekIndex)
    {
        for (var i = 0; i < cuts.Entries.Count; i++)
        {
            if (cuts.Entries[i].WeekIndex == weekIndex)
                return i;
        }
        return -1;
    }
}
