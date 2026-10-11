namespace OrganizedCrime.Model;

internal enum Chapter3RaidRemovalState { Planned, Pending, Confirmed, Ambiguous }

// A pending intent survives faults and reload. Ambiguous is terminal: never
// reinterpret a later matching read as permission to take the slot again.
internal sealed record Chapter3RaidRemoval(Chapter3RaidTake Take,
    Chapter3RaidRemovalState State = Chapter3RaidRemovalState.Planned)
{
    // Optional for legacy receipts: unknown totals never authorize a retry.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? PreFingerprintTotal { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int PostQuantity => Take.PreQuantity - Take.Packages;
}

internal sealed record Chapter3RaidJournal(string Id, string SiteCode, string SiteName,
    double StartMinutes, double DeadlineMinutes, long TotalUnits, long Budget,
    IReadOnlyList<Chapter3RaidRemoval> Slots, bool Completed = false,
    bool CoolingApplied = false, string? NoticeReceipt = null)
{
    internal long ActualUnits => Slots.Where(s => s.State == Chapter3RaidRemovalState.Confirmed).Sum(s => s.Take.Units);
    internal long PlannedUnits => Slots.Sum(s => s.Take.Units);
    internal string NoticeCorrelation(string playerId) => Chapter3PresentationReceipt.Correlation(playerId, "raid-" + Id);
    internal string NoticeText => $"Police hit {SiteName}. They took {ActualUnits} units.";

    public bool Equals(Chapter3RaidJournal? other) => other is not null &&
        Id == other.Id && SiteCode == other.SiteCode && SiteName == other.SiteName &&
        StartMinutes == other.StartMinutes && DeadlineMinutes == other.DeadlineMinutes &&
        TotalUnits == other.TotalUnits && Budget == other.Budget && Slots.SequenceEqual(other.Slots) &&
        Completed == other.Completed && CoolingApplied == other.CoolingApplied && NoticeReceipt == other.NoticeReceipt;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(SiteCode); hash.Add(SiteName);
        hash.Add(StartMinutes); hash.Add(DeadlineMinutes); hash.Add(TotalUnits); hash.Add(Budget);
        foreach (var slot in Slots) hash.Add(slot);
        hash.Add(Completed); hash.Add(CoolingApplied); hash.Add(NoticeReceipt);
        return hash.ToHashCode();
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(SiteCode) || string.IsNullOrWhiteSpace(SiteName) ||
            !Chapter3Sweep.ValidMinutes(StartMinutes) || !Chapter3Sweep.ValidMinutes(DeadlineMinutes) || DeadlineMinutes <= StartMinutes ||
            TotalUnits < 1 || Budget != TotalUnits / 4 || Slots is null || Slots.Count == 0)
            throw new ArgumentException("Invalid raid identity, interval or frozen budget.");
        var identities = new HashSet<Chapter3RaidSlotIdentity>();
        long planned = 0;
        string? previous = null;
        var pending = 0;
        foreach (var slot in Slots)
        {
            var take = slot?.Take;
            if (take is null || take.Identity.StorageGuid == Guid.Empty || take.Identity.SlotIndex < 0 ||
                !identities.Add(take.Identity) || string.IsNullOrWhiteSpace(take.Fingerprint) ||
                take.Packages < 1 || take.PreQuantity < take.Packages || take.Units < take.Packages ||
                take.Units % take.Packages != 0 || !Enum.IsDefined(slot!.State) ||
                (slot.PreFingerprintTotal is { } total && total < take.PreQuantity))
                throw new ArgumentException("Invalid raid slot receipt.");
            var key = $"{take.Identity.StorageGuid:D}/{take.Identity.SlotIndex:D10}";
            if (previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0)
                throw new ArgumentException("Raid slots must retain frozen GUID/slot order.");
            previous = key;
            if (take.Units > Budget - planned) throw new ArgumentException("Raid plan exceeds its budget.");
            planned += take.Units;
            if (slot.State == Chapter3RaidRemovalState.Pending) pending++;
        }
        foreach (var group in Slots.GroupBy(s => (s.Take.Identity.StorageGuid, s.Take.Fingerprint)))
        {
            var totals = group.Where(s => s.PreFingerprintTotal is not null).Select(s => s.PreFingerprintTotal!.Value).Distinct().ToArray();
            if (totals.Length > 1 || (totals.Length == 1 && group.Sum(s => (long)s.Take.Packages) > totals[0]))
                throw new ArgumentException("Invalid raid fingerprint total.");
        }
        if (pending > 1 || (CoolingApplied && ActualUnits == 0) ||
            (NoticeReceipt is not null && (!Completed || ActualUnits == 0 || string.IsNullOrWhiteSpace(NoticeReceipt))))
            throw new ArgumentException("Invalid raid progress or notice receipt.");
    }
}
