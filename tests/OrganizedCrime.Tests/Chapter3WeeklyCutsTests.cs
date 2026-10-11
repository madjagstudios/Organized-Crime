using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3WeeklyCutsTests
{
    [Fact]
    public void Week_pass_opens_a_due_cut_for_thirty_thousand()
    {
        var passed = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true);

        var effect = Assert.IsType<Chapter3WeeklyCutDueEffect>(Assert.Single(passed.Effects));
        Assert.Equal(3, effect.Cut.WeekIndex);
        Assert.Equal(30_000, effect.Cut.Amount);
        Assert.Equal(Chapter3WeeklyCutTuning.CutAmount, effect.Cut.Amount);
        Assert.Equal(10, effect.Cut.DueDay);
        Assert.Equal(12, effect.Cut.GraceUntilDay);
        Assert.Equal(Chapter3WeeklyCutTuning.GraceDays, effect.Cut.GraceUntilDay - effect.Cut.DueDay);
        Assert.Equal(Chapter3WeeklyCutStatus.Due, effect.Cut.Status);
        Assert.Equal(effect.Cut, Assert.Single(passed.Cuts.Entries));
    }

    [Fact]
    public void No_cut_while_not_owned()
    {
        var cuts = Chapter3WeeklyCuts.Empty;

        var passed = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 3, 10, warehouseOwned: false);

        Assert.Same(cuts, passed.Cuts);
        Assert.Empty(passed.Effects);
    }

    [Fact]
    public void Repeated_week_pass_is_a_noop()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;

        var again = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 3, 11, warehouseOwned: true);

        Assert.Same(cuts, again.Cuts);
        Assert.Empty(again.Effects);
        Assert.Equal(10, Assert.Single(again.Cuts.Entries).DueDay);
    }

    [Fact]
    public void Stale_week_pass_for_a_recorded_week_is_a_noop()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 5, 20, warehouseOwned: true).Cuts;

        var stale = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 5, 21, warehouseOwned: true);

        Assert.Same(cuts, stale.Cuts);
        Assert.Empty(stale.Effects);
    }

    [Fact]
    public void Several_weeks_can_be_outstanding_at_once()
    {
        var cuts = Chapter3WeeklyCuts.Empty;
        cuts = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 3, 10, warehouseOwned: true).Cuts;
        cuts = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 4, 17, warehouseOwned: true).Cuts;
        cuts = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 5, 24, warehouseOwned: true).Cuts;

        Assert.Equal(new[] { 3, 4, 5 }, cuts.Entries.Select(entry => entry.WeekIndex).ToArray());
        Assert.All(cuts.Entries, entry => Assert.Equal(Chapter3WeeklyCutStatus.Due, entry.Status));
    }

    [Fact]
    public void Grace_boundary_day_plus_two_is_still_due()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;

        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 12);

        Assert.Equal(Chapter3WeeklyCutStatus.Due, Assert.Single(ticked.Cuts.Entries).Status);
        Assert.Empty(ticked.Effects);
    }

    [Fact]
    public void Grace_boundary_day_plus_three_is_missed()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;

        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 13);

        var effect = Assert.IsType<Chapter3WeeklyCutMissedEffect>(Assert.Single(ticked.Effects));
        Assert.Equal(3, effect.Cut.WeekIndex);
        Assert.Equal(Chapter3WeeklyCutStatus.Missed, effect.Cut.Status);
        Assert.Equal(Chapter3WeeklyCutStatus.Missed, Assert.Single(ticked.Cuts.Entries).Status);
    }

    [Fact]
    public void Missed_cut_opens_exactly_one_make_good_of_thirty_seven_five()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;
        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 13);
        var missed = Assert.IsType<Chapter3WeeklyCutMissedEffect>(Assert.Single(ticked.Effects));

        var opened = Chapter3MakeGoods.OpenForMissedCut(
            Chapter3MakeGoodLedger.Empty, missed.Cut.WeekIndex, missed.Cut.Amount, 13);

        var entry = Assert.Single(opened.Ledger.Entries);
        Assert.Equal(new Chapter3MissedCutSource(3), entry.Source);
        Assert.Equal(37_500, entry.Amount);
    }

    [Fact]
    public void A_paid_cut_never_goes_missed()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;
        cuts = Chapter3WeeklyCutTransitions.Pay(cuts, 3, 11).Cuts;

        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 13);

        Assert.Equal(Chapter3WeeklyCutStatus.Paid, Assert.Single(ticked.Cuts.Entries).Status);
        Assert.Empty(ticked.Effects);
    }

    [Fact]
    public void Pay_marks_due_and_reports_paid()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;

        var paid = Chapter3WeeklyCutTransitions.Pay(cuts, 3, 11);

        var effect = Assert.IsType<Chapter3WeeklyCutPaidEffect>(Assert.Single(paid.Effects));
        Assert.Equal(3, effect.Cut.WeekIndex);
        Assert.Equal(Chapter3WeeklyCutStatus.Paid, effect.Cut.Status);
        Assert.Equal(Chapter3WeeklyCutStatus.Paid, Assert.Single(paid.Cuts.Entries).Status);
    }

    [Fact]
    public void Pay_is_a_noop_for_unknown_paid_and_missed_weeks()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;

        Assert.Same(cuts, Chapter3WeeklyCutTransitions.Pay(cuts, 9, 11).Cuts);
        Assert.Empty(Chapter3WeeklyCutTransitions.Pay(cuts, 9, 11).Effects);

        var paid = Chapter3WeeklyCutTransitions.Pay(cuts, 3, 11).Cuts;
        Assert.Same(paid, Chapter3WeeklyCutTransitions.Pay(paid, 3, 12).Cuts);

        var missed = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 13).Cuts;
        Assert.Same(missed, Chapter3WeeklyCutTransitions.Pay(missed, 3, 13).Cuts);
        Assert.Empty(Chapter3WeeklyCutTransitions.Pay(missed, 3, 13).Effects);
    }

    [Fact]
    public void Day_tick_is_idempotent_per_day()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, 10, warehouseOwned: true).Cuts;
        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 13).Cuts;

        var again = Chapter3WeeklyCutTransitions.OnDayTick(ticked, 13);

        Assert.Same(ticked, again.Cuts);
        Assert.Empty(again.Effects);
    }

    [Fact]
    public void Each_week_goes_missed_on_its_own_day()
    {
        var cuts = Chapter3WeeklyCuts.Empty;
        cuts = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 3, 10, warehouseOwned: true).Cuts;
        cuts = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 4, 17, warehouseOwned: true).Cuts;

        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 13);

        Assert.Equal(
            new[] { Chapter3WeeklyCutStatus.Missed, Chapter3WeeklyCutStatus.Due },
            ticked.Cuts.Entries.Select(entry => entry.Status).ToArray());
        Assert.Equal(3, Assert.IsType<Chapter3WeeklyCutMissedEffect>(Assert.Single(ticked.Effects)).Cut.WeekIndex);
    }

    [Fact]
    public void Negative_week_index_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, -1, 10, warehouseOwned: true));
    }

    [Fact]
    public void Negative_chapter_day_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 3, -1, warehouseOwned: true));
    }

    [Fact]
    public void Duplicate_week_entries_fail_validation()
    {
        var cuts = new Chapter3WeeklyCuts(
            new[]
            {
                new Chapter3WeeklyCut(3, 30_000, 10, 12, Chapter3WeeklyCutStatus.Due),
                new Chapter3WeeklyCut(3, 30_000, 10, 12, Chapter3WeeklyCutStatus.Due)
            },
            -1);

        Assert.Throws<ArgumentException>(() => cuts.Validate());
    }
}
