namespace OrganizedCrime.Model;

/// <summary>
/// The six Release 1 mission archetypes, reused as the Chapter 3 ordinary job
/// types. Declaration order matches <see cref="Release1MissionCatalog"/> sequence
/// order; <see cref="Chapter3JobScheduler"/> offers DeadDrop first.
/// </summary>
public enum Chapter3JobType
{
    DeadDrop,
    PackageRecovery,
    TemporaryStorageSafehouse,
    EmergencySupply,
    OperationalShutdown,
    CashMovement
}

/// <summary>
/// An open offer: its terms (type, reward, response deadline) shown before acceptance.
/// An Emergency Supply offer also names its frozen product, chosen when the
/// offer is created so the player can decide whether they can make it.
/// </summary>
public sealed record Chapter3JobOffer(
    int OfferId,
    Chapter3JobType JobType,
    int Reward,
    int OfferedDay,
    int RespondByDay,
    string? ProductId = null,
    string? ProductName = null)
{
    internal Chapter3StorageRun? StorageRun { get; init; }
    internal Chapter3CashMovement? CashMovement { get; init; }
    internal Chapter3Shutdown? Shutdown { get; init; }
}

/// <summary>
/// An accepted ordinary job. Only one is active at a time. Delivery carries
/// the frozen dead-drop details once OC-121's freeze runs, Recovery the frozen
/// package-recovery details once OC-123's freeze runs; each stays null for
/// other job types and for accepts that predated its freeze. An Emergency
/// Supply job reuses Delivery for its named drop and records its 12-hour
/// window in game minutes; its Reward is the nominal value, never paid.
/// </summary>
public sealed record Chapter3ActiveJob(
    int JobId,
    int OfferId,
    Chapter3JobType JobType,
    int Reward,
    int AcceptedDay,
    int DeadlineDay,
    bool ExtensionUsed,
    Chapter3DeadDropDelivery? Delivery = null,
    Chapter3PackageRecovery? Recovery = null,
    double? AcceptedGameMinutes = null,
    double? ExpiresGameMinutes = null)
{
    internal Chapter3StorageRun? StorageRun { get; init; }
    internal Chapter3CashMovement? CashMovement { get; init; }
    internal Chapter3Shutdown? Shutdown { get; init; }
}

/// <summary>
/// Chapter 3's job board: the open offer, the active job, the cooldown, the
/// refusal streak and the counters later features depend on. Pure data; all
/// transitions live in <see cref="Chapter3JobScheduler"/>.
/// </summary>
public sealed record Chapter3JobBoard(
    Chapter3JobOffer? OpenOffer,
    Chapter3ActiveJob? ActiveJob,
    int? CooldownUntilDay,
    int LastProcessedDay,
    Chapter3JobType? LastTypeOffered,
    IReadOnlyList<Chapter3JobType> TypesSeen,
    int RefusalStreak,
    bool NellHintIssued,
    bool CooperationVisitIssued,
    int SuccessfulJobs,
    int SuccessfulJobsSinceWarehousePurchase,
    int NextOfferId,
    int NextJobId,
    int? LastDeclinedOfferId = null,
    int? LastExpiredOfferId = null,
    int? LastRefusedOfferId = null,
    Chapter3JobCompletion? LastCompletedJob = null,
    int? LastEmergencySupplyOfferId = null)
{
    public static Chapter3JobBoard Empty => new(
        null, null, null, -1, null, Array.Empty<Chapter3JobType>(),
        0, false, false, 0, 0, 1, 1);

    public bool Equals(Chapter3JobBoard? other) =>
        other is not null &&
        OpenOffer == other.OpenOffer &&
        ActiveJob == other.ActiveJob &&
        CooldownUntilDay == other.CooldownUntilDay &&
        LastProcessedDay == other.LastProcessedDay &&
        LastTypeOffered == other.LastTypeOffered &&
        TypesSeen.SequenceEqual(other.TypesSeen) &&
        RefusalStreak == other.RefusalStreak &&
        NellHintIssued == other.NellHintIssued &&
        CooperationVisitIssued == other.CooperationVisitIssued &&
        SuccessfulJobs == other.SuccessfulJobs &&
        SuccessfulJobsSinceWarehousePurchase == other.SuccessfulJobsSinceWarehousePurchase &&
        NextOfferId == other.NextOfferId &&
        LastDeclinedOfferId == other.LastDeclinedOfferId &&
        LastExpiredOfferId == other.LastExpiredOfferId &&
        LastRefusedOfferId == other.LastRefusedOfferId &&
        LastCompletedJob == other.LastCompletedJob &&
        LastEmergencySupplyOfferId == other.LastEmergencySupplyOfferId &&
        NextJobId == other.NextJobId;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OpenOffer);
        hash.Add(ActiveJob);
        hash.Add(CooldownUntilDay);
        hash.Add(LastProcessedDay);
        hash.Add(LastTypeOffered);
        foreach (var type in TypesSeen)
            hash.Add(type);
        hash.Add(RefusalStreak);
        hash.Add(NellHintIssued);
        hash.Add(CooperationVisitIssued);
        hash.Add(SuccessfulJobs);
        hash.Add(SuccessfulJobsSinceWarehousePurchase);
        hash.Add(NextOfferId);
        hash.Add(LastDeclinedOfferId);
        hash.Add(LastExpiredOfferId);
        hash.Add(LastRefusedOfferId);
        hash.Add(LastCompletedJob);
        hash.Add(LastEmergencySupplyOfferId);
        hash.Add(NextJobId);
        return hash.ToHashCode();
    }

    public void Validate()
    {
        if (OpenOffer is not null)
        {
            if (OpenOffer.OfferId <= 0)
                throw new ArgumentException("Chapter 3 offer id was invalid.", nameof(OpenOffer));
            ValidateType(OpenOffer.JobType, nameof(OpenOffer));
            if (OpenOffer.Reward < 0)
                throw new ArgumentException("Chapter 3 offer reward was negative.", nameof(OpenOffer));
            if (OpenOffer.OfferedDay < 0)
                throw new ArgumentException("Chapter 3 offer day was negative.", nameof(OpenOffer));
            if (OpenOffer.RespondByDay < OpenOffer.OfferedDay)
                throw new ArgumentException("Chapter 3 offer response day was before its offer day.", nameof(OpenOffer));
            if (OpenOffer.StorageRun is not null)
            {
                if (OpenOffer.JobType != Chapter3JobType.TemporaryStorageSafehouse)
                    throw new ArgumentException("Storage terms belong to storage offers.");
                OpenOffer.StorageRun.Validate(OpenOffer.Reward, offered: true);
            }
            if (OpenOffer.Shutdown is { } shutdownOffer)
            {
                if (OpenOffer.JobType != Chapter3JobType.OperationalShutdown || OpenOffer.CashMovement is not null)
                    throw new ArgumentException("Shutdown terms belong to shutdown offers.");
                shutdownOffer.Validate(OpenOffer.Reward, offered: true);
            }
            if (OpenOffer.CashMovement is not null)
            {
                if (OpenOffer.JobType != Chapter3JobType.CashMovement || OpenOffer.StorageRun is not null || OpenOffer.Shutdown is not null)
                    throw new ArgumentException("Cash terms belong to cash offers.");
                OpenOffer.CashMovement.Validate(OpenOffer.Reward, offered: true);
            }
            if (OpenOffer.ProductId is null != (OpenOffer.ProductName is null))
                throw new ArgumentException("Chapter 3 offer product was half frozen.", nameof(OpenOffer));
            if (OpenOffer.ProductId is not null)
            {
                if (OpenOffer.JobType != Chapter3JobType.EmergencySupply)
                    throw new ArgumentException("Chapter 3 offer product was frozen for a paid job.", nameof(OpenOffer));
                if (string.IsNullOrWhiteSpace(OpenOffer.ProductId) || string.IsNullOrWhiteSpace(OpenOffer.ProductName))
                    throw new ArgumentException("Chapter 3 offer product was blank.", nameof(OpenOffer));
            }
        }
        if (ActiveJob is not null)
        {
            if (ActiveJob.JobId <= 0)
                throw new ArgumentException("Chapter 3 job id was invalid.", nameof(ActiveJob));
            if (ActiveJob.OfferId <= 0)
                throw new ArgumentException("Chapter 3 job offer id was invalid.", nameof(ActiveJob));
            ValidateType(ActiveJob.JobType, nameof(ActiveJob));
            if (ActiveJob.Reward < 0)
                throw new ArgumentException("Chapter 3 job reward was negative.", nameof(ActiveJob));
            if (ActiveJob.AcceptedDay < 0)
                throw new ArgumentException("Chapter 3 job accept day was negative.", nameof(ActiveJob));
            if (ActiveJob.DeadlineDay < ActiveJob.AcceptedDay)
                throw new ArgumentException("Chapter 3 job deadline was before its accept day.", nameof(ActiveJob));
            ActiveJob.Delivery?.Validate();
            ActiveJob.Recovery?.Validate();
            if (ActiveJob.StorageRun is not null)
            {
                if (ActiveJob.JobType != Chapter3JobType.TemporaryStorageSafehouse || ActiveJob.Delivery is not null || ActiveJob.Recovery is not null)
                    throw new ArgumentException("Storage progress belongs to storage jobs.");
                ActiveJob.StorageRun.Validate(ActiveJob.Reward, offered: false);
            }
            if (ActiveJob.Shutdown is { } shutdownJob)
            {
                if (ActiveJob.JobType != Chapter3JobType.OperationalShutdown || ActiveJob.CashMovement is not null || ActiveJob.Delivery is not null || ActiveJob.Recovery is not null)
                    throw new ArgumentException("Shutdown progress belongs to shutdown jobs.");
                shutdownJob.Validate(ActiveJob.Reward, offered: false);
            }
            if (ActiveJob.CashMovement is not null)
            {
                if (ActiveJob.JobType != Chapter3JobType.CashMovement || ActiveJob.Shutdown is not null || ActiveJob.StorageRun is not null || ActiveJob.Delivery is not null || ActiveJob.Recovery is not null)
                    throw new ArgumentException("Cash progress belongs to cash jobs.");
                ActiveJob.CashMovement.Validate(ActiveJob.Reward, offered: false);
            }
            if (ActiveJob.AcceptedGameMinutes.HasValue != ActiveJob.ExpiresGameMinutes.HasValue)
                throw new ArgumentException("Chapter 3 job window was half recorded.", nameof(ActiveJob));
            if (ActiveJob.AcceptedGameMinutes.HasValue)
            {
                if (ActiveJob.JobType != Chapter3JobType.EmergencySupply)
                    throw new ArgumentException("Chapter 3 job window was recorded for a paid job.", nameof(ActiveJob));
                var accepted = ActiveJob.AcceptedGameMinutes.Value;
                var expires = ActiveJob.ExpiresGameMinutes!.Value;
                if (!double.IsFinite(accepted) || !double.IsFinite(expires) || accepted < 0)
                    throw new ArgumentException("Chapter 3 job window was invalid.", nameof(ActiveJob));
                if (expires != accepted + Chapter3JobTuning.EmergencySupplyWindowMinutes)
                    throw new ArgumentException("Chapter 3 job window was not twelve hours.", nameof(ActiveJob));
            }
        }
        if (CooldownUntilDay.HasValue && CooldownUntilDay.Value < 0)
            throw new ArgumentException("Chapter 3 cooldown day was negative.", nameof(CooldownUntilDay));
        if (LastProcessedDay < -1)
            throw new ArgumentException("Chapter 3 board last processed day was invalid.", nameof(LastProcessedDay));
        if (LastTypeOffered.HasValue)
            ValidateType(LastTypeOffered.Value, nameof(LastTypeOffered));
        if (TypesSeen is null)
            throw new ArgumentException("Chapter 3 seen types were missing.", nameof(TypesSeen));
        foreach (var type in TypesSeen)
            ValidateType(type, nameof(TypesSeen));
        if (RefusalStreak < 0)
            throw new ArgumentException("Chapter 3 refusal streak was negative.", nameof(RefusalStreak));
        if (SuccessfulJobs < 0)
            throw new ArgumentException("Chapter 3 success counter was negative.", nameof(SuccessfulJobs));
        if (SuccessfulJobsSinceWarehousePurchase < 0)
            throw new ArgumentException("Chapter 3 post-warehouse success counter was negative.", nameof(SuccessfulJobsSinceWarehousePurchase));
        if (NextOfferId <= 0)
            throw new ArgumentException("Chapter 3 next offer id was invalid.", nameof(NextOfferId));
        if (LastDeclinedOfferId.HasValue && LastDeclinedOfferId.Value <= 0)
            throw new ArgumentException("Chapter 3 declined offer id was invalid.", nameof(LastDeclinedOfferId));
        if (LastExpiredOfferId.HasValue && LastExpiredOfferId.Value <= 0)
            throw new ArgumentException("Chapter 3 expired offer id was invalid.", nameof(LastExpiredOfferId));
        if (LastRefusedOfferId.HasValue && LastRefusedOfferId.Value <= 0)
            throw new ArgumentException("Chapter 3 refused offer id was invalid.", nameof(LastRefusedOfferId));
        LastCompletedJob?.Validate();
        if (LastEmergencySupplyOfferId.HasValue && LastEmergencySupplyOfferId.Value <= 0)
            throw new ArgumentException("Chapter 3 last emergency supply offer id was invalid.", nameof(LastEmergencySupplyOfferId));
        if (NextJobId <= 0)
            throw new ArgumentException("Chapter 3 next job id was invalid.", nameof(NextJobId));
    }

    private static void ValidateType(Chapter3JobType type, string field)
    {
        if (!Enum.IsDefined(typeof(Chapter3JobType), type))
            throw new ArgumentException($"Chapter 3 job type was unknown: {(int)type}.", field);
    }
}
