using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3MakeGoodsTests
{
    [Fact]
    public void Failed_job_opens_for_the_reward_with_a_two_day_deadline()
    {
        var opened = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5);

        var effect = Assert.IsType<Chapter3MakeGoodOpenedEffect>(Assert.Single(opened.Effects));
        Assert.Equal(1, effect.MakeGood.Id);
        Assert.Equal(new Chapter3FailedJobSource(2, Chapter3JobType.DeadDrop), effect.MakeGood.Source);
        Assert.Equal(17_000, effect.MakeGood.Amount);
        Assert.Equal(5, effect.MakeGood.OpenedDay);
        Assert.Equal(7, effect.MakeGood.DueDay);
        Assert.Equal(Chapter3MakeGoodTuning.DueDays, effect.MakeGood.DueDay - effect.MakeGood.OpenedDay);
        Assert.Equal(Chapter3MakeGoodStatus.Open, effect.MakeGood.Status);
        Assert.Equal(Chapter3MakeGoodStage.None, effect.MakeGood.Stage);
        Assert.Equal(effect.MakeGood, Assert.Single(opened.Ledger.Entries));
    }

    [Fact]
    public void Missed_cut_opens_at_cut_plus_a_quarter()
    {
        var opened = Chapter3MakeGoods.OpenForMissedCut(Chapter3MakeGoodLedger.Empty, 3, 30_000, 5);

        var effect = Assert.IsType<Chapter3MakeGoodOpenedEffect>(Assert.Single(opened.Effects));
        Assert.Equal(new Chapter3MissedCutSource(3), effect.MakeGood.Source);
        Assert.Equal(37_500, effect.MakeGood.Amount);
        Assert.Equal(7, effect.MakeGood.DueDay);
    }

    [Theory]
    [InlineData(30_000, 37_500)]
    [InlineData(30_001, 37_501)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Missed_cut_keeps_whole_dollars(int cutAmount, int expected)
    {
        var opened = Chapter3MakeGoods.OpenForMissedCut(Chapter3MakeGoodLedger.Empty, 0, cutAmount, 5);

        Assert.Equal(expected, Assert.Single(opened.Ledger.Entries).Amount);
    }

    [Fact]
    public void Missed_demand_opens_at_full_value()
    {
        var opened = Chapter3MakeGoods.OpenForMissedDemand(Chapter3MakeGoodLedger.Empty, "demand-7", 40, 500, 5);

        var effect = Assert.IsType<Chapter3MakeGoodOpenedEffect>(Assert.Single(opened.Effects));
        Assert.Equal(new Chapter3MissedDemandSource("demand-7"), effect.MakeGood.Source);
        Assert.Equal(20_000, effect.MakeGood.Amount);
        Assert.Equal(7, effect.MakeGood.DueDay);
    }

    [Fact]
    public void Openers_are_idempotent_per_source()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;

        var again = Chapter3MakeGoods.OpenForFailedJob(ledger, 2, Chapter3JobType.DeadDrop, 19_000, 6);

        Assert.Same(ledger, again.Ledger);
        Assert.Empty(again.Effects);
        Assert.Equal(17_000, Assert.Single(again.Ledger.Entries).Amount);
    }

    [Fact]
    public void Cut_opener_is_idempotent_per_week()
    {
        var ledger = Chapter3MakeGoods.OpenForMissedCut(Chapter3MakeGoodLedger.Empty, 3, 30_000, 5).Ledger;

        var again = Chapter3MakeGoods.OpenForMissedCut(ledger, 3, 30_000, 6);

        Assert.Same(ledger, again.Ledger);
        Assert.Empty(again.Effects);
        Assert.Single(again.Ledger.Entries);
    }

    [Fact]
    public void Demand_opener_is_idempotent_per_demand()
    {
        var ledger = Chapter3MakeGoods.OpenForMissedDemand(Chapter3MakeGoodLedger.Empty, "demand-7", 40, 500, 5).Ledger;

        var again = Chapter3MakeGoods.OpenForMissedDemand(ledger, "demand-7", 10, 500, 6);

        Assert.Same(ledger, again.Ledger);
        Assert.Empty(again.Effects);
        Assert.Equal(20_000, Assert.Single(again.Ledger.Entries).Amount);
    }

    [Fact]
    public void Tick_on_the_due_day_stays_open_but_the_next_day_goes_overdue_with_a_warning_due()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;

        var dueDay = Chapter3MakeGoods.OnDayTick(ledger, 7);
        Assert.Empty(dueDay.Effects);
        Assert.Equal(Chapter3MakeGoodStatus.Open, Assert.Single(dueDay.Ledger.Entries).Status);

        var overdue = Chapter3MakeGoods.OnDayTick(dueDay.Ledger, 8);
        Assert.Equal(2, overdue.Effects.Count);
        var over = Assert.IsType<Chapter3MakeGoodOverdueEffect>(overdue.Effects[0]);
        Assert.Equal(1, over.MakeGood.Id);
        var warning = Assert.IsType<Chapter3WarningDueEffect>(overdue.Effects[1]);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue, warning.MakeGood.Status);
        Assert.Equal(Chapter3MakeGoodStage.WarningDue, warning.MakeGood.Stage);
    }

    [Fact]
    public void Beating_comes_due_two_days_after_the_warning()
    {
        var ledger = OpenAndOverdue();

        var warned = Chapter3MakeGoods.RecordWarningDelivered(ledger, 1, 8);

        Assert.Empty(warned.Effects);
        Assert.Equal(Chapter3MakeGoodStage.None, Assert.Single(warned.Ledger.Entries).Stage);
        Assert.Equal(8, Assert.Single(warned.Ledger.Entries).WarningDeliveredDay);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue, Assert.Single(warned.Ledger.Entries).Status);

        var nextDay = Chapter3MakeGoods.OnDayTick(warned.Ledger, 9);
        Assert.Empty(nextDay.Effects);

        var beating = Chapter3MakeGoods.OnDayTick(nextDay.Ledger, 10);
        var due = Assert.IsType<Chapter3BeatingDueEffect>(Assert.Single(beating.Effects));
        Assert.Equal(1, due.MakeGood.Id);
        Assert.Equal(Chapter3MakeGoodStage.BeatingDue, due.MakeGood.Stage);
    }

    [Fact]
    public void Beating_stays_owed_and_never_leads_to_a_second_beating()
    {
        var ledger = OpenAndOverdue();
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, 1, 8).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 9).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 10).Ledger;

        var beaten = Chapter3MakeGoods.RecordBeatingDelivered(ledger, 1, 10);

        Assert.Empty(beaten.Effects);
        var entry = Assert.Single(beaten.Ledger.Entries);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue, entry.Status);
        Assert.Equal(Chapter3MakeGoodStage.None, entry.Stage);
        Assert.Equal(10, entry.BeatingDeliveredDay);

        var later = Chapter3MakeGoods.OnDayTick(Chapter3MakeGoods.OnDayTick(beaten.Ledger, 11).Ledger, 15);
        Assert.Empty(later.Effects);
        Assert.Equal(Chapter3MakeGoodStage.None, Assert.Single(later.Ledger.Entries).Stage);

        Assert.Same(beaten.Ledger, Chapter3MakeGoods.RecordBeatingDelivered(beaten.Ledger, 1, 15).Ledger);
    }

    [Fact]
    public void Pay_cancels_a_due_warning()
    {
        var ledger = OpenAndOverdue();

        var paid = Chapter3MakeGoods.Pay(ledger, 1);

        var effect = Assert.IsType<Chapter3MakeGoodPaidEffect>(Assert.Single(paid.Effects));
        Assert.Equal(Chapter3MakeGoodStatus.Paid, effect.MakeGood.Status);
        Assert.Equal(Chapter3MakeGoodStage.None, effect.MakeGood.Stage);

        var later = Chapter3MakeGoods.OnDayTick(paid.Ledger, 20);
        Assert.Empty(later.Effects);
    }

    [Fact]
    public void Pay_cancels_a_due_beating()
    {
        var ledger = OpenAndOverdue();
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, 1, 8).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 9).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 10).Ledger;
        Assert.Equal(Chapter3MakeGoodStage.BeatingDue, Assert.Single(ledger.Entries).Stage);

        var paid = Chapter3MakeGoods.Pay(ledger, 1);

        Assert.Equal(Chapter3MakeGoodStatus.Paid, Assert.Single(paid.Ledger.Entries).Status);
        Assert.Empty(Chapter3MakeGoods.OnDayTick(paid.Ledger, 20).Effects);
    }

    [Fact]
    public void Pay_after_a_beating_still_closes_it()
    {
        var ledger = OpenAndOverdue();
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, 1, 8).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 10).Ledger;
        ledger = Chapter3MakeGoods.RecordBeatingDelivered(ledger, 1, 10).Ledger;
        Assert.Equal(10, Assert.Single(ledger.Entries).BeatingDeliveredDay);

        var paid = Chapter3MakeGoods.Pay(ledger, 1);

        Assert.IsType<Chapter3MakeGoodPaidEffect>(Assert.Single(paid.Effects));
        Assert.Equal(Chapter3MakeGoodStatus.Paid, Assert.Single(paid.Ledger.Entries).Status);
    }

    [Fact]
    public void Pay_before_the_deadline_closes_it()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;

        var paid = Chapter3MakeGoods.Pay(ledger, 1);

        Assert.IsType<Chapter3MakeGoodPaidEffect>(Assert.Single(paid.Effects));
        Assert.Equal(Chapter3MakeGoodStatus.Paid, Assert.Single(paid.Ledger.Entries).Status);
    }

    [Fact]
    public void Pay_twice_pays_once()
    {
        var ledger = Chapter3MakeGoods.Pay(OpenAndOverdue(), 1).Ledger;

        var again = Chapter3MakeGoods.Pay(ledger, 1);

        Assert.Same(ledger, again.Ledger);
        Assert.Empty(again.Effects);
    }

    [Fact]
    public void Several_make_goods_escalate_independently()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        ledger = Chapter3MakeGoods.OpenForMissedCut(ledger, 3, 30_000, 6).Ledger;

        var day8 = Chapter3MakeGoods.OnDayTick(ledger, 8);
        Assert.Equal(2, day8.Effects.Count);
        Assert.Equal(1, ((Chapter3MakeGoodOverdueEffect)day8.Effects[0]).MakeGood.Id);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue, day8.Ledger.Entries[0].Status);
        Assert.Equal(Chapter3MakeGoodStatus.Open, day8.Ledger.Entries[1].Status);

        var day9 = Chapter3MakeGoods.OnDayTick(day8.Ledger, 9);
        Assert.Equal(2, day9.Effects.Count);
        Assert.Equal(2, ((Chapter3MakeGoodOverdueEffect)day9.Effects[0]).MakeGood.Id);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue, day9.Ledger.Entries[1].Status);
    }

    [Fact]
    public void HasOpenMakeGood_covers_open_and_overdue_but_not_paid()
    {
        Assert.False(Chapter3MakeGoodLedger.Empty.HasOpenMakeGood);

        var open = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        Assert.True(open.HasOpenMakeGood);

        var overdue = Chapter3MakeGoods.OnDayTick(open, 8).Ledger;
        Assert.True(overdue.HasOpenMakeGood);

        var paid = Chapter3MakeGoods.Pay(overdue, 1).Ledger;
        Assert.False(paid.HasOpenMakeGood);
    }

    [Fact]
    public void MostUrgentStage_ranks_none_below_open_below_warning_below_beating()
    {
        Assert.Equal(Chapter3MakeGoodUrgency.None, Chapter3MakeGoodLedger.Empty.MostUrgentStage);

        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.Open, ledger.MostUrgentStage);

        ledger = Chapter3MakeGoods.OnDayTick(ledger, 8).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.WarningDue, ledger.MostUrgentStage);

        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, 1, 8).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.Open, ledger.MostUrgentStage);

        ledger = Chapter3MakeGoods.OnDayTick(ledger, 10).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.BeatingDue, ledger.MostUrgentStage);

        ledger = Chapter3MakeGoods.Pay(ledger, 1).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.None, ledger.MostUrgentStage);
    }

    [Fact]
    public void MostUrgentStage_takes_the_worst_across_entries()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        ledger = Chapter3MakeGoods.OpenForMissedCut(ledger, 3, 30_000, 5).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 8).Ledger;

        Assert.Equal(Chapter3MakeGoodUrgency.WarningDue, ledger.MostUrgentStage);

        ledger = Chapter3MakeGoods.Pay(ledger, 1).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.WarningDue, ledger.MostUrgentStage);

        ledger = Chapter3MakeGoods.Pay(ledger, 2).Ledger;
        Assert.Equal(Chapter3MakeGoodUrgency.None, ledger.MostUrgentStage);
    }

    [Fact]
    public void Ticks_for_processed_days_change_nothing()
    {
        var ledger = Chapter3MakeGoods.OnDayTick(OpenAndOverdue(), 8).Ledger;

        var repeat = Chapter3MakeGoods.OnDayTick(ledger, 8);
        var older = Chapter3MakeGoods.OnDayTick(ledger, 5);

        Assert.Same(ledger, repeat.Ledger);
        Assert.Same(ledger, older.Ledger);
        Assert.Empty(repeat.Effects);
        Assert.Empty(older.Effects);
    }

    [Fact]
    public void Unknown_ids_are_noops()
    {
        var ledger = OpenAndOverdue();

        Assert.Same(ledger, Chapter3MakeGoods.Pay(ledger, 999).Ledger);
        Assert.Same(ledger, Chapter3MakeGoods.RecordWarningDelivered(ledger, 999, 8).Ledger);
        Assert.Same(ledger, Chapter3MakeGoods.RecordBeatingDelivered(ledger, 999, 10).Ledger);
    }

    [Fact]
    public void Warning_needs_a_due_warning()
    {
        var open = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;

        Assert.Same(open, Chapter3MakeGoods.RecordWarningDelivered(open, 1, 6).Ledger);

        var warned = Chapter3MakeGoods.RecordWarningDelivered(OpenAndOverdue(), 1, 8).Ledger;

        Assert.Same(warned, Chapter3MakeGoods.RecordWarningDelivered(warned, 1, 9).Ledger);
    }

    [Fact]
    public void Beating_needs_a_due_beating()
    {
        var overdue = OpenAndOverdue();

        Assert.Same(overdue, Chapter3MakeGoods.RecordBeatingDelivered(overdue, 1, 9).Ledger);
    }

    [Fact]
    public void Openers_reject_invalid_inputs()
    {
        var ledger = Chapter3MakeGoodLedger.Empty;

        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForFailedJob(ledger, 0, Chapter3JobType.DeadDrop, 17_000, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForFailedJob(ledger, 2, Chapter3JobType.DeadDrop, -1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForFailedJob(ledger, 2, Chapter3JobType.DeadDrop, 17_000, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForMissedCut(ledger, -1, 30_000, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForMissedCut(ledger, 3, -1, 5));
        Assert.Throws<ArgumentException>(() => Chapter3MakeGoods.OpenForMissedDemand(ledger, "  ", 40, 500, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForMissedDemand(ledger, "demand-7", -1, 500, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3MakeGoods.OpenForMissedDemand(ledger, "demand-7", 40, -1, 5));
    }

    [Fact]
    public void DescribeSource_labels_each_kind()
    {
        Assert.Equal("failed DeadDrop job",
            Chapter3MakeGoods.DescribeSource(new Chapter3FailedJobSource(2, Chapter3JobType.DeadDrop)));
        Assert.Equal("missed week 3 cut",
            Chapter3MakeGoods.DescribeSource(new Chapter3MissedCutSource(3)));
        Assert.Equal("missed demand demand-7",
            Chapter3MakeGoods.DescribeSource(new Chapter3MissedDemandSource("demand-7")));
    }

    private static Chapter3MakeGoodLedger OpenAndOverdue()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        return Chapter3MakeGoods.OnDayTick(ledger, 8).Ledger;
    }
    [Fact]
    public void BeatingByAttack_beats_every_unpaid_overdue_entry_at_once()
    {
        var ledger = Chapter3MakeGoodLedger.Empty;
        ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, 1, Chapter3JobType.DeadDrop, 1_000, chapterDay: 0).Ledger;
        ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, 2, Chapter3JobType.DeadDrop, 2_000, chapterDay: 0).Ledger;
        ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, 3, Chapter3JobType.DeadDrop, 3_000, chapterDay: 0).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 3).Ledger;
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, id: 2, chapterDay: 3).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 5).Ledger;
        // Entry 1 is WarningDue, entry 2 is BeatingDue, entry 3 is overdue with no stage yet.
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, id: 3, chapterDay: 5).Ledger;

        var beaten = Chapter3MakeGoods.RecordBeatingByAttack(ledger, chapterDay: 6).Ledger;

        Assert.All(beaten.Entries, entry =>
        {
            Assert.Equal(Chapter3MakeGoodStage.None, entry.Stage);
            Assert.Equal(6, entry.BeatingDeliveredDay);
            Assert.True(entry.WarningDeliveredDay.HasValue);
        });
        Assert.Equal(6, beaten.Entries[0].WarningDeliveredDay);
        Assert.Equal(3, beaten.Entries[1].WarningDeliveredDay);
        Assert.Equal(5, beaten.Entries[2].WarningDeliveredDay);
    }

    [Fact]
    public void BeatingByAttack_leaves_open_paid_and_already_beaten_entries_alone()
    {
        var ledger = Chapter3MakeGoodLedger.Empty;
        ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, 1, Chapter3JobType.DeadDrop, 1_000, chapterDay: 0).Ledger;
        ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, 2, Chapter3JobType.DeadDrop, 2_000, chapterDay: 4).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 3).Ledger;
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, id: 1, chapterDay: 3).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 5).Ledger;
        ledger = Chapter3MakeGoods.RecordBeatingDelivered(ledger, id: 1, chapterDay: 5).Ledger;

        var beaten = Chapter3MakeGoods.RecordBeatingByAttack(ledger, chapterDay: 6).Ledger;

        Assert.Same(ledger, beaten);
        Assert.Equal(Chapter3MakeGoodStatus.Open, beaten.Entries[1].Status);
        Assert.Equal(5, beaten.Entries[0].BeatingDeliveredDay);
    }

    [Fact]
    public void Beaten_entries_never_become_BeatingDue_again()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 1, Chapter3JobType.DeadDrop, 1_000, chapterDay: 0).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 3).Ledger;
        ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, id: 1, chapterDay: 3).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 5).Ledger;
        ledger = Chapter3MakeGoods.RecordBeatingDelivered(ledger, id: 1, chapterDay: 5).Ledger;

        var ticked = Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 30).Ledger;

        Assert.Equal(Chapter3MakeGoodStage.None, ticked.Entries[0].Stage);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue, ticked.Entries[0].Status);
        Assert.Equal(5, ticked.Entries[0].BeatingDeliveredDay);

        var attacked = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 2_000, chapterDay: 0).Ledger;
        attacked = Chapter3MakeGoods.OnDayTick(attacked, chapterDay: 3).Ledger;
        attacked = Chapter3MakeGoods.RecordBeatingByAttack(attacked, chapterDay: 4).Ledger;
        var reticked = Chapter3MakeGoods.OnDayTick(attacked, chapterDay: 30).Ledger;

        Assert.Equal(Chapter3MakeGoodStage.None, reticked.Entries[0].Stage);
        Assert.Equal(4, reticked.Entries[0].BeatingDeliveredDay);
    }

}
