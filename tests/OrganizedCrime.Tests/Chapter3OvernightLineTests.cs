using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3OvernightLineTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Presentation_constants_are_pinned()
    {
        Assert.Equal(4f, Chapter3OvernightLine.DurationSeconds);
        Assert.Equal(100, Chapter3OvernightLine.Order);
    }

    [Fact]
    public void Copy_lines_are_the_approved_drafts()
    {
        Assert.Equal("Arthur has stopped asking nicely.", Chapter3OvernightCopy.ArthurBeatingDue);
        Assert.Equal("Arthur has been asking where you are.", Chapter3OvernightCopy.ArthurWarningDue);
        Assert.Equal("Nell has something bigger in mind for you.", Chapter3OvernightCopy.WarehouseEligible);
        Assert.Equal("The family's job is still waiting on you.", Chapter3OvernightCopy.JobWaiting);
        Assert.Equal("Nell is wondering if you're still with the family.", Chapter3OvernightCopy.FamilyWondering);
    }

    [Fact]
    public void Inactive_chapter_selects_nothing()
    {
        Assert.Null(Chapter3OvernightLine.Select(Chapter3State.CreateInitial(PlayerId), 0));
    }

    [Fact]
    public void Inactive_gate_wins_over_standing()
    {
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            JobBoard = Chapter3JobScheduler.Refuse(
                Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board,
            Ledger = WarningDueLedger()
        };

        Assert.Null(Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Warning_due_selects_the_Arthur_warning_line()
    {
        var state = StartedState() with { Ledger = WarningDueLedger() };

        Assert.Equal(Chapter3OvernightCopy.ArthurWarningDue, Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Beating_due_selects_the_Arthur_beating_line()
    {
        var ledger = Chapter3MakeGoods.RecordWarningDelivered(WarningDueLedger(), 1, 8).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 10).Ledger;
        var state = StartedState() with { Ledger = ledger };

        Assert.Equal(Chapter3OvernightCopy.ArthurBeatingDue, Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Arthur_beats_a_waiting_job()
    {
        var board = Chapter3JobScheduler.Accept(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        var state = StartedState() with { JobBoard = board, Ledger = WarningDueLedger() };

        Assert.Equal(Chapter3OvernightCopy.ArthurWarningDue, Chapter3OvernightLine.Select(state, 1));
    }

    [Fact]
    public void Arthur_beats_a_wondering_Nell()
    {
        var board = Chapter3JobScheduler.Refuse(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        var state = StartedState() with { JobBoard = board, Ledger = WarningDueLedger() };

        Assert.Equal(Chapter3OvernightCopy.ArthurWarningDue, Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Eligibility_selects_the_Nell_line()
    {
        var state = EligibleState();

        Assert.Equal(Chapter3OvernightCopy.WarehouseEligible, Chapter3OvernightLine.Select(state, 18));
    }

    [Fact]
    public void Open_or_purchased_offer_stops_the_Nell_line()
    {
        var opened = EligibleState() with { WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18) };
        var bought = EligibleState() with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, Purchased: true, PurchasedDay: 20)
        };

        Assert.NotEqual(Chapter3OvernightCopy.WarehouseEligible, Chapter3OvernightLine.Select(opened, 18));
        Assert.NotEqual(Chapter3OvernightCopy.WarehouseEligible, Chapter3OvernightLine.Select(bought, 20));
    }

    [Fact]
    public void Eligibility_beats_a_waiting_job()
    {
        var board = Chapter3JobScheduler.Accept(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        var state = EligibleState() with { JobBoard = board with { SuccessfulJobs = 5 } };

        Assert.Equal(Chapter3OvernightCopy.WarehouseEligible, Chapter3OvernightLine.Select(state, 1));
    }

    [Fact]
    public void Job_with_deadline_within_a_day_selects_the_waiting_line()
    {
        var board = Chapter3JobScheduler.Accept(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        var state = StartedState() with { JobBoard = board };

        Assert.Equal(2, board.ActiveJob!.DeadlineDay);
        Assert.Equal(Chapter3OvernightCopy.JobWaiting, Chapter3OvernightLine.Select(state, 1));
        Assert.Equal(Chapter3OvernightCopy.JobWaiting, Chapter3OvernightLine.Select(state, 2));
    }

    [Fact]
    public void Job_with_far_deadline_selects_nothing()
    {
        var board = Chapter3JobScheduler.Accept(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        var state = StartedState() with { JobBoard = board };

        Assert.Null(Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Cooldown_with_streak_selects_the_wondering_line()
    {
        var board = Chapter3JobScheduler.Refuse(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        var state = StartedState() with { JobBoard = board };

        Assert.Equal(Chapter3OvernightCopy.FamilyWondering, Chapter3OvernightLine.Select(state, 0));
        Assert.Equal(Chapter3OvernightCopy.FamilyWondering, Chapter3OvernightLine.Select(state, 1));
    }

    [Fact]
    public void Wondering_needs_no_open_offer()
    {
        var state = StartedState();

        Assert.NotNull(state.JobBoard.OpenOffer);
        Assert.Null(Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Wondering_needs_no_active_job()
    {
        var board = Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board;
        board = Chapter3JobScheduler.Refuse(board, 1, 0).Board;
        board = Chapter3JobScheduler.OnDayTick(Chapter3JobScheduler.OnDayTick(Chapter3JobScheduler.OnDayTick(board, PlayerId, 1).Board, PlayerId, 2).Board, PlayerId, 3).Board;
        board = Chapter3JobScheduler.Accept(board, 2, 3).Board;
        var state = StartedState() with { JobBoard = board };

        Assert.Equal(1, board.RefusalStreak);
        Assert.Equal(5, board.ActiveJob!.DeadlineDay);
        Assert.Null(Chapter3OvernightLine.Select(state, 3));
    }

    [Fact]
    public void Wondering_needs_a_streak()
    {
        var board = Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board;
        board = Chapter3JobScheduler.Accept(board, 1, 0).Board;
        board = Chapter3JobScheduler.Complete(board, 1, succeeded: true, chapterDay: 0).Board;
        var state = StartedState() with { JobBoard = board };

        Assert.Equal(0, board.RefusalStreak);
        Assert.Null(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Null(Chapter3OvernightLine.Select(state, 0));
    }

    [Fact]
    public void Wondering_needs_cooldown()
    {
        var board = Chapter3JobScheduler.Refuse(
            Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board, 1, 0).Board;
        board = board with { CooldownUntilDay = 0 };
        var state = StartedState() with { JobBoard = board };

        Assert.Null(Chapter3OvernightLine.Select(state, 1));
    }

    [Fact]
    public void Null_state_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Chapter3OvernightLine.Select(null!, 0));
    }

    private static Chapter3State StartedState() =>
        Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;

    private static Chapter3State EligibleState()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        for (var elapsed = 11; elapsed <= 29; elapsed++)
            state = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true).State;
        return state with { JobBoard = state.JobBoard with { SuccessfulJobs = 5 } };
    }

    private static Chapter3MakeGoodLedger WarningDueLedger()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        return Chapter3MakeGoods.OnDayTick(ledger, 8).Ledger;
    }
}
