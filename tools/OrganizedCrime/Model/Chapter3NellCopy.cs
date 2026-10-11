namespace OrganizedCrime.Model;

/// <summary>
/// Every word Nell says in Chapter 3, in one place.
/// Amounts and time spans are described; internal day numbers never appear.
/// </summary>
public static class Chapter3NellCopy
{
    public static string JobName(Chapter3JobType jobType) => jobType switch
    {
        Chapter3JobType.DeadDrop => "a dead drop delivery",
        Chapter3JobType.PackageRecovery => "a package recovery",
        Chapter3JobType.TemporaryStorageSafehouse => "a safehouse storage run",
        Chapter3JobType.EmergencySupply => "an emergency supply run",
        Chapter3JobType.OperationalShutdown => "a shutdown operation",
        Chapter3JobType.CashMovement => "a cash movement",
        _ => "some work"
    };

    public static string OfferText(Chapter3JobType jobType, int reward, int activeDays) =>
        $"Got work if you want it: {JobName(jobType)}. Pays ${reward:N0}. " +
        $"You get {activeDays} days once you say yes. Tell me by tomorrow, don't make me fucking chase you.";

    /// <summary>
    /// The unpaid emergency offer: it names the frozen product and brick count
    /// so the player can decide whether they can make it. The product fallback
    /// is deliberately not "your best product": this one is named, not best.
    /// </summary>
    public static string EmergencySupplyOfferText(string? productName, int brickCount)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "the product" : productName;
        return $"The family needs {brickCount} bricks of {product}, and we don't make that shit ourselves. " +
            "No pay on this one. It comes back to you, trust me. You get 12 hours once you say yes.";
    }

    internal static string StorageOfferText(Chapter3StorageRun run, int reward) =>
        $"Got a storage run. {run.Route.BrickCount} bricks through the HQ room, held for {run.HoldMinutes / 60:0} hours, then another drop. ${reward:N0}. Three days once you say yes.";
    internal static string StorageAcceptedText(Chapter3StorageRun run) =>
        $"They're at {run.Route.Pickup.Name}. Player closet at HQ, whole stack. Keep it out of the family locker. Don't move it until I fucking call.";
    internal static string StorageReleasedText(Chapter3StorageRun run) =>
        $"Room's done. Bring the whole stack to {run.Route.Handoff.Name}.";

    public const string AcceptedText = "Good. I'll send the details.";

    /// <summary>
    /// A frozen dead-drop accept: bricks, product and drop are the frozen
    /// terms. Blank names fall back to generics so the line never reads
    /// half-empty.
    /// </summary>
    public static string DeadDropAcceptedText(Chapter3DeadDropDelivery delivery, int reward)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));
        var product = string.IsNullOrWhiteSpace(delivery.ProductName) ? "your best product" : delivery.ProductName;
        var drop = string.IsNullOrWhiteSpace(delivery.DropName) ? "the drop" : delivery.DropName;
        return $"Good. {delivery.BrickCount} bricks of {product} at {drop}. ${reward:N0} when they land. Don't fuck it up.";
    }

    /// <summary>
    /// A frozen package-recovery accept: pickup, handoff, bricks and product
    /// are the frozen terms, with the same blank-name fallbacks as the
    /// dead-drop line.
    /// </summary>
    public static string PackageRecoveryAcceptedText(Chapter3PackageRecovery recovery, int reward)
    {
        if (recovery is null)
            throw new ArgumentNullException(nameof(recovery));
        var pickup = string.IsNullOrWhiteSpace(recovery.Pickup.Name) ? "the drop" : recovery.Pickup.Name;
        var handoff = string.IsNullOrWhiteSpace(recovery.Handoff.Name) ? "the drop" : recovery.Handoff.Name;
        var product = string.IsNullOrWhiteSpace(recovery.ProductName) ? "your best product" : recovery.ProductName;
        return $"Some idiot left a package at {pickup}: {recovery.BrickCount} bricks of {product}. " +
            $"Get it to {handoff}. ${reward:N0} when it lands.";
    }

    /// <summary>
    /// A frozen emergency-supply accept: bricks, named product and drop are
    /// the frozen terms. No reward is mentioned: there is no pay.
    /// </summary>
    public static string EmergencySupplyAcceptedText(Chapter3DeadDropDelivery delivery)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));
        var product = string.IsNullOrWhiteSpace(delivery.ProductName) ? "the product" : delivery.ProductName;
        var drop = string.IsNullOrWhiteSpace(delivery.DropName) ? "the drop" : delivery.DropName;
        return $"{delivery.BrickCount} bricks of {product} at {drop}. Twelve hours. Move your ass.";
    }

    public static string CustodyText(Chapter3PackageRecovery recovery)
    {
        if (recovery is null)
            throw new ArgumentNullException(nameof(recovery));
        var handoff = string.IsNullOrWhiteSpace(recovery.Handoff.Name) ? "the drop" : recovery.Handoff.Name;
        return $"You've got it. Now get it to {handoff}, and don't get fucking cute.";
    }

    internal static string ShutdownOfferText(Chapter3Shutdown run, int reward) =>
        $"Need {run.PropertyName} quiet for twelve hours. Stop the staff and finish the batches first. ${reward:N0}, two days. Your other places can keep working.";
    internal const string ShutdownAcceptedText = "Good. Nobody works there until I call. Don't trash product, just stop the fucking operation.";
    internal static string ShutdownCompletedText(int reward) =>
        $"Quiet window's done. ${reward:N0}'s yours. Get back to work.";
    internal const string ShutdownMonitoringText = "For this one I'm watching staff and the running stations. Manual pressing, packaging and laundering aren't detected. Brief work can slip past the checks.";

    internal static string CashOfferText(Chapter3CashMovement run, int reward) =>
        $"Cash movement. Front ${run.Advance:N0} into the family locker at HQ. You get it back plus ${reward:N0}. Two days. Physical cash, don't fucking text me a balance.";
    internal const string CashAcceptedText = "Whole amount in the family locker, a thousand to a stack. Don't spread it around.";
    internal static string CashCompletedText(Chapter3CashMovement run, int reward) =>
        $"Received. Your ${run.Advance:N0} is back, and ${reward:N0}'s yours.";

    public static string JobCompletedText(Chapter3JobType jobType, int reward) => jobType switch
    {
        Chapter3JobType.TemporaryStorageSafehouse => $"It landed. ${reward:N0}'s yours. Room did its job.",
        Chapter3JobType.PackageRecovery => $"It landed. ${reward:N0}'s yours.",
        Chapter3JobType.EmergencySupply => "Got it. The family won't forget that.",
        _ => $"They landed. ${reward:N0}'s yours. Not bad."
    };

    public const string OfferRefusedText = "You've got nothing I can move right now.";
    public const string DeclinedText = "Your fucking call.";
    public const string OfferExpiredText = "No answer? I'll take that as a fuck no.";
    public const string NellHintText = "You keep passing. People are starting to fucking notice.";
    public const string DeadlineCloseText = "Clock's running on that job. Move your ass.";
    public const string ExtensionBoughtText = "Fine. One more day. Don't make a fucking habit of it.";
    public const string Trust3Text = "Look at you. The family's starting to know your name.";
    public const string Trust5Text = "The family's noticed. Don't let it go to your fucking head.";
    public const string MakeGoodOverdueText = "You're late. Arthur's been told, and he is not a patient man.";
    public const string PaidText = "Received.";
    public const string ArthurWarningFollowupText = "Arthur came by. Pay the family, and do it fast.";
    public const string ArthurRanOffText = "Arthur says you walked off on him. Don't fucking do that again.";
    public const string ArthurBeatingNoDebtText = "You swung at Arthur? Don't be fucking stupid.";

    public static string ArthurBeatingDebtsText(int amountOwed) =>
        $"Arthur sorted you out. You still owe the family ${amountOwed:N0}, so fucking pay it.";
    public const string ShortText = "You're short. Don't waste my fucking time.";
    public const string WarehouseOfferText = "You've been pulling your weight. There's a warehouse on the docks, right next to the Docks Warehouse. $300,000 and it's fucking yours. No rush, this one stays open, and I'll check in every week.";
    public const string WarehouseReminderText = "That warehouse is still sitting there. $300,000 whenever you're ready.";
    public const string WarehouseBoughtText = "Keys are yours. Place'll be ready in the morning. Welcome to the fucking big leagues. The family takes $30,000 a week, and don't make me chase it.";
    public const string WarehouseBuiltText = "Your place is ready. Go take a look, and don't fuck it up.";
    public const string WarehouseFellThroughText = "Deal fell through. Your money's back. I'll sort it out.";
    public const string WarehouseNotReadyText = "Hold on, the place isn't ready. Try me again in a minute.";

    public static string MakeGoodOpenedText(Chapter3MakeGoodSource source, int amount) =>
        $"{MakeGoodReason(source)}. The family wants ${amount:N0} within 2 days. Don't make them ask twice.";

    /// <summary>
    /// The job noun without an article, for sentences that already have a
    /// subject ("That dead drop delivery went to shit.").
    /// </summary>
    public static string JobNoun(Chapter3JobType jobType) => jobType switch
    {
        Chapter3JobType.DeadDrop => "dead drop delivery",
        Chapter3JobType.PackageRecovery => "package recovery",
        Chapter3JobType.TemporaryStorageSafehouse => "safehouse storage run",
        Chapter3JobType.EmergencySupply => "emergency supply run",
        Chapter3JobType.OperationalShutdown => "shutdown operation",
        Chapter3JobType.CashMovement => "cash movement",
        _ => "work"
    };

    public static string MakeGoodReason(Chapter3MakeGoodSource source) => source switch
    {
        Chapter3FailedJobSource failed => $"That {JobNoun(failed.JobType)} went to shit",
        Chapter3MissedCutSource => "You missed the family's cut",
        Chapter3MissedDemandSource => "The family order went short",
        _ => "You owe the family"
    };

    public static string CutDueText(int amount) =>
        $"Family's cut this week: ${amount:N0}. Pay up.";

    public static string PayLabel(int amount) => $"Pay ${amount:N0}";

    public const string AcceptLabel = "Accept";
    public const string DeclineLabel = "Decline";

    public static string BuyTimeLabel(int cost) => $"Buy more time (${cost:N0})";

    public static string BuyWarehouseLabel(int price) => $"Buy the warehouse (${price:N0})";

    /// <summary>
    /// OC-117: the expansion offer texts and their paid confirmations. The
    /// button label names the expansion (never "Pay $...") so the native
    /// boundary, which keys buttons by label, can never collapse it into a
    /// debt payment.
    /// </summary>
    public const string WarehouseCapacityTier2OfferText = "The family liked that delivery. They'll let you run sixteen people out of the Syndicate Warehouse. $50,000 and it's done. Offer stays open.";
    public const string WarehouseCapacityTier3OfferText = "Three clean orders. The family says you can run twenty out of the warehouse. $100,000. It stays open, don't make me fucking ask twice.";

    public static string WarehouseCapacityPaidText(int capacity) =>
        $"Done. The warehouse takes {capacity} now. Go hire them, and buy them somewhere to sleep.";

    public static string ExpandWarehouseLabel(int price) => $"Expand warehouse (${price:N0})";

    /// <summary>
    /// Short lines for re-showing buttons whose subject Nell already delivered.
    /// A reprompt never repeats a subject; each subject reaches the player once.
    /// </summary>
    public const string PayRepromptText = "Still waiting on that fucking money.";
    public const string AnswerRepromptText = "Still need an answer on that job.";
    public const string BuyTimeRepromptText = "Clock's still running.";
    public const string WarehouseRepromptText = "Warehouse is still yours if you want it.";
    public const string WarehouseCapacityRepromptText = "That warehouse expansion's still on the table.";

    /// <summary>
    /// OC-136: the weekly production demand's texts. The receipt covers
    /// one hand-over burst (OC-137); the last one closes the lot.
    /// </summary>
    public const string FamilyLockerWhere = "the back locker in the row nearest Arthur's desk";

    public static string DemandIssuedText(int quantity, string? productName, int dueDay)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "product" : productName;
        return $"The family wants {quantity} {product} out of the warehouse by day {dueDay}. Drop it in the family locker at the HQ, {FamilyLockerWhere}. Half price, and don't fucking argue.";
    }

    public static string DemandReceiptText(int tookUnits, int leftUnits) =>
        leftUnits <= 0
            ? $"Got {tookUnits}. That's the lot."
            : $"Got {tookUnits}. {leftUnits} to go.";

    public static string DemandCompleteText() =>
        "That's the order. Family's happy, for now.";

    public static string DemandMissedText(int leftUnits, string? productName, int amount)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "product" : productName;
        return $"You came up short on the order. {leftUnits} {product} at full price, that's ${amount:N0}. Two days.";
    }

    public static string DemandDueTomorrowText(int leftUnits, string? productName)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "product" : productName;
        return $"Family order's due tomorrow. {leftUnits} {product} still owed.";
    }

    /// <summary>
    /// The reprompt for a decision, chosen by its first button: a Pay button
    /// wants money, Accept wants an answer, Buy more time wants the clock,
    /// Buy the warehouse wants the keys.
    /// </summary>
    public static string RepromptText(Chapter3PresentationCommand command) => command switch
    {
        null => throw new ArgumentNullException(nameof(command)),
        Chapter3PayMakeGoodCommand => PayRepromptText,
        Chapter3PayCutCommand => PayRepromptText,
        Chapter3AcceptCommand => AnswerRepromptText,
        Chapter3DeclineCommand => AnswerRepromptText,
        Chapter3BuyTimeCommand => BuyTimeRepromptText,
        Chapter3BuyWarehouseCommand => WarehouseRepromptText,
        Chapter3BuyWarehouseCapacityCommand => WarehouseCapacityRepromptText,
        _ => throw new ArgumentException($"Chapter 3 reprompt command was unknown: {command.GetType().Name}.", nameof(command))
    };
}
