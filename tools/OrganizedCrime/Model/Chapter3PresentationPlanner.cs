namespace OrganizedCrime.Model;

/// <summary>
/// Pure projection from Chapter 3 state to Nell's messages plus her one
/// decision; ownership is a live world fact, so the caller passes it in.
/// The projector, not this planner, dedupes against receipts for
/// sending, but the planner already hides receipted texts from the message
/// list so QA status and tests show what is actually pending. The decision
/// prompt is the most relevant subject text whether or not it is receipted, so
/// the prompt is stable; the projector delivers it once (as the prompt or as a
/// message) and re-shows the buttons with the reprompt line after that.
/// Follows the Chief's prompt rule: a subject text shown as the decision
/// prompt is not also queued as a message.
/// </summary>
public static class Chapter3PresentationPlanner
{
    public const int MaxButtons = 4;

    public static Chapter3PresentationPlan Build(Chapter3State state, bool release1HasNellDecision, bool warehouseOwned, Release1ChiefProtectionSnapshot? protection = null, bool demandReceiptReady = true)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));
        if (state.Phase != Chapter3Phase.Active)
            return Chapter3PresentationPlan.Empty;
        var chapterDay = state.ChapterDayAt(state.LastProcessedElapsedDay);
        if (!chapterDay.HasValue)
            return Chapter3PresentationPlan.Empty;

        var receipts = new HashSet<string>(state.PresentationReceipts.Select(r => r.CorrelationId));
        var options = release1HasNellDecision
            ? new List<Chapter3DecisionOption>()
            : DecisionOptions(state, chapterDay.Value);
        var promptScope = options.Count == 0 ? null : PickPromptScope(state, chapterDay.Value);

        var messages = new List<Chapter3DesiredMessage>();
        if (state.Raid is { Completed: true, CoolingApplied: true, ActualUnits: > 0 } raid &&
            raid.NoticeReceipt is null && !receipts.Contains(raid.NoticeCorrelation(state.PlayerId)))
            messages.Add(new(raid.NoticeCorrelation(state.PlayerId), raid.NoticeText));
        foreach (var scope in PendingScopes(state, chapterDay.Value, warehouseOwned, demandReceiptReady))
        {
            if (scope == promptScope || receipts.Contains(Chapter3PresentationReceipt.Correlation(state.PlayerId, scope)))
                continue;
            messages.Add(new Chapter3DesiredMessage(
                Chapter3PresentationReceipt.Correlation(state.PlayerId, scope), TextFor(state, scope)));
        }

        var quest = DesiredQuest(state.JobBoard.ActiveJob, state.ActiveDemand, CapacityOfferTier(state), DesiredProtectionEntries(state, protection));

        if (options.Count == 0)
            return new Chapter3PresentationPlan(messages, null, quest);

        var reprompt = Chapter3NellCopy.RepromptText(options[0].Command);
        var prompt = promptScope is null ? reprompt : TextFor(state, promptScope);
        var promptCorrelationId = promptScope is null
            ? null
            : Chapter3PresentationReceipt.Correlation(state.PlayerId, promptScope);
        var id = "ch3-nell:" + string.Join("+", options.Select(option => KeyFor(state.JobBoard, option)));
        return new Chapter3PresentationPlan(messages, new Chapter3DesiredDecision(id, prompt, options, promptCorrelationId, reprompt), quest);
    }

    /// <summary>
    /// The one Chapter 3 quest, desired only while an active job carries a
    /// frozen delivery or recovery with finite drop positions. Anything less
    /// (no job, an unfrozen legacy job, a non-finite position) desires no
    /// quest at all, so the journal never gets a zero or missing marker. A
    /// recovery desires two entries; the pickup entry completes at custody.
    /// </summary>
    /// <summary>
    /// OC-136: the one Chapter 3 quest carries the job entries first,
    /// then the open demand's three entries (title plus the two progress
    /// lines), all on the HQ door marker. Either side alone still
    /// desires its own entries; neither desires anything unmappable.
    /// </summary>
    private static Release1DesiredQuest? DesiredQuest(Chapter3ActiveJob? job, Chapter3ProductionDemand? demand, int? capacityOfferTier, IReadOnlyList<Release1DesiredEntry>? protectionEntries)
    {
        var jobQuest = DesiredJobQuest(job);
        var demandEntries = DesiredDemandEntries(demand);
        var capacityEntries = DesiredCapacityEntries(capacityOfferTier);
        if (jobQuest is null && demandEntries is null && capacityEntries is null && protectionEntries is null)
            return null;
        var entries = (jobQuest?.Entries ?? Array.Empty<Release1DesiredEntry>())
            .Concat(demandEntries ?? Array.Empty<Release1DesiredEntry>())
            .Concat(capacityEntries ?? Array.Empty<Release1DesiredEntry>())
            .Concat(protectionEntries ?? Array.Empty<Release1DesiredEntry>())
            .ToArray();
        return new Release1DesiredQuest(
            Chapter3QuestCopy.QuestKey,
            Chapter3QuestCopy.QuestTitle,
            entries);
    }

    /// <summary>
    /// OC-117: the open expansion offer's journal line, markerless (the
    /// player answers through Nell). Desired alone while the offer stays
    /// open, even with no job or demand running.
    /// </summary>
    private static IReadOnlyList<Release1DesiredEntry>? DesiredCapacityEntries(int? capacityOfferTier)
    {
        if (!capacityOfferTier.HasValue)
            return null;
        return new[]
        {
            new Release1DesiredEntry(
                Chapter3QuestCopy.WarehouseCapacityOfferLine(
                    Chapter3WarehouseCapacityTiers.CapacityForTier(capacityOfferTier.Value),
                    Chapter3PaymentTuning.CapacityPriceForTier(capacityOfferTier.Value)),
                Release1DesiredEntryState.Active,
                Marker: null)
        };
    }

    /// <summary>
    /// OC-118. Campbell's protection line, markerless like the capacity offer (the player answers
    /// through the Chief, not a map pin). Desired while the processed day is strictly below the
    /// cover end day, in the same absolute-day numbering the Chief's own IsProtected uses, with
    /// the end day converted to chapter days like Nell's "by day N". Null snapshot, no end day,
    /// or a lapsed end day desires nothing, so the line disappears on lapse.
    /// </summary>
    private static IReadOnlyList<Release1DesiredEntry>? DesiredProtectionEntries(Chapter3State state, Release1ChiefProtectionSnapshot? protection)
    {
        if (protection?.ProtectedUntilDay is not { } until)
            return null;
        if (state.LastProcessedElapsedDay >= until)
            return null;
        var chapterDay = state.ChapterDayAt(until);
        if (chapterDay is null)
            return null;
        return new[]
        {
            new Release1DesiredEntry(
                Chapter3QuestCopy.ProtectionLine(chapterDay.Value),
                Release1DesiredEntryState.Active,
                Marker: null)
        };
    }

    private static int? CapacityOfferTier(Chapter3State state) =>
        Chapter3WarehouseCapacityTiers.OpenOfferTier(
            state.WarehouseOffer?.Purchased == true,
            state.WarehouseCapacityTier,
            state.DemandsCompleted);

    private static IReadOnlyList<Release1DesiredEntry>? DesiredDemandEntries(Chapter3ProductionDemand? demand)
    {
        if (demand is null || !demand.HqDoorX.HasValue || !demand.HqDoorY.HasValue || !demand.HqDoorZ.HasValue)
            return null;
        var door = new Release1WorldPoint((float)demand.HqDoorX.Value, (float)demand.HqDoorY.Value, (float)demand.HqDoorZ.Value);
        return new[]
        {
            new Release1DesiredEntry(
                Chapter3QuestCopy.DemandEntryTitle(demand.Quantity, demand.ProductName),
                Release1DesiredEntryState.Active,
                door),
            new Release1DesiredEntry(
                Chapter3QuestCopy.DemandMadeLine(demand.FreshCount, demand.Quantity),
                Release1DesiredEntryState.Active,
                door),
            new Release1DesiredEntry(
                Chapter3QuestCopy.DemandHandedLine(demand.Delivered, demand.Quantity),
                Release1DesiredEntryState.Active,
                door)
        };
    }

    private static Release1DesiredQuest? DesiredJobQuest(Chapter3ActiveJob? job)
    {
        if (job?.Shutdown is { } shutdown)
            return new Release1DesiredQuest(Chapter3QuestCopy.QuestKey, Chapter3QuestCopy.QuestTitle, new[]
            {
                new Release1DesiredEntry($"Stop staff and finish monitored batches at {shutdown.PropertyName}",
                    shutdown.QuietSince.HasValue ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active, Marker: null)
                    { Scope = $"chapter3.shutdown:{job.JobId}:stop" },
                new Release1DesiredEntry($"Keep {shutdown.PropertyName} quiet for twelve hours",
                    shutdown.QuietComplete ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active, Marker: null)
                    { Scope = $"chapter3.shutdown:{job.JobId}:quiet" },
                new Release1DesiredEntry($"Receive ${job.Reward:N0}",
                    shutdown.Paid ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active, Marker: null)
                    { Scope = $"chapter3.shutdown:{job.JobId}:payment" }
            });
        if (job?.CashMovement is { } cash)
            return new Release1DesiredQuest(Chapter3QuestCopy.QuestKey, Chapter3QuestCopy.QuestTitle, new[]
            {
                new Release1DesiredEntry($"Front ${cash.Advance:N0} as {cash.Stacks} full $1,000 stacks in the HQ family locker",
                    cash.ConfirmedSlots == cash.Stacks ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active, Marker: null) { Scope = Chapter3QuestCopy.CashDepositScope(job.JobId) },
                new Release1DesiredEntry($"Receive ${cash.Advance:N0} reimbursement plus ${job.Reward:N0}",
                    cash.Paid ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active, Marker: null) { Scope = Chapter3QuestCopy.CashPaymentScope(job.JobId) }
            });
        if (job?.StorageRun is { } storage)
        {
            var route = storage.Route;
            return new Release1DesiredQuest(Chapter3QuestCopy.QuestKey, Chapter3QuestCopy.QuestTitle, new[]
            {
                new Release1DesiredEntry(Chapter3QuestCopy.RecoveryPickupEntryText(route.Pickup.Name),
                    route.Custody ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active,
                    new Release1WorldPoint((float)route.Pickup.X, (float)route.Pickup.Y, (float)route.Pickup.Z)),
                new Release1DesiredEntry($"Hold the whole {route.BrickCount}-brick stack in a player closet at HQ for {storage.HoldMinutes / 60:0} hours (keep it out of the family locker)",
                    storage.Released ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active, Marker: null),
                new Release1DesiredEntry(Chapter3QuestCopy.RecoveryHandoffEntryText(route.Handoff.Name),
                    Release1DesiredEntryState.Active,
                    new Release1WorldPoint((float)route.Handoff.X, (float)route.Handoff.Y, (float)route.Handoff.Z))
            });
        }
        if (job?.Recovery is not null)
            return DesiredRecoveryQuest(job.Recovery);
        var delivery = job?.Delivery;
        if (!Chapter3QuestCopy.HasMappablePosition(delivery))
            return null;
        return new Release1DesiredQuest(
            Chapter3QuestCopy.QuestKey,
            Chapter3QuestCopy.QuestTitle,
            new[]
            {
                new Release1DesiredEntry(
                    Chapter3QuestCopy.DeadDropEntryText(delivery.BrickCount, delivery.ProductName, delivery.DropName),
                    Release1DesiredEntryState.Active,
                    new Release1WorldPoint((float)delivery.DropX, (float)delivery.DropY, (float)delivery.DropZ))
            });
    }

    private static Release1DesiredQuest? DesiredRecoveryQuest(Chapter3PackageRecovery recovery)
    {
        if (!Chapter3QuestCopy.HasMappablePosition(recovery))
            return null;
        return new Release1DesiredQuest(
            Chapter3QuestCopy.QuestKey,
            Chapter3QuestCopy.QuestTitle,
            new[]
            {
                new Release1DesiredEntry(
                    Chapter3QuestCopy.RecoveryPickupEntryText(recovery.Pickup.Name),
                    recovery.Custody ? Release1DesiredEntryState.Complete : Release1DesiredEntryState.Active,
                    new Release1WorldPoint((float)recovery.Pickup.X, (float)recovery.Pickup.Y, (float)recovery.Pickup.Z)),
                new Release1DesiredEntry(
                    Chapter3QuestCopy.RecoveryHandoffEntryText(recovery.Handoff.Name),
                    Release1DesiredEntryState.Active,
                    new Release1WorldPoint((float)recovery.Handoff.X, (float)recovery.Handoff.Y, (float)recovery.Handoff.Z))
            });
    }

    private static List<Chapter3DecisionOption> DecisionOptions(Chapter3State state, int chapterDay)
    {
        var options = new List<Chapter3DecisionOption>();
        var makeGood = MostUrgentMakeGood(state);
        if (makeGood is not null)
            options.Add(new Chapter3DecisionOption(
                Chapter3NellCopy.PayLabel(makeGood.Amount), new Chapter3PayMakeGoodCommand(makeGood.Id)));
        var cut = OldestDueCut(state);
        if (cut is not null)
            options.Add(new Chapter3DecisionOption(
                Chapter3NellCopy.PayLabel(cut.Amount), new Chapter3PayCutCommand(cut.WeekIndex)));
        var offer = state.JobBoard.OpenOffer;
        if (offer is not null)
        {
            options.Add(new Chapter3DecisionOption(Chapter3NellCopy.AcceptLabel, new Chapter3AcceptCommand()));
            options.Add(new Chapter3DecisionOption(Chapter3NellCopy.DeclineLabel, new Chapter3DeclineCommand()));
        }
        if (IsWarehouseButtonShown(state))
            options.Add(new Chapter3DecisionOption(
                Chapter3NellCopy.BuyWarehouseLabel(Chapter3PaymentTuning.WarehousePrice),
                new Chapter3BuyWarehouseCommand()));
        var capacityOffer = CapacityOfferTier(state);
        if (capacityOffer.HasValue)
            options.Add(new Chapter3DecisionOption(
                Chapter3NellCopy.ExpandWarehouseLabel(Chapter3PaymentTuning.CapacityPriceForTier(capacityOffer.Value)),
                new Chapter3BuyWarehouseCapacityCommand()));
        var job = state.JobBoard.ActiveJob;
        if (job is not null && Chapter3JobScheduler.CanExtendDeadline(state.JobBoard, chapterDay))
            options.Add(new Chapter3DecisionOption(
                Chapter3NellCopy.BuyTimeLabel(Chapter3JobScheduler.ExtensionCost(job.Reward)),
                new Chapter3BuyTimeCommand()));

        // The native boundary keys buttons by label, so two identically-worded
        // buttons would collapse into one with the wrong command. First wins.
        var seen = new HashSet<string>();
        return options
            .Where(option => seen.Add(option.Label))
            .Take(MaxButtons)
            .ToList();
    }

    private static string? PickPromptScope(Chapter3State state, int chapterDay)
    {
        var offer = state.JobBoard.OpenOffer;
        if (offer is not null)
            return $"offer-{offer.OfferId}";
        if (WarehousePromptScope(state) is string warehouseScope)
            return warehouseScope;
        var capacityOffer = CapacityOfferTier(state);
        if (capacityOffer.HasValue)
            return $"capacity-{capacityOffer.Value}-offer";
        var makeGood = MostUrgentMakeGood(state);
        if (makeGood is not null)
            return makeGood.Status == Chapter3MakeGoodStatus.Overdue
                ? $"mg-{makeGood.Id}-overdue"
                : $"mg-{makeGood.Id}-opened";
        var cut = OldestDueCut(state);
        if (cut is not null)
            return $"cut-{cut.WeekIndex}-due";
        var job = state.JobBoard.ActiveJob;
        if (job is not null && job.DeadlineDay - chapterDay is >= 0 and <= 1)
            return $"job-{job.JobId}-deadline-close";
        return null;
    }

    private static IEnumerable<string> PendingScopes(Chapter3State state, int chapterDay, bool warehouseOwned, bool demandReceiptReady)
    {
        for (var shortfall = 1; shortfall <= state.PaymentShortfallCount; shortfall++)
            yield return $"short-{shortfall}";
        var board = state.JobBoard;
        if (board.OpenOffer?.Shutdown is not null || board.ActiveJob?.Shutdown is not null)
            yield return $"offer-{board.OpenOffer?.OfferId ?? board.ActiveJob!.OfferId}-shutdown-monitoring";
        if (board.OpenOffer is not null)
            yield return $"offer-{board.OpenOffer.OfferId}";
        if (board.ActiveJob is not null)
            yield return $"job-{board.ActiveJob.JobId}-accepted";
        if (board.ActiveJob?.Recovery?.Custody == true)
            yield return $"job-{board.ActiveJob.JobId}-custody";
        if (board.LastDeclinedOfferId is not null)
            yield return $"offer-{board.LastDeclinedOfferId}-declined";
        if (board.LastExpiredOfferId is not null)
            yield return $"offer-{board.LastExpiredOfferId}-expired";
        if (board.ActiveJob?.StorageRun?.Released == true)
            yield return $"job-{board.ActiveJob.JobId}-storage-released";
        if (board.LastCompletedJob is not null)
            yield return $"job-{board.LastCompletedJob.JobId}-completed";
        if (board.OpenOffer is not null && board.LastRefusedOfferId == board.OpenOffer.OfferId)
            yield return $"offer-{board.OpenOffer.OfferId}-refused";
        if (board.NellHintIssued)
            yield return "nell-hint";
        if (board.ActiveJob is not null && board.ActiveJob.DeadlineDay - chapterDay is >= 0 and <= 1)
            yield return $"job-{board.ActiveJob.JobId}-deadline-close";
        if (board.ActiveJob is not null && board.ActiveJob.ExtensionUsed)
            yield return $"job-{board.ActiveJob.JobId}-extended";
        if (board.SuccessfulJobs >= 3)
            yield return "trust-3";
        if (board.SuccessfulJobs >= 5)
            yield return "trust-5";
        foreach (var entry in state.Ledger.Entries)
            yield return $"mg-{entry.Id}-opened";
        foreach (var entry in state.Ledger.Entries.Where(e => e.Status == Chapter3MakeGoodStatus.Overdue))
            yield return $"mg-{entry.Id}-overdue";
        foreach (var entry in state.Cuts.Entries)
            yield return $"cut-{entry.WeekIndex}-due";
        foreach (var entry in state.Ledger.Entries.Where(e => e.Status == Chapter3MakeGoodStatus.Paid))
            yield return $"mg-{entry.Id}-paid";
        foreach (var entry in state.Cuts.Entries.Where(e => e.Status == Chapter3WeeklyCutStatus.Paid))
            yield return $"cut-{entry.WeekIndex}-paid";
        var warehouse = state.WarehouseOffer;
        if (warehouse is not null && !warehouse.Purchased)
        {
            yield return "warehouse-offer";
            for (var week = 1; week <= warehouse.LastReminderWeek; week++)
                yield return $"warehouse-reminder-{week}";
            for (var fellThrough = 1; fellThrough <= warehouse.FellThroughCount; fellThrough++)
                yield return $"warehouse-fell-through-{fellThrough}";
            for (var notReady = 1; notReady <= warehouse.NotReadyCount; notReady++)
                yield return $"warehouse-not-ready-{notReady}";
        }
        if (warehouse is not null && warehouse.Purchased)
            yield return "warehouse-bought";
        if (warehouse is not null && warehouse.Purchased && warehouseOwned)
            yield return "warehouse-built";
        for (var followup = 1; followup <= state.ArthurWarningFollowups; followup++)
            yield return $"arthur-warning-followup-{followup}";
        for (var beating = 1; beating <= state.ArthurBeatings; beating++)
            yield return $"arthur-beating-{beating}";
        for (var ranOff = 1; ranOff <= state.ArthurRanOffs; ranOff++)
            yield return $"arthur-ran-off-{ranOff}";
        var demand = state.ActiveDemand;
        if (demand is not null)
        {
            yield return $"demand-{demand.DemandId}-issued";
            if (demandReceiptReady && demand.HandOverCount >= 1)
                yield return $"demand-{demand.DemandId}-receipt-{demand.HandOverCount}";
        }
        var outcome = state.LastDemandOutcome;
        if (outcome is not null)
            yield return outcome.Completed
                ? $"demand-{outcome.DemandId}-completed"
                : $"demand-{outcome.DemandId}-missed";
        var capacityOffer = CapacityOfferTier(state);
        if (capacityOffer.HasValue)
            yield return $"capacity-{capacityOffer.Value}-offer";
        if (state.WarehouseCapacityTier >= 2)
            yield return "capacity-2-paid";
        if (state.WarehouseCapacityTier >= 3)
            yield return "capacity-3-paid";
    }

    /// <summary>
    /// The warehouse button shows while the offer is open and unbought, unless
    /// a make-good is Open or Overdue: the family won't sell to someone who owes.
    /// </summary>
    private static bool IsWarehouseButtonShown(Chapter3State state) =>
        state.WarehouseOffer is not null && !state.WarehouseOffer.Purchased &&
        MostUrgentMakeGood(state) is null;

    /// <summary>
    /// The warehouse prompt is the latest live subject: this week's reminder
    /// once reminders have started, else the offer itself. Only a prompt when
    /// the button shows; a hidden button must not advertise itself.
    /// </summary>
    /// <summary>
    /// OC-135: the beating text. When the ledger shows a delivered
    /// beating, Nell names the total still owed across the unpaid
    /// make-goods; with no beating on record the only way here is
    /// an attack with no overdue debt.
    /// </summary>
    private static string BeatingText(Chapter3State state)
    {
        if (!state.Ledger.Entries.Any(entry => entry.BeatingDeliveredDay.HasValue))
            return Chapter3NellCopy.ArthurBeatingNoDebtText;
        var owed = state.Ledger.Entries
            .Where(entry => entry.Status != Chapter3MakeGoodStatus.Paid)
            .Sum(entry => entry.Amount);
        return Chapter3NellCopy.ArthurBeatingDebtsText(owed);
    }

    private static string? WarehousePromptScope(Chapter3State state)
    {
        if (!IsWarehouseButtonShown(state))
            return null;
        var warehouse = state.WarehouseOffer!;
        return warehouse.LastReminderWeek >= 1
            ? $"warehouse-reminder-{warehouse.LastReminderWeek}"
            : "warehouse-offer";
    }

    private static string TextFor(Chapter3State state, string scope)
    {
        if (scope.StartsWith("short-", StringComparison.Ordinal))
            return Chapter3NellCopy.ShortText;
        if (scope == "warehouse-offer")
            return Chapter3NellCopy.WarehouseOfferText;
        if (scope.StartsWith("warehouse-reminder-", StringComparison.Ordinal))
            return Chapter3NellCopy.WarehouseReminderText;
        if (scope == "warehouse-built")
            return Chapter3NellCopy.WarehouseBuiltText;
        if (scope.StartsWith("warehouse-fell-through-", StringComparison.Ordinal))
            return Chapter3NellCopy.WarehouseFellThroughText;
        if (scope == "warehouse-bought")
            return Chapter3NellCopy.WarehouseBoughtText;
        if (scope.StartsWith("warehouse-not-ready-", StringComparison.Ordinal))
            return Chapter3NellCopy.WarehouseNotReadyText;
        if (scope == "capacity-2-offer")
            return Chapter3NellCopy.WarehouseCapacityTier2OfferText;
        if (scope == "capacity-3-offer")
            return Chapter3NellCopy.WarehouseCapacityTier3OfferText;
        if (scope == "capacity-2-paid")
            return Chapter3NellCopy.WarehouseCapacityPaidText(Chapter3WarehouseCapacityTiers.CapacityForTier(2));
        if (scope == "capacity-3-paid")
            return Chapter3NellCopy.WarehouseCapacityPaidText(Chapter3WarehouseCapacityTiers.CapacityForTier(3));
        if (scope == "nell-hint")
            return Chapter3NellCopy.NellHintText;
        if (scope == "trust-3")
            return Chapter3NellCopy.Trust3Text;
        if (scope == "trust-5")
            return Chapter3NellCopy.Trust5Text;
        if (scope.StartsWith("arthur-warning-followup-", StringComparison.Ordinal))
            return Chapter3NellCopy.ArthurWarningFollowupText;
        if (scope.StartsWith("arthur-beating-", StringComparison.Ordinal))
            return BeatingText(state);
        if (scope.StartsWith("arthur-ran-off-", StringComparison.Ordinal))
            return Chapter3NellCopy.ArthurRanOffText;
        var board = state.JobBoard;
        if ((board.OpenOffer?.Shutdown is not null || board.ActiveJob?.Shutdown is not null) &&
            scope == $"offer-{board.OpenOffer?.OfferId ?? board.ActiveJob!.OfferId}-shutdown-monitoring")
            return Chapter3NellCopy.ShutdownMonitoringText;
        if (board.OpenOffer is not null && scope == $"offer-{board.OpenOffer.OfferId}")
            return board.OpenOffer.Shutdown is { } shutdownOffer
                ? Chapter3NellCopy.ShutdownOfferText(shutdownOffer, board.OpenOffer.Reward)
                : board.OpenOffer.CashMovement is { } cashOffer
                ? Chapter3NellCopy.CashOfferText(cashOffer, board.OpenOffer.Reward)
                : board.OpenOffer.StorageRun is { } storageOffer
                ? Chapter3NellCopy.StorageOfferText(storageOffer, board.OpenOffer.Reward)
                : board.OpenOffer.JobType == Chapter3JobType.EmergencySupply
                ? Chapter3NellCopy.EmergencySupplyOfferText(
                    board.OpenOffer.ProductName, Chapter3JobTuning.BricksForReward(board.OpenOffer.Reward))
                : Chapter3NellCopy.OfferText(
                    board.OpenOffer.JobType, board.OpenOffer.Reward, Chapter3JobTuning.CompletionDeadlineDays);
        if (board.ActiveJob is not null && scope == $"job-{board.ActiveJob.JobId}-accepted")
            return board.ActiveJob.Shutdown is not null
                ? Chapter3NellCopy.ShutdownAcceptedText
                : board.ActiveJob.CashMovement is not null
                ? Chapter3NellCopy.CashAcceptedText
                : board.ActiveJob.StorageRun is { } storageJob
                ? Chapter3NellCopy.StorageAcceptedText(storageJob)
                : board.ActiveJob.JobType == Chapter3JobType.EmergencySupply && board.ActiveJob.Delivery is not null
                ? Chapter3NellCopy.EmergencySupplyAcceptedText(board.ActiveJob.Delivery)
                : board.ActiveJob.Delivery is not null
                    ? Chapter3NellCopy.DeadDropAcceptedText(board.ActiveJob.Delivery, board.ActiveJob.Reward)
                    : board.ActiveJob.Recovery is not null
                        ? Chapter3NellCopy.PackageRecoveryAcceptedText(board.ActiveJob.Recovery, board.ActiveJob.Reward)
                        : Chapter3NellCopy.AcceptedText;
        if (board.LastDeclinedOfferId is not null && scope == $"offer-{board.LastDeclinedOfferId}-declined")
            return Chapter3NellCopy.DeclinedText;
        if (board.LastExpiredOfferId is not null && scope == $"offer-{board.LastExpiredOfferId}-expired")
            return Chapter3NellCopy.OfferExpiredText;
        if (board.LastCompletedJob is not null && scope == $"job-{board.LastCompletedJob.JobId}-completed")
            return board.LastCompletedJob.Shutdown is not null
                ? Chapter3NellCopy.ShutdownCompletedText(board.LastCompletedJob.Reward)
                : board.LastCompletedJob.CashMovement is { } cashCompleted
                ? Chapter3NellCopy.CashCompletedText(cashCompleted, board.LastCompletedJob.Reward)
                : Chapter3NellCopy.JobCompletedText(board.LastCompletedJob.JobType, board.LastCompletedJob.Reward);
        if (board.OpenOffer is not null && board.LastRefusedOfferId == board.OpenOffer.OfferId &&
            scope == $"offer-{board.OpenOffer.OfferId}-refused")
            return Chapter3NellCopy.OfferRefusedText;
        if (board.ActiveJob?.StorageRun is { Released: true } released && scope == $"job-{board.ActiveJob.JobId}-storage-released")
            return Chapter3NellCopy.StorageReleasedText(released);
        if (board.ActiveJob?.Recovery?.Custody == true && scope == $"job-{board.ActiveJob.JobId}-custody")
            return Chapter3NellCopy.CustodyText(board.ActiveJob.Recovery);
        if (board.ActiveJob is not null && scope == $"job-{board.ActiveJob.JobId}-deadline-close")
            return Chapter3NellCopy.DeadlineCloseText;
        if (board.ActiveJob is not null && scope == $"job-{board.ActiveJob.JobId}-extended")
            return Chapter3NellCopy.ExtensionBoughtText;
        foreach (var entry in state.Ledger.Entries)
        {
            if (scope == $"mg-{entry.Id}-opened")
                return Chapter3NellCopy.MakeGoodOpenedText(entry.Source, entry.Amount);
            if (scope == $"mg-{entry.Id}-overdue")
                return Chapter3NellCopy.MakeGoodOverdueText;
            if (scope == $"mg-{entry.Id}-paid")
                return Chapter3NellCopy.PaidText;
        }
        foreach (var entry in state.Cuts.Entries)
        {
            if (scope == $"cut-{entry.WeekIndex}-due")
                return Chapter3NellCopy.CutDueText(entry.Amount);
            if (scope == $"cut-{entry.WeekIndex}-paid")
                return Chapter3NellCopy.PaidText;
        }
        var demand = state.ActiveDemand;
        if (demand is not null && scope == $"demand-{demand.DemandId}-issued")
            return Chapter3NellCopy.DemandIssuedText(demand.Quantity, demand.ProductName, demand.DueDay);
        if (demand is not null && demand.HandOverCount >= 1 && scope == $"demand-{demand.DemandId}-receipt-{demand.HandOverCount}")
            return Chapter3NellCopy.DemandReceiptText(demand.LastTakeUnits, demand.Quantity - demand.Delivered);
        var outcome = state.LastDemandOutcome;
        if (outcome is not null && outcome.Completed && scope == $"demand-{outcome.DemandId}-completed")
            return Chapter3NellCopy.DemandCompleteText();
        if (outcome is not null && !outcome.Completed && scope == $"demand-{outcome.DemandId}-missed")
            return Chapter3NellCopy.DemandMissedText(outcome.LeftUnits, outcome.ProductName, outcome.Amount);
        throw new ArgumentException($"Chapter 3 message scope was unknown: {scope}.", nameof(scope));
    }

    private static Chapter3MakeGood? MostUrgentMakeGood(Chapter3State state) =>
        state.Ledger.Entries
            .Where(entry => entry.Status is Chapter3MakeGoodStatus.Open or Chapter3MakeGoodStatus.Overdue)
            .OrderBy(entry => entry.Status == Chapter3MakeGoodStatus.Overdue ? 0 : 1)
            .ThenBy(entry => entry.DueDay)
            .ThenBy(entry => entry.Id)
            .FirstOrDefault();

    private static Chapter3WeeklyCut? OldestDueCut(Chapter3State state) =>
        state.Cuts.Entries
            .Where(entry => entry.Status == Chapter3WeeklyCutStatus.Due)
            .OrderBy(entry => entry.WeekIndex)
            .FirstOrDefault();

    private static string KeyFor(Chapter3JobBoard board, Chapter3DecisionOption option) => option.Command switch
    {
        Chapter3PayMakeGoodCommand pay => $"mg{pay.MakeGoodId}",
        Chapter3PayCutCommand pay => $"cut{pay.WeekIndex}",
        Chapter3AcceptCommand => $"offer{board.OpenOffer!.OfferId}a",
        Chapter3DeclineCommand => $"offer{board.OpenOffer!.OfferId}d",
        Chapter3BuyTimeCommand => $"buy{board.ActiveJob!.JobId}",
        Chapter3BuyWarehouseCommand => "warehouse",
        Chapter3BuyWarehouseCapacityCommand => "capacity",
        _ => "x"
    };
}
