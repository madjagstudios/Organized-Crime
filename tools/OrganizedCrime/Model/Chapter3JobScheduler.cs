using System.Text;

namespace OrganizedCrime.Model;

/// <summary>
/// Tunable Chapter 3 job numbers, as named constants. Starting values, not final balance.
/// </summary>
public static class Chapter3JobTuning
{
    public const int MinReward = 15_000;
    public const int MaxReward = 20_000;
    public const int RewardStep = 500;
    public const int OfferResponseDays = 1;
    public const int CooldownDays = 2;
    public const int CompletionDeadlineDays = 2;
    public const int NellHintStreak = 3;
    public const int CooperationVisitStreak = 5;
    public const int ExtensionUnlockSuccesses = 3;
    public const int ExtensionDays = 1;
    public const int ExtensionCostPercent = 25;
    public const int WarehouseEligibilityDay = 18;
    public const int WarehouseEligibilitySuccesses = 5;
    /// <summary>
    /// The job types the scheduler may offer: only jobs the player can
    /// actually do. Later archetype tickets extend this set; declaration order
    /// is the selection pool order.
    /// </summary>
    public static readonly Chapter3JobType[] RunnableJobTypes = new[] { Chapter3JobType.DeadDrop, Chapter3JobType.PackageRecovery, Chapter3JobType.EmergencySupply, Chapter3JobType.TemporaryStorageSafehouse };
    /// <summary>
    /// The runnable types that pay a reward. Emergency Supply is occasional
    /// and unpaid, so paid selection draws from this pool instead.
    /// </summary>
    public static readonly Chapter3JobType[] PaidJobTypes = RunnableJobTypes
        .Where(type => type != Chapter3JobType.EmergencySupply && type != Chapter3JobType.TemporaryStorageSafehouse).ToArray();
    // Storage promotion runs in the offer feature after live HQ eligibility.
    // Keep the pure sibling pool stable when that read surface is absent.
    /// <summary>
    /// The Emergency Supply window: 12 in-game hours from Accept, measured in
    /// canonical game minutes.
    /// </summary>
    public const double EmergencySupplyWindowMinutes = 720d;
    /// <summary>
    /// Emergency Supply eligibility spacing in offer ids: at least 3 offers
    /// go by between one Emergency Supply offer and the next, and the first
    /// three offers are never Emergency Supply.
    /// </summary>
    public const int EmergencySupplyMinOfferIdGap = 4;
    /// <summary>
    /// The Emergency Supply roll: when eligible, a deterministic 1-in-3
    /// chance per offer.
    /// </summary>
    public const int EmergencySupplyChanceDenominator = 3;
    /// <summary>
    /// Staged bricks scale with the reward: two bricks up to $17,000, three
    /// from $17,500 up. Shared by DeadDrop and PackageRecovery.
    /// </summary>
    public const int SmallBrickCount = 2;
    public const int LargeBrickCount = 3;
    public const int LargeBrickMinReward = 17_500;

    public static int BricksForReward(int reward) =>
        reward >= LargeBrickMinReward ? LargeBrickCount : SmallBrickCount;
}

public sealed record Chapter3JobTransition(Chapter3JobBoard Board, IReadOnlyList<Chapter3JobEffect> Effects);

public sealed record Chapter3DeadlineExtensionResult(
    Chapter3JobBoard Board,
    bool Extended,
    int Cost,
    IReadOnlyList<Chapter3JobEffect> Effects);

/// <summary>
/// Decides when the family offers work and tracks what the player does with each
/// offer. Pure transitions: each takes a board and returns the new board plus the
/// effects it produced. Stale and repeated calls (an unknown id, an answered
/// offer, a tick for a processed day) return the board unchanged with no effects.
/// </summary>
public static class Chapter3JobScheduler
{
    public static Chapter3JobTransition OnDayTick(Chapter3JobBoard board, string playerId, int chapterDay)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("Chapter 3 player id was empty.", nameof(playerId));

        if (chapterDay <= board.LastProcessedDay)
            return new Chapter3JobTransition(board, Array.Empty<Chapter3JobEffect>());

        var next = board with { LastProcessedDay = chapterDay };
        var effects = new List<Chapter3JobEffect>();

        if (next.OpenOffer is not null && chapterDay > next.OpenOffer.RespondByDay)
        {
            var expired = next.OpenOffer;
            next = next with { OpenOffer = null, LastExpiredOfferId = expired.OfferId };
            AddRefusal(ref next, chapterDay, effects);
            effects.Insert(0, new Chapter3OfferExpiredEffect(expired, next.RefusalStreak));
        }

        // A consumed-but-unpaid delivery is mid-transaction, not late: failing it
        // would take the player's bricks and open a make-good on top, so it
        // keeps retrying its credit past the deadline instead. The handoff
        // removal of a package recovery counts the same way.
        if (next.ActiveJob is not null && next.ActiveJob.Delivery?.RemovalConfirmed != true &&
            next.ActiveJob.Recovery?.RemovalConfirmed != true &&
            next.ActiveJob.StorageRun?.Reconciling != true &&
            next.ActiveJob.CashMovement?.Reconciling != true &&
            next.ActiveJob.Shutdown?.Reconciling != true &&
            chapterDay > next.ActiveJob.DeadlineDay)
        {
            var job = next.ActiveJob;
            next = next with
            {
                ActiveJob = null,
                CooldownUntilDay = chapterDay + Chapter3JobTuning.CooldownDays
            };
            effects.Add(new Chapter3JobCompletedEffect(job.JobId, job.JobType, false));
            effects.Add(new Chapter3MakeGoodRequiredEffect(job.JobId, job.JobType, job.Reward));
        }

        if (next.OpenOffer is null && next.ActiveJob is null &&
            (next.CooldownUntilDay is null || chapterDay > next.CooldownUntilDay.Value))
        {
            var offer = new Chapter3JobOffer(
                next.NextOfferId,
                SelectType(playerId, next),
                SelectReward(playerId, next.NextOfferId),
                chapterDay,
                chapterDay + Chapter3JobTuning.OfferResponseDays);
            next = next with
            {
                OpenOffer = offer,
                LastTypeOffered = offer.JobType,
                TypesSeen = next.TypesSeen.Contains(offer.JobType)
                    ? next.TypesSeen
                    : next.TypesSeen.Append(offer.JobType).ToArray(),
                NextOfferId = next.NextOfferId + 1,
                LastEmergencySupplyOfferId = offer.JobType == Chapter3JobType.EmergencySupply
                    ? offer.OfferId
                    : next.LastEmergencySupplyOfferId
            };
            effects.Add(new Chapter3OfferCreatedEffect(offer));
        }

        return new Chapter3JobTransition(next, effects);
    }

    public static Chapter3JobTransition Accept(Chapter3JobBoard board, int offerId, int chapterDay)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));

        var offer = board.OpenOffer;
        if (offer is null || offer.OfferId != offerId || board.ActiveJob is not null || chapterDay > offer.RespondByDay)
            return new Chapter3JobTransition(board, Array.Empty<Chapter3JobEffect>());

        var job = new Chapter3ActiveJob(
            board.NextJobId, offer.OfferId, offer.JobType, offer.Reward,
            chapterDay, chapterDay + (offer.JobType == Chapter3JobType.TemporaryStorageSafehouse ? 3 : Chapter3JobTuning.CompletionDeadlineDays), false)
        { StorageRun = offer.StorageRun, CashMovement = offer.CashMovement, Shutdown = offer.Shutdown };
        var next = board with
        {
            OpenOffer = null,
            ActiveJob = job,
            NextJobId = board.NextJobId + 1
        };
        return new Chapter3JobTransition(next, Array.Empty<Chapter3JobEffect>());
    }

    public static Chapter3JobTransition Refuse(Chapter3JobBoard board, int offerId, int chapterDay)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));

        var offer = board.OpenOffer;
        if (offer is null || offer.OfferId != offerId || chapterDay > offer.RespondByDay)
            return new Chapter3JobTransition(board, Array.Empty<Chapter3JobEffect>());

        var next = board with { OpenOffer = null, LastDeclinedOfferId = offerId };
        var effects = new List<Chapter3JobEffect>();
        AddRefusal(ref next, chapterDay, effects);
        return new Chapter3JobTransition(next, effects);
    }

    public static Chapter3JobTransition Complete(Chapter3JobBoard board, int jobId, bool succeeded, int chapterDay)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));

        var job = board.ActiveJob;
        if (job is null || job.JobId != jobId)
            return new Chapter3JobTransition(board, Array.Empty<Chapter3JobEffect>());

        var effects = new List<Chapter3JobEffect> { new Chapter3JobCompletedEffect(job.JobId, job.JobType, succeeded) };
        var next = board with
        {
            ActiveJob = null,
            CooldownUntilDay = chapterDay + Chapter3JobTuning.CooldownDays
        };
        if (succeeded)
        {
            next = next with
            {
                RefusalStreak = 0,
                NellHintIssued = false,
                CooperationVisitIssued = false,
                SuccessfulJobs = next.SuccessfulJobs + 1,
                LastCompletedJob = new Chapter3JobCompletion(job.JobId, job.JobType, job.Reward) { CashMovement = job.CashMovement, Shutdown = job.Shutdown }
            };
        }
        else
        {
            effects.Add(new Chapter3MakeGoodRequiredEffect(job.JobId, job.JobType, job.Reward));
        }
        return new Chapter3JobTransition(next, effects);
    }

    /// <summary>
    /// The paid deadline extension, model only: once per job, requested on or
    /// before the deadline day, unlocked after the third success. Returns the
    /// cost without charging anything.
    /// </summary>
    public static int ExtensionCost(int reward) => reward * Chapter3JobTuning.ExtensionCostPercent / 100;

    public static bool CanExtendDeadline(Chapter3JobBoard board, int chapterDay)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));
        var job = board.ActiveJob;
        return job is not null && job.JobType != Chapter3JobType.EmergencySupply && !job.ExtensionUsed &&
            board.SuccessfulJobs >= Chapter3JobTuning.ExtensionUnlockSuccesses &&
            chapterDay <= job.DeadlineDay;
    }

    public static Chapter3DeadlineExtensionResult TryExtendDeadline(Chapter3JobBoard board, int jobId, int chapterDay)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));

        var job = board.ActiveJob;
        if (job is null || job.JobId != jobId || job.JobType == Chapter3JobType.EmergencySupply || job.ExtensionUsed ||
            board.SuccessfulJobs < Chapter3JobTuning.ExtensionUnlockSuccesses ||
            chapterDay > job.DeadlineDay)
            return new Chapter3DeadlineExtensionResult(board, false, 0, Array.Empty<Chapter3JobEffect>());

        var cost = ExtensionCost(job.Reward);
        var extended = job with
        {
            DeadlineDay = job.DeadlineDay + Chapter3JobTuning.ExtensionDays,
            ExtensionUsed = true
        };
        var next = board with { ActiveJob = extended };
        var effect = new Chapter3DeadlineExtendedEffect(job.JobId, extended.DeadlineDay, cost);
        return new Chapter3DeadlineExtensionResult(next, true, cost, new[] { effect });
    }

    /// <summary>
    /// Warehouse-offer eligibility: enough days, enough successes, no open
    /// make-good. The flag is a parameter until OC-115 provides it.
    /// </summary>
    public static bool IsWarehouseOfferEligible(int chapterDay, int successfulJobs, bool hasOpenMakeGood) =>
        chapterDay >= Chapter3JobTuning.WarehouseEligibilityDay &&
        successfulJobs >= Chapter3JobTuning.WarehouseEligibilitySuccesses &&
        !hasOpenMakeGood;

    private static void AddRefusal(ref Chapter3JobBoard next, int chapterDay, List<Chapter3JobEffect> effects)
    {
        var streak = next.RefusalStreak + 1;
        next = next with
        {
            RefusalStreak = streak,
            CooldownUntilDay = chapterDay + Chapter3JobTuning.CooldownDays
        };
        if (streak >= Chapter3JobTuning.NellHintStreak && !next.NellHintIssued)
        {
            next = next with { NellHintIssued = true };
            effects.Add(new Chapter3NellHintEffect(streak));
        }
        if (streak >= Chapter3JobTuning.CooperationVisitStreak && !next.CooperationVisitIssued)
        {
            next = next with { CooperationVisitIssued = true };
            effects.Add(new Chapter3ArthurCooperationVisitEffect(streak));
        }
    }

    /// <summary>
    /// Deterministic, reload-stable selection inside the runnable set: the
    /// first offer is the first runnable type; otherwise Emergency Supply may
    /// roll when its spacing allows, and anything else draws from the paid
    /// pool exactly as before (every paid type except the last one offered,
    /// narrowed to unseen types while any remain). Every pick is a stable
    /// hash of the player id plus the offers made so far, so it varies per
    /// player and survives reloads.
    /// </summary>
    private static Chapter3JobType SelectType(string playerId, Chapter3JobBoard board)
    {
        var runnable = Chapter3JobTuning.RunnableJobTypes;
        if (runnable is null || runnable.Length == 0)
            throw new InvalidOperationException("Chapter 3 has no runnable job types.");
        if (board.NextOfferId == 1)
            return runnable[0];

        if (IsEmergencySupplyEligible(board) &&
            StableHash($"emergency|{playerId}|{board.NextOfferId}") % (uint)Chapter3JobTuning.EmergencySupplyChanceDenominator == 0)
            return Chapter3JobType.EmergencySupply;
        return SelectPaidType(playerId, board);
    }

    /// <summary>
    /// Emergency Supply spacing: at least 3 offers go by since the last
    /// Emergency Supply offer (or since the start, so never in the first 3
    /// offers). The gap also makes twice-in-a-row impossible.
    /// </summary>
    private static bool IsEmergencySupplyEligible(Chapter3JobBoard board) =>
        (board.LastEmergencySupplyOfferId ?? 0) + Chapter3JobTuning.EmergencySupplyMinOfferIdGap <= board.NextOfferId;

    private static Chapter3JobType SelectPaidType(string playerId, Chapter3JobBoard board)
    {
        var paid = Chapter3JobTuning.PaidJobTypes;
        if (paid is null || paid.Length == 0)
            throw new InvalidOperationException("Chapter 3 has no paid job types.");
        var pool = paid
            .Where(type => type != board.LastTypeOffered)
            .ToArray();
        if (pool.Length == 0)
            pool = paid.ToArray();
        var unseen = pool.Where(type => !board.TypesSeen.Contains(type)).ToArray();
        var candidates = unseen.Length > 0 ? unseen : pool;
        var index = (int)((StableHash("type|" + playerId) + (uint)(board.NextOfferId - 1)) % (uint)candidates.Length);
        return candidates[index];
    }

    /// <summary>
    /// The named emergency product: a deterministic pick among the discovered
    /// candidates, ordered by id so world enumeration order cannot matter. It
    /// is not always the best; the player reads it in the offer text. Null
    /// when nothing discovered can be named.
    /// </summary>
    public static Release1SmallCourtesyProductCandidate? SelectEmergencyProduct(
        IReadOnlyList<Release1SmallCourtesyProductCandidate>? candidates, string playerId, int offerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("Chapter 3 player id was empty.", nameof(playerId));
        if (offerId <= 0)
            throw new ArgumentException("Chapter 3 offer id was invalid.", nameof(offerId));
        var discovered = (candidates ?? Array.Empty<Release1SmallCourtesyProductCandidate>())
            .Where(candidate => candidate is not null && candidate.IsDiscovered &&
                !string.IsNullOrWhiteSpace(candidate.ProductId) &&
                !string.IsNullOrWhiteSpace(candidate.ProductName))
            .OrderBy(candidate => candidate.ProductId, StringComparer.Ordinal)
            .ToArray();
        if (discovered.Length == 0)
            return null;
        var index = (int)(StableHash($"emergency-product|{playerId}|{offerId}") % (uint)discovered.Length);
        return discovered[index];
    }

    /// <summary>
    /// Rewrites a just-created Emergency Supply offer to a paid type when no
    /// product could be frozen for it. The board surgery mirrors what the tick
    /// would have recorded had the paid type been picked outright: the paid
    /// pick excludes the pre-tick last type so visible offers still alternate,
    /// the emergency type leaves the seen set only when this tick added it,
    /// and the emergency counter returns to its pre-tick value.
    /// </summary>
    public static Chapter3JobBoard FallbackEmergencyOffer(
        Chapter3JobBoard board, Chapter3JobBoard boardBeforeTick, string playerId)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));
        if (boardBeforeTick is null)
            throw new ArgumentNullException(nameof(boardBeforeTick));
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("Chapter 3 player id was empty.", nameof(playerId));
        var offer = board.OpenOffer;
        if (offer is null || offer.JobType != Chapter3JobType.EmergencySupply)
            throw new ArgumentException("Chapter 3 fallback needs a fresh Emergency Supply offer.", nameof(board));
        var paid = SelectPaidType(playerId, board with { LastTypeOffered = boardBeforeTick.LastTypeOffered });
        var seen = board.TypesSeen.Contains(Chapter3JobType.EmergencySupply) &&
            !boardBeforeTick.TypesSeen.Contains(Chapter3JobType.EmergencySupply)
            ? board.TypesSeen.Where(type => type != Chapter3JobType.EmergencySupply).ToArray()
            : board.TypesSeen.ToArray();
        if (!seen.Contains(paid))
            seen = seen.Append(paid).ToArray();
        return board with
        {
            OpenOffer = offer with { JobType = paid, ProductId = null, ProductName = null },
            LastTypeOffered = paid,
            TypesSeen = seen,
            LastEmergencySupplyOfferId = boardBeforeTick.LastEmergencySupplyOfferId
        };
    }

    /// <summary>
    /// Rewrites a fresh offer to Emergency Supply for the owner-only QA force
    /// command. The runtime freezes the product right after; the counters move
    /// as if the scheduler had picked it.
    /// </summary>
    public static Chapter3JobBoard ForceEmergencyOffer(Chapter3JobBoard board)
    {
        if (board is null)
            throw new ArgumentNullException(nameof(board));
        var offer = board.OpenOffer;
        if (offer is null)
            throw new ArgumentException("Chapter 3 force needs an open offer.", nameof(board));
        return board with
        {
            OpenOffer = offer with { JobType = Chapter3JobType.EmergencySupply, Shutdown = null },
            LastTypeOffered = Chapter3JobType.EmergencySupply,
            TypesSeen = board.TypesSeen.Contains(Chapter3JobType.EmergencySupply)
                ? board.TypesSeen
                : board.TypesSeen.Append(Chapter3JobType.EmergencySupply).ToArray(),
            LastEmergencySupplyOfferId = offer.OfferId
        };
    }

    private static int SelectReward(string playerId, int offerId)
    {
        var steps = (Chapter3JobTuning.MaxReward - Chapter3JobTuning.MinReward) / Chapter3JobTuning.RewardStep + 1;
        var roll = (int)(StableHash("reward|" + playerId + "|" + offerId) % (uint)steps);
        return Chapter3JobTuning.MinReward + roll * Chapter3JobTuning.RewardStep;
    }

    /// <summary>
    /// FNV-1a over UTF-8 bytes. Used instead of string.GetHashCode, which is
    /// randomized per process and would break reload stability.
    /// </summary>
    private static uint StableHash(string text)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }
}
