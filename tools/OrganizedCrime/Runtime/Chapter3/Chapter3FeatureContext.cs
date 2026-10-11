using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 coordinator's shared state, reached by the feature
// classes and the QA facade. Every property below is one private field moved
// verbatim out of Chapter3RuntimeService (same type, same initializer); the
// coordinator assigns the constructor-bound ones in the same order. All
// access runs while the coordinator holds its gate. The wallet, clock,
// presentation-readiness and ownership helpers moved here too because the
// features share them, along with the pure format statics every feature logs
// through. Bodies are verbatim; only the receiver changed.
internal sealed class Chapter3FeatureContext
{
    public IRelease1StoryHostContext Context { get; set; } = null!;
    public IChapter3Repository Repository { get; set; } = null!;
    public Func<string, IChapter3Repository>? RepositoryFactory { get; set; }
    public IChapter3StoryGate StoryGate { get; set; } = null!;
    public IChapter3DayClock Clock { get; set; } = null!;
    public IChapter3WarehouseOwnership? Ownership { get; set; }
    public Action<string> Log { get; set; } = null!;
    public IChapter3Wallet? Wallet { get; set; }
    public IChapter3DeliveryWorld? DeadDropWorld { get; set; }
    internal Func<Release1HoldRoomSnapshot?>? StorageRoomReader { get; set; }
    internal Func<Release1WorldPoint?>? StorageRoomMarker { get; set; }
    internal Chapter3StorageFeature Storage { get; set; } = null!;
    internal IHoldRoomWorld? CashWorld { get; set; }
    internal Chapter3CashFeature Cash { get; set; } = null!;
    internal IProductionActivityWorld? ShutdownWorld { get; set; }
    internal Chapter3ShutdownFeature Shutdown { get; set; } = null!;
    public IChapter3WarehouseAcquisition? WarehouseAcquisition { get; set; }
    public IChapter3WarehouseCapacity? WarehouseCapacity { get; set; }
    public IFishWarehouseStaffReturnBannerSender? StaffReturnBannerSender { get; set; }
    public Func<Release1ChiefProtectionSnapshot?>? ProtectionSnapshot { get; set; }
    public IRelease1ChiefProtectionQa? ChiefProtectionQa { get; set; }
    public OcOfficerRosterService? OfficerRoster { get; set; }
    public IRelease1ChiefForcedWantedQa? ChiefForcedWantedQa { get; set; }
    public string? BoundDropGuid { get; set; }
    public IRelease1SmallCourtesyDropSubscription? DropSubscription { get; set; }
    public string? LockedDropGuid { get; set; }
    public int LockedSlotIndex { get; set; } = -1;
    public OnceLog LoggedDeliveryHolds { get; set; } = null!;
    public int? UnfrozenQuestLoggedJobId { get; set; }
    public Chapter3DeliveryFeature.Chapter3PendingRemoval? PendingRemoval { get; set; }
    public IChapter3ProductionDemandWorld? DemandWorld { get; set; }
    public IReadOnlyList<Chapter3DemandStationSnapshot>? DemandStationBaseline { get; set; }
    public int DemandLockedSlotIndex { get; set; } = -1;
    public string? DemandHoldKey { get; set; }
    public bool DemandLockerNameReceiptLogged { get; set; }
    public bool ForceNextEmergencyOffer { get; set; }
    public Chapter3State? State { get; set; }
    public Chapter3State? SaveSnapshot { get; set; }
    public Chapter3RuntimePhase Phase { get; set; } = Chapter3RuntimePhase.Detached;
    public bool Disposed { get; set; }
    public string? QuarantinedRawContents { get; set; }
    public int? LastOvernightLineElapsedDay { get; set; }
    public bool LoadBuildArmed { get; set; }
    public bool UnsettledBuildNotified { get; set; }
    public IChapter3ArthurWorld? ArthurWorld { get; set; }
    public IChapter3HqPresence? HqPresence { get; set; }
    public Func<float> ArthurClock { get; set; } = static () => unchecked((float)Environment.TickCount64 / 1000f);
    public Chapter3ArthurVisitPump ArthurPump { get; set; } = null!;
    public IChapter3ArthurVisitConditions? VisitConditions { get; set; }
    public IChapter3BeatProbeWorld? BeatProbeWorld { get; set; }
    public Chapter3BeatInvincibleProbe? BeatInvincible { get; set; }
    public IChapter3BeatingWorld? BeatingWorld { get; set; }
    public IChapter3BeatingHq? BeatingHq { get; set; }
    public Chapter3ArthurBeating? Beating { get; set; }
    public IChapter3ArthurPhone? ArthurPhone { get; set; }
    public Chapter3ArthurFeature.Chapter3ArthurVisitProgress? VisitProgress { get; set; }
    public float LastVisitTickAt { get; set; } = float.NegativeInfinity;
    public string? LastVisitStartFailure { get; set; }

    /// <summary>
    /// Canonical game minutes, Short Notice's rule: a Ready read, finite, and
    /// non-negative. False on any fault or a missing world, so a broken clock
    /// only skips the expiry pass or refuses the accept.
    /// </summary>
    internal bool TryReadCanonicalMinutesLocked(out double totalMinutes)
    {
        totalMinutes = 0d;
        var world = DeadDropWorld;
        if (world is null)
            return false;
        try
        {
            if (world.TryReadCanonicalTotalMinutes(out var minutes) != Release1SmallCourtesyWorldReadStatus.Ready ||
                !double.IsFinite(minutes) || minutes < 0)
                return false;
            totalMinutes = minutes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A reply button applies to the last processed chapter day, like the QA
    /// commands: the tap lands between ticks, never mid-tick.
    /// </summary>
    internal bool PresentationCommandReady(out int chapterDay)
    {
        chapterDay = 0;
        if (Disposed || Phase != Chapter3RuntimePhase.Active || State is null)
            return false;
        var day = State.ChapterDayAt(State.LastProcessedElapsedDay);
        if (!day.HasValue)
            return false;
        chapterDay = day.Value;
        return true;
    }

    internal Chapter3WalletDebit DebitLocked(int amount)
    {
        var wallet = Wallet;
        if (wallet is null)
            return Chapter3WalletDebit.Unavailable;
        try { return wallet.TryDebit(amount); }
        catch (Exception exception)
        {
            Log($"Chapter 3 wallet debit faulted: {exception.GetType().Name}");
            return Chapter3WalletDebit.Unavailable;
        }
    }

    internal Chapter3WalletDebit DebitLocked(int amount, int maximum)
    {
        var wallet = Wallet;
        if (wallet is null)
            return Chapter3WalletDebit.Unavailable;
        try { return wallet.TryDebit(amount, maximum); }
        catch (Exception exception)
        {
            Log($"Chapter 3 wallet debit faulted: {exception.GetType().Name}");
            return Chapter3WalletDebit.Unavailable;
        }
    }

    internal Chapter3WalletCredit CreditLocked(int amount)
    {
        var wallet = Wallet;
        if (wallet is null)
            return Chapter3WalletCredit.Unavailable;
        try { return wallet.TryCredit(amount); }
        catch (Exception exception)
        {
            Log($"Chapter 3 wallet credit faulted: {exception.GetType().Name}");
            return Chapter3WalletCredit.Unavailable;
        }
    }

    internal Chapter3WalletCredit CreditLocked(int amount, int maximum)
    {
        var wallet = Wallet;
        if (wallet is null)
            return Chapter3WalletCredit.Unavailable;
        try { return wallet.TryCredit(amount, maximum); }
        catch (Exception exception)
        {
            Log($"Chapter 3 wallet credit faulted: {exception.GetType().Name}");
            return Chapter3WalletCredit.Unavailable;
        }
    }

    internal bool ReadOwnership()
    {
        if (Ownership is null)
            return false;
        try { return Ownership.IsOwned(); }
        catch (Exception ex)
        {
            OrganizedCrimeLog.Warning($"Chapter 3 warehouse ownership read faulted; treating the warehouse as unowned: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The week index for an elapsed day. S1API exposes no week counter: only
    /// OnWeekPass, ElapsedDays and CurrentDay (verified by reflection against the
    /// installed 3.2.1-beta.7 DLL and the S1API preview source) - so the index is
    /// elapsed days over seven. The window edges don't need to match the game's
    /// week boundary: any 7-day spacing yields distinct indices, and the cuts
    /// record keys idempotency by week index, so repeats are no-ops.
    /// </summary>
    internal static int WeekIndexAt(int elapsedDays) => elapsedDays / 7;

    internal static string FormatEffect(Chapter3JobEffect effect) => effect switch
    {
        Chapter3OfferCreatedEffect created => FormatOfferCreated(created.Offer),
        Chapter3OfferExpiredEffect expired => $"Chapter 3 offer expired: {expired.Offer.JobType}; refusal streak {expired.RefusalStreak}",
        Chapter3NellHintEffect hint => $"Chapter 3 refusal streak {hint.RefusalStreak}: Nell hint",
        Chapter3ArthurCooperationVisitEffect visit => $"Chapter 3 refusal streak {visit.RefusalStreak}: Arthur cooperation visit",
        Chapter3JobCompletedEffect done => done.Succeeded
            ? $"Chapter 3 job {done.JobId} completed: {done.JobType}"
            : $"Chapter 3 job {done.JobId} failed: {done.JobType}",
        Chapter3MakeGoodRequiredEffect makeGood => $"Chapter 3 make-good required: job {makeGood.JobId} {makeGood.JobType}, reference amount ${makeGood.ReferenceAmount:N0}",
        Chapter3DeadlineExtendedEffect extended => $"Chapter 3 deadline extended: job {extended.JobId}, new deadline day {extended.NewDeadlineDay}, cost ${extended.Cost:N0}",
        _ => $"Chapter 3 job effect: {effect.GetType().Name}"
    };

    internal static string FormatOfferCreated(Chapter3JobOffer offer) =>
        offer.JobType == Chapter3JobType.EmergencySupply
            ? $"Chapter 3 offer: {offer.JobType}, {Chapter3JobTuning.BricksForReward(offer.Reward)} bricks of {offer.ProductName ?? "the product"} (nominal ${offer.Reward:N0}), respond by day {offer.RespondByDay}"
            : $"Chapter 3 offer: {offer.JobType}, reward ${offer.Reward:N0}, respond by day {offer.RespondByDay}";

    internal static string FormatLedgerEffect(Chapter3MakeGoodEffect effect) => effect switch
    {
        Chapter3MakeGoodOpenedEffect opened => $"Chapter 3 make-good opened: {Chapter3MakeGoods.DescribeSource(opened.MakeGood.Source)}, ${opened.MakeGood.Amount:N0}, due day {opened.MakeGood.DueDay}",
        Chapter3MakeGoodOverdueEffect overdue => $"Chapter 3 make-good {overdue.MakeGood.Id} overdue: ${overdue.MakeGood.Amount:N0} unpaid past day {overdue.MakeGood.DueDay}",
        Chapter3WarningDueEffect warning => $"Chapter 3 make-good {warning.MakeGood.Id}: Arthur warning due",
        Chapter3BeatingDueEffect beating => $"Chapter 3 make-good {beating.MakeGood.Id}: Arthur beating due",
        Chapter3MakeGoodPaidEffect paid => $"Chapter 3 make-good {paid.MakeGood.Id} paid: ${paid.MakeGood.Amount:N0}",
        _ => $"Chapter 3 make-good effect: {effect.GetType().Name}"
    };

    internal static string FormatCutEffect(Chapter3WeeklyCutEffect effect) => effect switch
    {
        Chapter3WeeklyCutDueEffect due => $"Chapter 3 weekly cut due: week {due.Cut.WeekIndex}, ${due.Cut.Amount:N0}, grace until day {due.Cut.GraceUntilDay}",
        Chapter3WeeklyCutPaidEffect paid => $"Chapter 3 weekly cut paid: week {paid.Cut.WeekIndex}, ${paid.Cut.Amount:N0}",
        Chapter3WeeklyCutMissedEffect missed => $"Chapter 3 weekly cut missed: week {missed.Cut.WeekIndex}, ${missed.Cut.Amount:N0} unpaid past day {missed.Cut.GraceUntilDay}",
        _ => $"Chapter 3 weekly cut effect: {effect.GetType().Name}"
    };

    internal static string DescribeVisitKind(Chapter3ArthurVisitKind kind) =>
        kind == Chapter3ArthurVisitKind.Cooperation ? "cooperation" : kind == Chapter3ArthurVisitKind.Beating ? "beating" : "warning";
}
