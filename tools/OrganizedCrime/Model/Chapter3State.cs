namespace OrganizedCrime.Model;

public enum Chapter3Phase
{
    NotStarted,
    Active
}

public sealed record Chapter3DayTickResult(Chapter3State State, bool Started, int? ChapterDay, IReadOnlyList<Chapter3JobEffect> Effects);

/// <summary>
/// Chapter 3's own saved state: its phase, the elapsed day it started on, the
/// last elapsed day a tick processed, the job board, the make-good ledger and the weekly cuts. Pure data; all transitions are pure
/// functions returning a new state plus a result.
/// </summary>
public sealed record Chapter3State
{
    public Chapter3State(
        string PlayerId,
        Chapter3Phase Phase,
        int? StartElapsedDay,
        int LastProcessedElapsedDay,
        long Revision,
        Chapter3JobBoard? JobBoard = null,
        Chapter3MakeGoodLedger? Ledger = null,
        Chapter3WeeklyCuts? Cuts = null,
        IReadOnlyList<Chapter3PresentationReceipt>? PresentationReceipts = null,
        int PaymentShortfallCount = 0,
        Chapter3WarehouseOffer? WarehouseOffer = null,
        Chapter3ArthurVisitKind ArthurVisitPending = Chapter3ArthurVisitKind.None,
        int ArthurVisitMissedEvenings = 0,
        int ArthurVisitLastAttemptedDay = -1,
        bool ArthurCooperationDelivered = false,
        int ArthurWarningFollowups = 0,
        int ArthurRanOffs = 0,
        int ArthurBeatings = 0,
        Chapter3ProductionDemand? ActiveDemand = null,
        IReadOnlyList<string>? MadeProductIds = null,
        int LastDemandWeek = -1,
        int NextDemandId = 1,
        Chapter3DemandOutcome? LastDemandOutcome = null,
        int WarehouseCapacityTier = 1,
        int DemandsCompleted = 0,
        string? CheckpointTargetCode = null,
        int? CheckpointTargetDay = null,
        Chapter3Sweep? Sweep = null,
        double? LastSweepStartMinutes = null)
    {
        this.PlayerId = PlayerId;
        this.Phase = Phase;
        this.StartElapsedDay = StartElapsedDay;
        this.LastProcessedElapsedDay = LastProcessedElapsedDay;
        this.Revision = Revision;
        this.JobBoard = JobBoard ?? Chapter3JobBoard.Empty;
        this.Ledger = Ledger ?? Chapter3MakeGoodLedger.Empty;
        this.Cuts = Cuts ?? Chapter3WeeklyCuts.Empty;
        this.PresentationReceipts = PresentationReceipts ?? Array.Empty<Chapter3PresentationReceipt>();
        this.PaymentShortfallCount = PaymentShortfallCount;
        this.WarehouseOffer = WarehouseOffer;
        this.ArthurVisitPending = ArthurVisitPending;
        this.ArthurVisitMissedEvenings = ArthurVisitMissedEvenings;
        this.ArthurVisitLastAttemptedDay = ArthurVisitLastAttemptedDay;
        this.ArthurCooperationDelivered = ArthurCooperationDelivered;
        this.ArthurWarningFollowups = ArthurWarningFollowups;
        this.ArthurRanOffs = ArthurRanOffs;
        this.ArthurBeatings = ArthurBeatings;
        this.ActiveDemand = ActiveDemand;
        this.MadeProductIds = MadeProductIds ?? Array.Empty<string>();
        this.LastDemandWeek = LastDemandWeek;
        this.NextDemandId = NextDemandId;
        this.LastDemandOutcome = LastDemandOutcome;
        this.WarehouseCapacityTier = WarehouseCapacityTier;
        this.DemandsCompleted = DemandsCompleted;
        this.CheckpointTargetCode = CheckpointTargetCode;
        this.CheckpointTargetDay = CheckpointTargetDay;
        this.Sweep = Sweep;
        this.LastSweepStartMinutes = LastSweepStartMinutes;
        Validate();
    }

    public bool Equals(Chapter3State? other) =>
        other is not null &&
        PlayerId == other.PlayerId &&
        Phase == other.Phase &&
        StartElapsedDay == other.StartElapsedDay &&
        LastProcessedElapsedDay == other.LastProcessedElapsedDay &&
        Revision == other.Revision &&
        JobBoard == other.JobBoard &&
        Ledger == other.Ledger &&
        Cuts == other.Cuts &&
        PresentationReceipts.SequenceEqual(other.PresentationReceipts) &&
        PaymentShortfallCount == other.PaymentShortfallCount &&
        WarehouseOffer == other.WarehouseOffer &&
        ArthurVisitPending == other.ArthurVisitPending &&
        ArthurVisitMissedEvenings == other.ArthurVisitMissedEvenings &&
        ArthurVisitLastAttemptedDay == other.ArthurVisitLastAttemptedDay &&
        ArthurCooperationDelivered == other.ArthurCooperationDelivered &&
        ArthurWarningFollowups == other.ArthurWarningFollowups &&
        ArthurRanOffs == other.ArthurRanOffs &&
        ArthurBeatings == other.ArthurBeatings &&
        ActiveDemand == other.ActiveDemand &&
        MadeProductIds.SequenceEqual(other.MadeProductIds) &&
        LastDemandWeek == other.LastDemandWeek &&
        NextDemandId == other.NextDemandId &&
        LastDemandOutcome == other.LastDemandOutcome &&
        WarehouseCapacityTier == other.WarehouseCapacityTier &&
        DemandsCompleted == other.DemandsCompleted &&
        CheckpointTargetCode == other.CheckpointTargetCode &&
        CheckpointTargetDay == other.CheckpointTargetDay &&
        Sweep == other.Sweep &&
        LastSweepStartMinutes == other.LastSweepStartMinutes &&
        Raid == other.Raid &&
        LastRaidSuccessMinutes == other.LastRaidSuccessMinutes;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PlayerId);
        hash.Add(Phase);
        hash.Add(StartElapsedDay);
        hash.Add(LastProcessedElapsedDay);
        hash.Add(Revision);
        hash.Add(JobBoard);
        hash.Add(Ledger);
        hash.Add(Cuts);
        foreach (var receipt in PresentationReceipts)
            hash.Add(receipt);
        hash.Add(PaymentShortfallCount);
        hash.Add(WarehouseOffer);
        hash.Add(ArthurVisitPending);
        hash.Add(ArthurVisitMissedEvenings);
        hash.Add(ArthurVisitLastAttemptedDay);
        hash.Add(ArthurCooperationDelivered);
        hash.Add(ArthurWarningFollowups);
        hash.Add(ArthurRanOffs);
        hash.Add(ArthurBeatings);
        hash.Add(ActiveDemand);
        foreach (var made in MadeProductIds)
            hash.Add(made);
        hash.Add(LastDemandWeek);
        hash.Add(NextDemandId);
        hash.Add(LastDemandOutcome);
        hash.Add(WarehouseCapacityTier);
        hash.Add(DemandsCompleted);
        hash.Add(CheckpointTargetCode);
        hash.Add(CheckpointTargetDay);
        hash.Add(Sweep);
        hash.Add(LastSweepStartMinutes);
        hash.Add(Raid);
        hash.Add(LastRaidSuccessMinutes);
        return hash.ToHashCode();
    }

    public string PlayerId { get; init; }
    public Chapter3Phase Phase { get; init; }
    public int? StartElapsedDay { get; init; }
    public int LastProcessedElapsedDay { get; init; }
    public long Revision { get; init; }
    public Chapter3JobBoard JobBoard { get; init; }
    public Chapter3MakeGoodLedger Ledger { get; init; }
    public Chapter3WeeklyCuts Cuts { get; init; }
    public IReadOnlyList<Chapter3PresentationReceipt> PresentationReceipts { get; init; }
    public int PaymentShortfallCount { get; init; }
    public Chapter3WarehouseOffer? WarehouseOffer { get; init; }
    public Chapter3ArthurVisitKind ArthurVisitPending { get; init; }
    public int ArthurVisitMissedEvenings { get; init; }
    public int ArthurVisitLastAttemptedDay { get; init; }
    public bool ArthurCooperationDelivered { get; init; }
    public int ArthurWarningFollowups { get; init; }
    public int ArthurRanOffs { get; init; }
    public int ArthurBeatings { get; init; }
    public Chapter3ProductionDemand? ActiveDemand { get; init; }
    public IReadOnlyList<string> MadeProductIds { get; init; }
    public int LastDemandWeek { get; init; }
    public int NextDemandId { get; init; }
    public Chapter3DemandOutcome? LastDemandOutcome { get; init; }
    public int WarehouseCapacityTier { get; init; }
    public int DemandsCompleted { get; init; }
    public string? CheckpointTargetCode { get; init; }
    public int? CheckpointTargetDay { get; init; }
    public Chapter3Sweep? Sweep { get; init; }
    public double? LastSweepStartMinutes { get; init; }

    internal Chapter3RaidJournal? Raid { get; init; }
    internal double? LastRaidSuccessMinutes { get; init; }

    public static Chapter3State CreateInitial(string playerId) =>
        new(playerId, Chapter3Phase.NotStarted, null, -1, 0);

    /// <summary>
    /// Processes the day tick for an elapsed day. Idempotent: a tick for an
    /// elapsed day at or below the last processed day returns the state
    /// unchanged, so ticks never move backwards. Chapter 3 starts on the first
    /// processed tick where The Envelope is satisfied; the start morning counts
    /// as Chapter 3 day 0. Accepted ticks on an active chapter also advance the
    /// job board for the chapter day and carry its effects.
    /// </summary>
    public static Chapter3DayTickResult ProcessDayTick(Chapter3State state, int elapsedDays, bool envelopeSatisfied)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        if (elapsedDays <= state.LastProcessedElapsedDay)
            return new Chapter3DayTickResult(state, false, state.ChapterDayAt(state.LastProcessedElapsedDay), Array.Empty<Chapter3JobEffect>());

        if (state.Phase == Chapter3Phase.NotStarted && envelopeSatisfied)
        {
            var started = state with
            {
                Phase = Chapter3Phase.Active,
                StartElapsedDay = elapsedDays,
                LastProcessedElapsedDay = elapsedDays,
                Revision = state.Revision + 1
            };
            var startBoard = Chapter3JobScheduler.OnDayTick(started.JobBoard, started.PlayerId, 0);
            started = started with { JobBoard = startBoard.Board };
            return new Chapter3DayTickResult(started, true, 0, startBoard.Effects);
        }

        var advanced = state with
        {
            LastProcessedElapsedDay = elapsedDays,
            Revision = state.Revision + 1
        };
        var chapterDay = advanced.ChapterDayAt(elapsedDays);
        if (!chapterDay.HasValue)
            return new Chapter3DayTickResult(advanced, false, null, Array.Empty<Chapter3JobEffect>());
        var boardTick = Chapter3JobScheduler.OnDayTick(advanced.JobBoard, advanced.PlayerId, chapterDay.Value);
        advanced = advanced with { JobBoard = boardTick.Board };
        return new Chapter3DayTickResult(advanced, false, chapterDay, boardTick.Effects);
    }

    public int? ChapterDayAt(int elapsedDays) =>
        Phase == Chapter3Phase.Active && StartElapsedDay.HasValue
            ? elapsedDays - StartElapsedDay.Value
            : null;

    /// <summary>
    /// Warehouse-offer eligibility, wired to the live board and ledger: enough
    /// days, enough successes, no open make-good, and the offer not yet open.
    /// Ownership stays out: it is a live world fact the runtime passes to the
    /// tick, not state.
    /// </summary>
    public bool IsWarehouseOfferEligible()
    {
        if (Phase != Chapter3Phase.Active || !StartElapsedDay.HasValue)
            return false;
        if (WarehouseOffer is not null)
            return false;
        var chapterDay = LastProcessedElapsedDay - StartElapsedDay.Value;
        return Chapter3JobScheduler.IsWarehouseOfferEligible(chapterDay, JobBoard.SuccessfulJobs, Ledger.HasOpenMakeGood);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PlayerId))
            throw new ArgumentException("Chapter 3 player id was empty.", nameof(PlayerId));
        if (Phase == Chapter3Phase.Active && !StartElapsedDay.HasValue)
            throw new ArgumentException("An active Chapter 3 requires a start elapsed day.", nameof(StartElapsedDay));
        if (Phase == Chapter3Phase.NotStarted && StartElapsedDay.HasValue)
            throw new ArgumentException("An unstarted Chapter 3 must not have a start elapsed day.", nameof(StartElapsedDay));
        if (StartElapsedDay.HasValue && StartElapsedDay.Value < 0)
            throw new ArgumentException("Chapter 3 start elapsed day was negative.", nameof(StartElapsedDay));
        if (LastProcessedElapsedDay < -1)
            throw new ArgumentException("Chapter 3 last processed elapsed day was invalid.", nameof(LastProcessedElapsedDay));
        if (Phase == Chapter3Phase.Active && LastProcessedElapsedDay < StartElapsedDay!.Value)
            throw new ArgumentException("Chapter 3 cannot have processed a day before it started.", nameof(LastProcessedElapsedDay));
        if (JobBoard is null)
            throw new ArgumentException("Chapter 3 job board was missing.", nameof(JobBoard));
        JobBoard.Validate();
        if (Ledger is null)
            throw new ArgumentException("Chapter 3 make-good ledger was missing.", nameof(Ledger));
        Ledger.Validate();
        if (Cuts is null)
            throw new ArgumentException("Chapter 3 weekly cuts were missing.", nameof(Cuts));
        Cuts.Validate();
        if (PresentationReceipts is null)
            throw new ArgumentException("Chapter 3 presentation receipts were missing.", nameof(PresentationReceipts));
        foreach (var receipt in PresentationReceipts)
        {
            if (receipt is null || string.IsNullOrWhiteSpace(receipt.CorrelationId))
                throw new ArgumentException("Chapter 3 presentation receipt was invalid.", nameof(PresentationReceipts));
            if (receipt.Revision < 0)
                throw new ArgumentException("Chapter 3 presentation receipt revision was negative.", nameof(PresentationReceipts));
        }
        if (PaymentShortfallCount < 0)
            throw new ArgumentException("Chapter 3 shortfall counter was negative.", nameof(PaymentShortfallCount));
        WarehouseOffer?.Validate();
        if (!Enum.IsDefined(ArthurVisitPending))
            throw new ArgumentException("Chapter 3 Arthur visit pending kind was invalid.", nameof(ArthurVisitPending));
        if (ArthurVisitMissedEvenings < 0)
            throw new ArgumentException("Chapter 3 Arthur visit missed evenings were negative.", nameof(ArthurVisitMissedEvenings));
        if (ArthurVisitLastAttemptedDay < -1)
            throw new ArgumentException("Chapter 3 Arthur visit last attempted day was invalid.", nameof(ArthurVisitLastAttemptedDay));
        if (ArthurWarningFollowups < 0)
            throw new ArgumentException("Chapter 3 Arthur warning follow-ups were negative.", nameof(ArthurWarningFollowups));
        if (ArthurRanOffs < 0)
            throw new ArgumentException("Chapter 3 Arthur ran-offs were negative.", nameof(ArthurRanOffs));
        if (Revision < 0)
            throw new ArgumentException("Chapter 3 revision was negative.", nameof(Revision));
        ActiveDemand?.Validate();
        if (MadeProductIds is null)
            throw new ArgumentException("Chapter 3 made products were missing.", nameof(MadeProductIds));
        var seenMade = new HashSet<string>(StringComparer.Ordinal);
        foreach (var made in MadeProductIds)
        {
            if (string.IsNullOrWhiteSpace(made) || !seenMade.Add(made))
                throw new ArgumentException("Chapter 3 made products held an invalid or duplicate id.", nameof(MadeProductIds));
        }
        if (LastDemandWeek < -1)
            throw new ArgumentException("Chapter 3 last demand week was invalid.", nameof(LastDemandWeek));
        if (NextDemandId < 1)
            throw new ArgumentException("Chapter 3 next demand id was invalid.", nameof(NextDemandId));
        LastDemandOutcome?.Validate();
        if (!Chapter3WarehouseCapacityTiers.IsValidTier(WarehouseCapacityTier))
            throw new ArgumentException("Chapter 3 warehouse capacity tier was invalid.", nameof(WarehouseCapacityTier));
        if (DemandsCompleted < 0)
            throw new ArgumentException("Chapter 3 completed demand count was negative.", nameof(DemandsCompleted));
        Raid?.Validate();
        if (LastRaidSuccessMinutes is { } raidStart && !Chapter3Sweep.ValidMinutes(raidStart))
            throw new ArgumentException("Chapter 3 last raid success minutes were invalid.", nameof(LastRaidSuccessMinutes));
        if (Raid?.ActualUnits > 0 && LastRaidSuccessMinutes != Raid.StartMinutes)
            throw new ArgumentException("Chapter 3 raid seizure and cooldown disagree.", nameof(LastRaidSuccessMinutes));
        if (Raid?.NoticeReceipt is { } notice && notice != Raid.NoticeCorrelation(PlayerId))
            throw new ArgumentException("Chapter 3 raid notice had a foreign correlation.", nameof(Raid));
        Sweep?.Validate();
        if (LastSweepStartMinutes is { } sweepStart && !Chapter3Sweep.ValidMinutes(sweepStart))
            throw new ArgumentException("Chapter 3 last sweep start was invalid.", nameof(LastSweepStartMinutes));
        if (Sweep?.ActiveStartMinutes is { } activeStart && LastSweepStartMinutes != activeStart)
            throw new ArgumentException("Chapter 3 active sweep and cooldown disagree.");
        if (CheckpointTargetCode is not null && string.IsNullOrWhiteSpace(CheckpointTargetCode))
            throw new ArgumentException("Chapter 3 checkpoint target code was blank.", nameof(CheckpointTargetCode));
        if (CheckpointTargetDay.HasValue && CheckpointTargetDay.Value < 0)
            throw new ArgumentException("Chapter 3 checkpoint target day was negative.", nameof(CheckpointTargetDay));
    }
}
