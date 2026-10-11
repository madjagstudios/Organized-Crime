using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurVisitSchedulerTests
{
    private static Chapter3MakeGoodLedger WarningDueLedger(params int[] amounts)
    {
        var ledger = Chapter3MakeGoodLedger.Empty;
        var jobId = 0;
        foreach (var amount in amounts)
        {
            jobId++;
            ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, jobId, Chapter3JobType.DeadDrop, amount, chapterDay: 0).Ledger;
        }
        // Day 3 is past the 2-day due date, so the tick moves every entry to WarningDue.
        return Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 3).Ledger;
    }

    [Fact]
    public void WarningDue_entries_make_a_warning_pending()
    {
        var ledger = WarningDueLedger(1_250);

        Assert.Equal(Chapter3ArthurVisitKind.Warning, Chapter3ArthurVisitScheduler.DesiredPending(ledger, cooperationDue: false));
    }

    [Fact]
    public void Warning_wins_over_cooperation()
    {
        var ledger = WarningDueLedger(1_250);

        Assert.Equal(Chapter3ArthurVisitKind.Warning, Chapter3ArthurVisitScheduler.DesiredPending(ledger, cooperationDue: true));
    }

    [Fact]
    public void Cooperation_is_pending_only_while_its_streak_is_undelivered()
    {
        Assert.Equal(Chapter3ArthurVisitKind.Cooperation, Chapter3ArthurVisitScheduler.DesiredPending(Chapter3MakeGoodLedger.Empty, cooperationDue: true));
        Assert.Equal(Chapter3ArthurVisitKind.None, Chapter3ArthurVisitScheduler.DesiredPending(Chapter3MakeGoodLedger.Empty, cooperationDue: false));
    }

    [Fact]
    public void Paid_or_unwarned_entries_pending_nothing()
    {
        var ledger = WarningDueLedger(1_250);
        ledger = Chapter3MakeGoods.Pay(ledger, id: 1).Ledger;

        Assert.Equal(Chapter3ArthurVisitKind.None, Chapter3ArthurVisitScheduler.DesiredPending(ledger, cooperationDue: false));
    }

    [Theory]
    [InlineData(17 * 60 + 59, false)]
    [InlineData(18 * 60, true)]
    [InlineData(23 * 60, true)]
    [InlineData(23 * 60 + 1, false)]
    public void Window_boundaries(int minuteOfDay, bool expected)
    {
        Assert.Equal(expected, Chapter3ArthurVisitScheduler.IsInWindow(minuteOfDay));
    }

    [Fact]
    public void In_window_with_conditions_starts_the_visit_once_per_day()
    {
        var first = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.None, 0, -1, Chapter3ArthurVisitKind.Warning,
            chapterDay: 5, minuteOfDay: 19 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitAction.StartVisit, first.Action);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, first.ActionKind);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, first.PendingKind);
        Assert.Equal(5, first.LastAttemptedDay);

        var second = Chapter3ArthurVisitScheduler.Decide(
            first.PendingKind, first.MissedEvenings, first.LastAttemptedDay, Chapter3ArthurVisitKind.Warning,
            chapterDay: 5, minuteOfDay: 20 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitAction.None, second.Action);
    }

    [Fact]
    public void Failed_conditions_start_nothing_and_miss_nothing_yet()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.None, 0, -1, Chapter3ArthurVisitKind.Warning,
            chapterDay: 5, minuteOfDay: 19 * 60, conditionsMet: false);

        Assert.Equal(Chapter3ArthurVisitAction.None, decision.Action);
        Assert.Equal(0, decision.MissedEvenings);
        Assert.Equal(4, decision.LastAttemptedDay);
    }

    [Fact]
    public void Window_end_without_a_visit_counts_one_missed_evening()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Warning, 0, 4, Chapter3ArthurVisitKind.Warning,
            chapterDay: 5, minuteOfDay: 23 * 60 + 1, conditionsMet: false);

        Assert.Equal(Chapter3ArthurVisitAction.None, decision.Action);
        Assert.Equal(1, decision.MissedEvenings);
        Assert.Equal(5, decision.LastAttemptedDay);
    }

    [Fact]
    public void Sleeping_past_evenings_misses_each_one()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Warning, 1, 5, Chapter3ArthurVisitKind.Warning,
            chapterDay: 8, minuteOfDay: 9 * 60, conditionsMet: false);

        Assert.Equal(3, decision.MissedEvenings);
        Assert.Equal(7, decision.LastAttemptedDay);
    }

    [Fact]
    public void Evenings_before_the_series_started_never_count_as_missed()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.None, 0, -1, Chapter3ArthurVisitKind.Warning,
            chapterDay: 8, minuteOfDay: 23 * 60 + 30, conditionsMet: false);

        Assert.Equal(0, decision.MissedEvenings);
        Assert.Equal(8, decision.LastAttemptedDay);
    }

    [Fact]
    public void Phone_fallback_fires_on_the_third_evening()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Warning, 2, 6, Chapter3ArthurVisitKind.Warning,
            chapterDay: 7, minuteOfDay: 18 * 60, conditionsMet: false);

        Assert.Equal(Chapter3ArthurVisitAction.PhoneFallback, decision.Action);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, decision.ActionKind);
        Assert.Equal(7, decision.LastAttemptedDay);
    }

    [Fact]
    public void Kind_change_resets_the_series()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Warning, 2, 6, Chapter3ArthurVisitKind.Cooperation,
            chapterDay: 7, minuteOfDay: 19 * 60, conditionsMet: true);

        Assert.Equal(0, decision.MissedEvenings);
        Assert.Equal(Chapter3ArthurVisitAction.StartVisit, decision.Action);
        Assert.Equal(Chapter3ArthurVisitKind.Cooperation, decision.ActionKind);
    }

    [Fact]
    public void Cleared_pending_resets_the_series()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Warning, 1, 6, Chapter3ArthurVisitKind.None,
            chapterDay: 7, minuteOfDay: 19 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitKind.None, decision.PendingKind);
        Assert.Equal(0, decision.MissedEvenings);
        Assert.Equal(-1, decision.LastAttemptedDay);
        Assert.Equal(Chapter3ArthurVisitAction.None, decision.Action);
    }

    [Fact]
    public void Copy_renders_the_ticket_lines_verbatim()
    {
        Assert.Equal("Evening. Nell tells me you're behind.", Chapter3ArthurCopy.WarningLine1);
        Assert.Equal("$1,250 to the family. That's what you owe.", Chapter3ArthurCopy.WarningLine2(1_250));
        Assert.Equal("Two days. I won't be this polite next time.", Chapter3ArthurCopy.WarningLine3);
        Assert.Equal("Nell says you keep telling her no.", Chapter3ArthurCopy.CooperationLine1);
        Assert.Equal("The family doesn't like no. Take the next one.", Chapter3ArthurCopy.CooperationLine2);
        Assert.Equal(
            "Couldn't find you tonight. You owe the family $1,250. Two days, then I find you.",
            Chapter3ArthurCopy.PhoneWarning(1_250));
        Assert.Equal(
            "Nell says you keep saying no. Take the next job. That's not a request.",
            Chapter3ArthurCopy.PhoneCooperation);
        Assert.Equal("Arthur came by. Pay the family, and do it fast.", Chapter3NellCopy.ArthurWarningFollowupText);
        Assert.Equal("Arthur says you walked off on him. Don't fucking do that again.", Chapter3NellCopy.ArthurRanOffText);
        Assert.Equal("I said I wouldn't be polite.", Chapter3ArthurCopy.BeatingScheduledLine);
        Assert.Equal("Wrong fucking move.", Chapter3ArthurCopy.BeatingAttackLine);
        Assert.Equal("You swung at Arthur? Don't be fucking stupid.", Chapter3NellCopy.ArthurBeatingNoDebtText);
        Assert.Equal(
            "Arthur sorted you out. You still owe the family $1,250, so fucking pay it.",
            Chapter3NellCopy.ArthurBeatingDebtsText(1_250));
    }
    private static Chapter3MakeGoodLedger BeatingDueLedger(params int[] amounts)
    {
        var ledger = WarningDueLedger(amounts);
        var jobId = 0;
        foreach (var _ in amounts)
        {
            jobId++;
            ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, jobId, chapterDay: 3).Ledger;
        }
        // Two days after the delivered warning, the tick moves every entry to BeatingDue.
        return Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 5).Ledger;
    }

    [Fact]
    public void BeatingDue_entries_make_a_beating_pending()
    {
        var ledger = BeatingDueLedger(1_250);

        Assert.Equal(Chapter3ArthurVisitKind.Beating, Chapter3ArthurVisitScheduler.DesiredPending(ledger, cooperationDue: false));
    }

    [Fact]
    public void Beating_wins_over_warning_and_cooperation()
    {
        var ledger = BeatingDueLedger(1_250);
        ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, 99, Chapter3JobType.DeadDrop, 500, chapterDay: 4).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 7).Ledger;

        Assert.Contains(ledger.Entries, entry => entry.Stage == Chapter3MakeGoodStage.WarningDue);
        Assert.Equal(Chapter3ArthurVisitKind.Beating, Chapter3ArthurVisitScheduler.DesiredPending(ledger, cooperationDue: true));
    }

    [Fact]
    public void Beating_uses_the_evening_window_before_two_misses()
    {
        var inWindow = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.None, 0, -1, Chapter3ArthurVisitKind.Beating,
            chapterDay: 5, minuteOfDay: 19 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitAction.StartVisit, inWindow.Action);
        Assert.Equal(Chapter3ArthurVisitKind.Beating, inWindow.ActionKind);

        var outOfWindow = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Beating, 0, 4, Chapter3ArthurVisitKind.Beating,
            chapterDay: 5, minuteOfDay: 10 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitAction.None, outOfWindow.Action);
    }

    [Fact]
    public void Beating_window_widens_after_two_missed_evenings()
    {
        Assert.False(Chapter3ArthurVisitScheduler.IsWidened(Chapter3ArthurVisitKind.Beating, 1));
        Assert.True(Chapter3ArthurVisitScheduler.IsWidened(Chapter3ArthurVisitKind.Beating, 2));
        Assert.False(Chapter3ArthurVisitScheduler.IsWidened(Chapter3ArthurVisitKind.Warning, 5));

        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Beating, 2, 6, Chapter3ArthurVisitKind.Beating,
            chapterDay: 7, minuteOfDay: 10 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitAction.StartVisit, decision.Action);
        Assert.Equal(Chapter3ArthurVisitKind.Beating, decision.ActionKind);
    }

    [Fact]
    public void Widened_beating_still_needs_conditions()
    {
        var decision = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Beating, 2, 6, Chapter3ArthurVisitKind.Beating,
            chapterDay: 7, minuteOfDay: 10 * 60, conditionsMet: false);

        Assert.Equal(Chapter3ArthurVisitAction.None, decision.Action);
    }

    [Fact]
    public void Beating_never_phones()
    {
        var unmet = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Beating, 3, 6, Chapter3ArthurVisitKind.Beating,
            chapterDay: 7, minuteOfDay: 19 * 60, conditionsMet: false);

        Assert.Equal(Chapter3ArthurVisitAction.None, unmet.Action);

        var met = Chapter3ArthurVisitScheduler.Decide(
            Chapter3ArthurVisitKind.Beating, 3, 6, Chapter3ArthurVisitKind.Beating,
            chapterDay: 7, minuteOfDay: 19 * 60, conditionsMet: true);

        Assert.Equal(Chapter3ArthurVisitAction.StartVisit, met.Action);
    }

}
