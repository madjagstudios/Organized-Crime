using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StateTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Starts_on_the_first_tick_after_the_envelope_is_satisfied_and_not_before()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        var before = Chapter3State.ProcessDayTick(state, elapsedDays: 10, envelopeSatisfied: false);

        Assert.False(before.Started);
        Assert.Null(before.ChapterDay);
        Assert.Equal(Chapter3Phase.NotStarted, before.State.Phase);
        Assert.Equal(10, before.State.LastProcessedElapsedDay);

        var start = Chapter3State.ProcessDayTick(before.State, elapsedDays: 11, envelopeSatisfied: true);

        Assert.True(start.Started);
        Assert.Equal(0, start.ChapterDay);
        Assert.Equal(Chapter3Phase.Active, start.State.Phase);
        Assert.Equal(11, start.State.StartElapsedDay);
        Assert.Equal(11, start.State.LastProcessedElapsedDay);
    }

    [Fact]
    public void An_old_save_thats_already_satisfied_starts_on_its_first_tick()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        var start = Chapter3State.ProcessDayTick(state, elapsedDays: 47, envelopeSatisfied: true);

        Assert.True(start.Started);
        Assert.Equal(0, start.ChapterDay);
        Assert.Equal(47, start.State.StartElapsedDay);
    }

    [Fact]
    public void Chapter_day_counts_from_the_start_morning_as_day_zero()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        state = Chapter3State.ProcessDayTick(state, elapsedDays: 11, envelopeSatisfied: true).State;

        var next = Chapter3State.ProcessDayTick(state, elapsedDays: 12, envelopeSatisfied: true);
        var later = Chapter3State.ProcessDayTick(next.State, elapsedDays: 15, envelopeSatisfied: true);

        Assert.False(next.Started);
        Assert.Equal(1, next.ChapterDay);
        Assert.Equal(4, later.ChapterDay);
        Assert.Equal(11, later.State.StartElapsedDay);
    }

    [Fact]
    public void Repeat_ticks_for_the_same_or_an_older_day_change_nothing()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        state = Chapter3State.ProcessDayTick(state, elapsedDays: 11, envelopeSatisfied: true).State;
        state = Chapter3State.ProcessDayTick(state, elapsedDays: 12, envelopeSatisfied: true).State;

        var repeat = Chapter3State.ProcessDayTick(state, elapsedDays: 12, envelopeSatisfied: true);
        var older = Chapter3State.ProcessDayTick(state, elapsedDays: 9, envelopeSatisfied: true);

        Assert.Equal(state, repeat.State);
        Assert.False(repeat.Started);
        Assert.Equal(1, repeat.ChapterDay);
        Assert.Equal(state, older.State);
        Assert.False(older.Started);
        Assert.Equal(1, older.ChapterDay);
    }

    [Fact]
    public void Ticks_never_move_the_processed_day_backwards()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        state = Chapter3State.ProcessDayTick(state, elapsedDays: 20, envelopeSatisfied: false).State;

        var older = Chapter3State.ProcessDayTick(state, elapsedDays: 5, envelopeSatisfied: true);

        Assert.Equal(20, older.State.LastProcessedElapsedDay);
        Assert.False(older.Started);
        Assert.Equal(Chapter3Phase.NotStarted, older.State.Phase);
    }

    [Fact]
    public void Never_starts_while_the_envelope_isnt_satisfied()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        for (var day = 0; day < 30; day++)
            state = Chapter3State.ProcessDayTick(state, elapsedDays: day, envelopeSatisfied: false).State;

        Assert.Equal(Chapter3Phase.NotStarted, state.Phase);
        Assert.Null(state.StartElapsedDay);
        Assert.Equal(29, state.LastProcessedElapsedDay);
    }

    [Fact]
    public void Accepted_ticks_bump_the_revision_but_idempotent_ticks_do_not()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        Assert.Equal(0, state.Revision);

        var first = Chapter3State.ProcessDayTick(state, elapsedDays: 10, envelopeSatisfied: false);
        var repeat = Chapter3State.ProcessDayTick(first.State, elapsedDays: 10, envelopeSatisfied: false);
        var start = Chapter3State.ProcessDayTick(first.State, elapsedDays: 11, envelopeSatisfied: true);

        Assert.Equal(1, first.State.Revision);
        Assert.Equal(1, repeat.State.Revision);
        Assert.Equal(2, start.State.Revision);
    }

    [Fact]
    public void Initial_state_is_unstarted_with_nothing_processed()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        Assert.Equal(PlayerId, state.PlayerId);
        Assert.Equal(Chapter3Phase.NotStarted, state.Phase);
        Assert.Null(state.StartElapsedDay);
        Assert.Equal(-1, state.LastProcessedElapsedDay);
        Assert.Equal(0, state.Revision);
    }

    [Fact]
    public void Rejects_an_empty_player_id()
    {
        Assert.Throws<ArgumentException>(() => Chapter3State.CreateInitial("  "));
    }

    [Fact]
    public void Rejects_inconsistent_phase_and_start_day()
    {
        Assert.Throws<ArgumentException>(() => new Chapter3State(PlayerId, Chapter3Phase.Active, null, 5, 0));
        Assert.Throws<ArgumentException>(() => new Chapter3State(PlayerId, Chapter3Phase.NotStarted, 5, 5, 0));
    }

    [Fact]
    public void CreateInitial_has_an_empty_job_board()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        Assert.Null(state.JobBoard.OpenOffer);
        Assert.Null(state.JobBoard.ActiveJob);
        Assert.Equal(0, state.JobBoard.RefusalStreak);
        Assert.Equal(1, state.JobBoard.NextOfferId);
        Assert.Equal(1, state.JobBoard.NextJobId);
    }

    [Fact]
    public void Start_tick_creates_the_first_DeadDrop_offer()
    {
        var start = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true);

        var created = Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(start.Effects));
        Assert.Equal(Chapter3JobType.DeadDrop, created.Offer.JobType);
        Assert.Equal(created.Offer, start.State.JobBoard.OpenOffer);
    }

    [Fact]
    public void Later_ticks_expire_the_offer_and_count_the_streak()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;

        var day1 = Chapter3State.ProcessDayTick(state, elapsedDays: 12, envelopeSatisfied: true);
        var day2 = Chapter3State.ProcessDayTick(day1.State, elapsedDays: 13, envelopeSatisfied: true);

        Assert.Empty(day1.Effects);
        var expired = Assert.IsType<Chapter3OfferExpiredEffect>(Assert.Single(day2.Effects));
        Assert.Equal(1, expired.RefusalStreak);
        Assert.Equal(1, day2.State.JobBoard.RefusalStreak);
    }

    [Fact]
    public void Ticks_before_the_start_carry_no_effects()
    {
        var tick = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), elapsedDays: 10, envelopeSatisfied: false);

        Assert.Empty(tick.Effects);
        Assert.Null(tick.State.JobBoard.OpenOffer);
    }

    [Fact]
    public void Stale_ticks_return_no_effects()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;

        var repeat = Chapter3State.ProcessDayTick(state, elapsedDays: 11, envelopeSatisfied: true);

        Assert.Empty(repeat.Effects);
        Assert.Equal(state, repeat.State);
    }

    [Fact]
    public void CreateInitial_has_an_empty_ledger()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        Assert.Empty(state.Ledger.Entries);
        Assert.False(state.Ledger.HasOpenMakeGood);
    }

    [Fact]
    public void Warehouse_eligibility_is_false_before_the_start()
    {
        Assert.False(Chapter3State.CreateInitial(PlayerId).IsWarehouseOfferEligible());
    }

    [Fact]
    public void Warehouse_eligibility_needs_days_successes_and_no_open_make_good()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        for (var elapsed = 11; elapsed <= 29; elapsed++)
            state = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true).State;
        state = state with
        {
            JobBoard = state.JobBoard with { SuccessfulJobs = 5 }
        };

        Assert.True(state.IsWarehouseOfferEligible());

        var owing = state with
        {
            Ledger = Chapter3MakeGoods.OpenForFailedJob(state.Ledger, 1, Chapter3JobType.DeadDrop, 17_000, 18).Ledger
        };
        Assert.False(owing.IsWarehouseOfferEligible());
    }

    [Fact]
    public void Warehouse_eligibility_ends_once_the_offer_opens()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        for (var elapsed = 11; elapsed <= 29; elapsed++)
            state = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true).State;
        state = state with
        {
            JobBoard = state.JobBoard with { SuccessfulJobs = 5 }
        };
        Assert.True(state.IsWarehouseOfferEligible());

        var opened = state with { WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18) };
        var bought = state with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, Purchased: true, PurchasedDay: 20)
        };

        Assert.False(opened.IsWarehouseOfferEligible());
        Assert.False(bought.IsWarehouseOfferEligible());
    }

    [Fact]
    public void Warehouse_eligibility_needs_eighteen_days_and_five_successes()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        for (var elapsed = 11; elapsed <= 28; elapsed++)
            state = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true).State;
        state = state with
        {
            JobBoard = state.JobBoard with { SuccessfulJobs = 5 }
        };
        Assert.False(state.IsWarehouseOfferEligible());

        state = Chapter3State.ProcessDayTick(state, 29, envelopeSatisfied: true).State;
        state = state with
        {
            JobBoard = state.JobBoard with { SuccessfulJobs = 4 }
        };
        Assert.False(state.IsWarehouseOfferEligible());
    }}
