namespace OrganizedCrime.Model;

/// <summary>
/// The overnight line drafts, in one owner-reviewable place. Copy describes
/// standing state, never "tonight" or "tomorrow": the day tick can land either
/// before or after the line shows.
/// </summary>
public static class Chapter3OvernightCopy
{
    public const string ArthurBeatingDue = "Arthur has stopped asking nicely.";
    public const string ArthurWarningDue = "Arthur has been asking where you are.";
    public const string WarehouseEligible = "Nell has something bigger in mind for you.";
    public const string JobWaiting = "The family's job is still waiting on you.";
    public const string FamilyWondering = "Nell is wondering if you're still with the family.";
}

/// <summary>
/// Picks at most one overnight line for the black sleep screen from standing
/// Chapter 3 state. Pure selection; it never changes state.
/// </summary>
public static class Chapter3OvernightLine
{
    public const float DurationSeconds = 4f;
    public const int Order = 100;

    public static string? Select(Chapter3State state, int chapterDay)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));
        if (state.Phase != Chapter3Phase.Active)
            return null;

        var urgency = state.Ledger.MostUrgentStage;
        if (urgency == Chapter3MakeGoodUrgency.BeatingDue)
            return Chapter3OvernightCopy.ArthurBeatingDue;
        if (urgency == Chapter3MakeGoodUrgency.WarningDue)
            return Chapter3OvernightCopy.ArthurWarningDue;

        if (state.Raid is { Completed: true, ActualUnits: > 0 } raid &&
            state.LastProcessedElapsedDay >= Math.Floor(raid.StartMinutes / 1440d) &&
            state.LastProcessedElapsedDay <= Math.Floor(raid.StartMinutes / 1440d) + 1d)
            return $"Police have hit {raid.SiteName}. Nell has the details.";

        if (state.IsWarehouseOfferEligible())
            return Chapter3OvernightCopy.WarehouseEligible;

        var job = state.JobBoard.ActiveJob;
        if (job is not null && job.DeadlineDay <= chapterDay + 1)
            return Chapter3OvernightCopy.JobWaiting;
        var demand = state.ActiveDemand;
        if (demand is not null && demand.DueDay <= chapterDay + 1)
            return Chapter3NellCopy.DemandDueTomorrowText(demand.Quantity - demand.Delivered, demand.ProductName);

        var board = state.JobBoard;
        if (board.OpenOffer is null && board.ActiveJob is null && board.RefusalStreak > 0 &&
            board.CooldownUntilDay.HasValue && chapterDay <= board.CooldownUntilDay.Value)
            return Chapter3OvernightCopy.FamilyWondering;

        return null;
    }
}
