using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3QaCommandTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Disabled_qa_refuses_without_touching_the_target()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: false);

        Assert.Equal(new[] { "Chapter 3 QA commands are disabled: OwnerQaKeys is off." }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Empty_args_print_usage()
    {
        var lines = Chapter3QaCommand.Execute(new FakeTarget(), Array.Empty<string>(), qaEnabled: true);

        Assert.Equal(Usage(), lines);
    }

    [Fact]
    public void Unknown_subcommand_prints_an_error_then_usage()
    {
        var lines = Chapter3QaCommand.Execute(new FakeTarget(), new[] { "explode" }, qaEnabled: true);

        Assert.Equal(new[] { "Unknown oc_ch3 subcommand: 'explode'." }.Concat(Usage()).ToArray(), lines);
    }

    [Fact]
    public void Status_without_state_says_so()
    {
        var lines = Chapter3QaCommand.Execute(new FakeTarget { State = null }, new[] { "status" }, qaEnabled: true);

        Assert.Equal(new[] { "Chapter 3 QA status: no loaded state." }, lines);
    }

    [Fact]
    public void Status_before_the_start_says_so()
    {
        var target = new FakeTarget { State = Chapter3State.CreateInitial(PlayerId) };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Equal(new[] { "Chapter 3 QA status: not started." }, lines);
    }

    [Fact]
    public void Status_shows_the_board_and_the_ledger()
    {
        var state = ActiveState();
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "STATUS" }, qaEnabled: true);

        var offer = state.JobBoard.OpenOffer!;
        var entry = state.Ledger.Entries[0];
        Assert.Equal(
            new[]
            {
                "Chapter 3 QA status: day 4",
                $"offer: #{offer.OfferId} {offer.JobType}, ${offer.Reward:N0}, respond by day {offer.RespondByDay}",
                "job: none",
                "warehouse: not eligible",
                $"make-good #{entry.Id}: failed {((Chapter3FailedJobSource)entry.Source).JobType} job, ${entry.Amount:N0}, due day {entry.DueDay}, Overdue, warning due",
                "cuts: none",
                "demand: none",
                "demand eligible: none",
                "last demand: none",
                "streak 1, successes 0, warehouse eligible: no",
                "arthur visit: none pending; delivered: cooperation no, warnings 0",
                "nell messages (3):",
                $"  \"{Chapter3NellCopy.DeclinedText}\"",
                $"  \"{Chapter3NellCopy.MakeGoodOpenedText(entry.Source, entry.Amount)}\"",
                $"  \"{Chapter3NellCopy.MakeGoodOverdueText}\"",
                $"nell buttons: {Chapter3NellCopy.PayLabel(entry.Amount)} | {Chapter3NellCopy.AcceptLabel} | {Chapter3NellCopy.DeclineLabel}",
                "overnight: \"Arthur has been asking where you are.\""
            },
            lines);
    }

    [Fact]
    public void Status_with_no_make_goods_says_none()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("make-goods: none", lines);
    }

    [Fact]
    public void Accept_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "accepted") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "accept" }, qaEnabled: true);

        Assert.Equal(new[] { "accepted" }, lines);
        Assert.Equal(new[] { "accept" }, target.Calls);
    }

    [Fact]
    public void Fail_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(false, "no job") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "fail" }, qaEnabled: true);

        Assert.Equal(new[] { "no job" }, lines);
        Assert.Equal(new[] { "fail" }, target.Calls);
    }

    [Fact]
    public void Deliver_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "delivered") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "deliver" }, qaEnabled: true);

        Assert.Equal(new[] { "delivered" }, lines);
        Assert.Equal(new[] { "deliver" }, target.Calls);
    }

    [Fact]
    public void Status_shows_frozen_delivery_terms()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        var job = accepted.Board.ActiveJob! with
        {
            Delivery = new Chapter3DeadDropDelivery(
                "drop-a", "Drop A", 1d, 2d, 3d, "rich", "Rich", "brick", "Brick", 2)
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("job: #1 DeadDrop, accepted day 0, deadline day 2, deliver 2 bricks of Rich at Drop A", lines);
    }

    [Fact]
    public void Status_shows_removal_confirmed()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        var job = accepted.Board.ActiveJob! with
        {
            Delivery = new Chapter3DeadDropDelivery(
                "drop-a", "Drop A", 1d, 2d, 3d, "rich", "Rich", "brick", "Brick", 2, true)
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("removal confirmed", lines.Single(line => line.StartsWith("job:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Status_keeps_the_plain_job_line_when_unfrozen()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        state = state with { JobBoard = accepted.Board };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("job: #1 DeadDrop, accepted day 0, deadline day 2", lines);
    }

    [Fact]
    public void Status_shows_frozen_recovery_terms_and_stage()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        var job = accepted.Board.ActiveJob! with
        {
            JobType = Chapter3JobType.PackageRecovery,
            Recovery = new Chapter3PackageRecovery(
                new Chapter3RecoveryDrop("drop-a", "Drop A", 1d, 2d, 3d),
                new Chapter3RecoveryDrop("drop-b", "Drop B", 4d, 5d, 6d),
                "rich", "Rich", "brick", "Brick", 2, 0, true)
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("job: #1 PackageRecovery, accepted day 0, deadline day 2, recover 2 bricks of Rich from Drop A to Drop B, staged", lines);
    }

    [Fact]
    public void Status_shows_recovery_custody()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        var job = accepted.Board.ActiveJob! with
        {
            JobType = Chapter3JobType.PackageRecovery,
            Recovery = new Chapter3PackageRecovery(
                new Chapter3RecoveryDrop("drop-a", "Drop A", 1d, 2d, 3d),
                new Chapter3RecoveryDrop("drop-b", "Drop B", 4d, 5d, 6d),
                "rich", "Rich", "brick", "Brick", 2, 0, true, true)
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("in custody", lines.Single(line => line.StartsWith("job:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Pay_needs_a_numeric_id()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "paid") };

        Assert.Equal(new[] { "Usage: oc_ch3 pay <id>" }, Chapter3QaCommand.Execute(target, new[] { "pay" }, qaEnabled: true));
        Assert.Equal(new[] { "Usage: oc_ch3 pay <id>" }, Chapter3QaCommand.Execute(target, new[] { "pay", "abc" }, qaEnabled: true));
        Assert.Empty(target.Calls);

        var lines = Chapter3QaCommand.Execute(target, new[] { "pay", "2" }, qaEnabled: true);

        Assert.Equal(new[] { "paid" }, lines);
        Assert.Equal(new[] { "pay:2" }, target.Calls);
    }

    [Fact]
    public void Ids_accept_the_hash_form_that_status_prints()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "ok") };

        Chapter3QaCommand.Execute(target, new[] { "pay", "#1" }, qaEnabled: true);
        Chapter3QaCommand.Execute(target, new[] { "warn", "#2" }, qaEnabled: true);

        Assert.Equal(new[] { "pay:1", "warn:2" }, target.Calls);
    }

    [Fact]
    public void Warn_needs_a_numeric_id()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "warned") };

        Assert.Equal(new[] { "Usage: oc_ch3 warn <id>" }, Chapter3QaCommand.Execute(target, new[] { "warn" }, qaEnabled: true));
        Assert.Empty(target.Calls);

        var lines = Chapter3QaCommand.Execute(target, new[] { "warn", "1" }, qaEnabled: true);

        Assert.Equal(new[] { "warned" }, lines);
        Assert.Equal(new[] { "warn:1" }, target.Calls);
    }

    private static string[] Usage() => new[]
    {
        "oc_ch3 status",
        "oc_ch3 accept",
        "oc_ch3 fail",
        "oc_ch3 deliver",
        "oc_ch3 offer emergency",
        "oc_ch3 warehouse",
        "oc_ch3 warehouse spawns | spawn here <exterior | interior>",
        "oc_ch3 pay <id>",
        "oc_ch3 paycut <week>",
        "oc_ch3 warn <id>",
        "oc_ch3 weekpass",
        "oc_ch3 arthur come | say <text> | leave | status | hq <on | off | auto | here> | visit <warn | coop | beat | call> | beat",
        "oc_ch3 beat probe snapshot | invincible <on | off> | anim [trigger]",
        "oc_ch3 demand now | eligible [productId] | fresh [units] | expire",
        "oc_ch3 capacity [unlock | set <1 | 2 | 3> | here]",
        "oc_ch3 notice show | hide",
        "oc_ch3 protection offer | status | lapse",
        "oc_ch3 lockdown age <hours>",
        "oc_ch3 wanted status",
        "oc_ch3 officers status | spawn <1-8> | clear",
        "oc_ch3 checkpoint status | spot here <label> | test <label> | clear | pick"
    };

    private static Chapter3State ActiveState()
    {
        // Day 4 with an open offer, refusal streak 1, and one overdue make-good.
        var state = Chapter3State.CreateInitial(PlayerId);
        for (var elapsed = 11; elapsed <= 15; elapsed++)
        {
            var tick = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true);
            state = tick.State;
            foreach (var effect in tick.Effects)
            {
                if (effect is Chapter3OfferCreatedEffect created && tick.ChapterDay == 0)
                    state = state with
                    {
                        JobBoard = Chapter3JobScheduler.Refuse(state.JobBoard, created.Offer.OfferId, 0).Board
                    };
            }
        }
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 1, Chapter3JobType.DeadDrop, 17_000, 1).Ledger;
        ledger = Chapter3MakeGoods.OnDayTick(ledger, 4).Ledger;
        return state with { Ledger = ledger };
    }

    [Fact]
    public void Status_shows_buttons_but_no_messages_for_a_fresh_offer()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("nell messages: none", lines);
        Assert.Contains($"nell buttons: {Chapter3NellCopy.AcceptLabel} | {Chapter3NellCopy.DeclineLabel}", lines);
    }

    [Fact]
    public void Status_shows_overnight_none()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Equal("overnight: none", lines[^1]);
    }

    [Fact]
    public void Offer_emergency_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "forced") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "offer", "emergency" }, qaEnabled: true);

        Assert.Equal(new[] { "forced" }, lines);
        Assert.Equal(new[] { "offer" }, target.Calls);
    }

    [Fact]
    public void Warehouse_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "opened") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "warehouse" }, qaEnabled: true);

        Assert.Equal(new[] { "opened" }, lines);
        Assert.Equal(new[] { "warehouse" }, target.Calls);
    }

    [Fact]
    public void Warehouse_spawns_forwards_the_status_lines()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "warehouse", "spawns" }, qaEnabled: true);

        Assert.Equal(new[] { "spawns" }, lines);
        Assert.Contains("warehouse-spawns", target.Calls);
    }

    [Theory]
    [InlineData("exterior", "warehouse-spawn-here:exterior")]
    [InlineData("interior", "warehouse-spawn-here:interior")]
    [InlineData("Exterior", "warehouse-spawn-here:exterior")]
    public void Warehouse_spawn_here_routes_each_side(string side, string call)
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "moved") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "warehouse", "spawn", "here", side }, qaEnabled: true);

        Assert.Equal(new[] { "moved" }, lines);
        Assert.Contains(call, target.Calls);
    }

    public static IEnumerable<object[]> BadWarehouseForms() => new[]
    {
        new object[] { new[] { "warehouse", "spawn" } },
        new object[] { new[] { "warehouse", "spawn", "here" } },
        new object[] { new[] { "warehouse", "spawn", "here", "roof" } },
        new object[] { new[] { "warehouse", "spawns", "extra" } },
        new object[] { new[] { "warehouse", "roof" } }
    };

    [Theory]
    [MemberData(nameof(BadWarehouseForms))]
    public void Warehouse_rejects_bad_forms_with_usage(string[] args)
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, args, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 warehouse [spawns | spawn here <exterior | interior>]" }, lines);
        Assert.DoesNotContain(target.Calls, call => call.StartsWith("warehouse-spawn", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_shows_the_warehouse_offer_states()
    {
        var fresh = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var freshLines = Chapter3QaCommand.Execute(new FakeTarget { State = fresh }, new[] { "status" }, qaEnabled: true);
        Assert.Contains("warehouse: not eligible", freshLines);

        var open = fresh with { WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 3) };
        var openLines = Chapter3QaCommand.Execute(new FakeTarget { State = open }, new[] { "status" }, qaEnabled: true);
        Assert.Contains("warehouse: open since day 3, reminder week 0", openLines);

        var reminded = fresh with { WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 3, LastReminderWeek: 2) };
        var remindedLines = Chapter3QaCommand.Execute(new FakeTarget { State = reminded }, new[] { "status" }, qaEnabled: true);
        Assert.Contains("warehouse: open since day 3, reminder week 2", remindedLines);

        var bought = fresh with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 3, Purchased: true, PurchasedDay: 9)
        };
        var boughtLines = Chapter3QaCommand.Execute(new FakeTarget { State = bought }, new[] { "status" }, qaEnabled: true);
        Assert.Contains("warehouse: purchased day 9, build pending", boughtLines);

        var builtLines = Chapter3QaCommand.Execute(
            new FakeTarget { State = bought, WarehouseOwned = true }, new[] { "status" }, qaEnabled: true);
        Assert.Contains("warehouse: purchased day 9", builtLines);
        Assert.DoesNotContain("warehouse: purchased day 9, build pending", builtLines);
    }

    [Fact]
    public void Status_shows_eligible_before_the_offer_opens()
    {
        var fresh = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var board = fresh.JobBoard with { SuccessfulJobs = 5 };
        var state = new Chapter3State(
            fresh.PlayerId, Chapter3Phase.Active, fresh.StartElapsedDay, 29, fresh.Revision, board);
        Assert.True(state.IsWarehouseOfferEligible());

        var lines = Chapter3QaCommand.Execute(new FakeTarget { State = state }, new[] { "status" }, qaEnabled: true);

        Assert.Contains("warehouse: eligible", lines);
    }

    [Fact]
    public void Offer_needs_the_emergency_word()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "forced") };

        Assert.Equal(
            new[] { "Usage: oc_ch3 offer emergency" },
            Chapter3QaCommand.Execute(target, new[] { "offer" }, qaEnabled: true));
        Assert.Equal(
            new[] { "Usage: oc_ch3 offer emergency" },
            Chapter3QaCommand.Execute(target, new[] { "offer", "deaddrop" }, qaEnabled: true));
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Status_shows_emergency_terms_and_minutes_left()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        var job = accepted.Board.ActiveJob! with
        {
            JobType = Chapter3JobType.EmergencySupply,
            Delivery = new Chapter3DeadDropDelivery(
                "drop-a", "Drop A", 1d, 2d, 3d, "rich", "Rich", "brick", "Brick", 2),
            AcceptedGameMinutes = 1_000d,
            ExpiresGameMinutes = 1_720d
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };
        var target = new FakeTarget { State = state, MinutesLeft = 712 };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains(
            "job: #1 EmergencySupply, accepted day 0, deadline day 2, deliver 2 bricks of Rich at Drop A, 712 minutes left",
            lines);
    }

    [Fact]
    public void Status_says_unknown_when_the_window_is_unreadable()
    {
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, 0);
        var job = accepted.Board.ActiveJob! with
        {
            JobType = Chapter3JobType.EmergencySupply,
            Delivery = new Chapter3DeadDropDelivery(
                "drop-a", "Drop A", 1d, 2d, 3d, "rich", "Rich", "brick", "Brick", 2),
            AcceptedGameMinutes = 1_000d,
            ExpiresGameMinutes = 1_720d
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };
        var target = new FakeTarget { State = state, MinutesLeft = null };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("minutes left unknown", lines.Single(line => line.StartsWith("job:", StringComparison.Ordinal)));
    }
    [Fact]
    public void Arthur_hq_forwards_the_mode_and_needs_one()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "hq on") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "hq", "on" }, qaEnabled: true);

        Assert.Equal(new[] { "hq on" }, lines);
        Assert.Equal(new[] { "arthur-hq:on" }, target.Calls);

        var usage = Chapter3QaCommand.Execute(target, new[] { "arthur", "hq" }, qaEnabled: true);
        Assert.Equal(new[] { "Usage: oc_ch3 arthur hq <on | off | auto | here>" }, usage);
    }

    [Fact]
    public void Arthur_hq_here_forwards_to_the_spot_capture()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "captured") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "hq", "here" }, qaEnabled: true);

        Assert.Equal(new[] { "captured" }, lines);
        Assert.Equal(new[] { "arthur-hq-here" }, target.Calls);

        var upper = new FakeTarget { Result = new Chapter3QaResult(true, "ok") };
        Chapter3QaCommand.Execute(upper, new[] { "arthur", "hq", "HERE" }, qaEnabled: true);
        Assert.Equal(new[] { "arthur-hq-here" }, upper.Calls);
    }

    [Fact]
    public void Arthur_visit_forwards_the_kind_and_needs_one()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "visiting") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "visit", "warn" }, qaEnabled: true);

        Assert.Equal(new[] { "visiting" }, lines);
        Assert.Equal(new[] { "arthur-visit:warn" }, target.Calls);

        var usage = Chapter3QaCommand.Execute(target, new[] { "arthur", "visit" }, qaEnabled: true);
        Assert.Equal(new[] { "Usage: oc_ch3 arthur visit <warn | coop | beat | call>" }, usage);
    }

    [Fact]
    public void Arthur_beat_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "beat" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "arthur-beat" }, target.Calls);
    }

    [Fact]
    public void Demand_now_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "now" }, qaEnabled: true);

        Assert.Equal(new[] { "now" }, lines);
        Assert.Equal(new[] { "demand-now" }, target.Calls);
    }

    [Fact]
    public void Demand_eligible_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "eligible" }, qaEnabled: true);

        Assert.Equal(new[] { "eligible" }, lines);
        Assert.Equal(new[] { "demand-eligible" }, target.Calls);
    }

    [Fact]
    public void Demand_fresh_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "fresh" }, qaEnabled: true);

        Assert.Equal(new[] { "fresh" }, lines);
        Assert.Equal(new[] { "demand-fresh" }, target.Calls);
    }

    [Fact]
    public void Demand_expire_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "expire" }, qaEnabled: true);

        Assert.Equal(new[] { "expire" }, lines);
        Assert.Equal(new[] { "demand-expire" }, target.Calls);
    }

    [Fact]
    public void Demand_unknown_subcommand_prints_an_error()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "bogus" }, qaEnabled: true);

        Assert.Equal(new[] { "Unknown oc_ch3 demand subcommand: 'bogus'." }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Demand_bare_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 demand now | eligible [productId] | fresh [units] | expire" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Demand_fresh_with_units_forwards_the_add()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "fresh", "50" }, qaEnabled: true);

        Assert.Equal(new[] { "fresh-add" }, lines);
        Assert.Equal(new[] { "demand-fresh-add:50" }, target.Calls);
    }

    [Fact]
    public void Demand_fresh_with_bad_units_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "fresh", "many" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 demand fresh [units]" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Demand_eligible_with_product_forwards_the_add()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "demand", "eligible", "ogkush" }, qaEnabled: true);

        Assert.Equal(new[] { "eligible-add" }, lines);
        Assert.Equal(new[] { "demand-eligible-add:ogkush" }, target.Calls);
    }

    [Fact]
    public void Status_shows_the_open_demand_and_the_last_outcome()
    {
        var demand = new Chapter3ProductionDemand(
            DemandId: "d2",
            ProductId: "ogkush",
            ProductName: "OG Kush",
            ProductType: Chapter3DemandProductType.Weed,
            Quantity: 250,
            UnitValue: 50,
            IssueDay: 6,
            DueDay: 9,
            FreshCount: 30,
            Delivered: 10,
            PaidTotal: 250,
            LastTakeUnits: 10,
            HandOverCount: 1,
            HqDoorX: 10,
            HqDoorY: 20,
            HqDoorZ: 30,
            PendingRemoval: null);
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State with
        {
            ActiveDemand = demand,
            MadeProductIds = new[] { "ogkush" },
            LastDemandWeek = 1,
            NextDemandId = 3,
            LastDemandOutcome = new Chapter3DemandOutcome("d1", "Sour Diesel", LeftUnits: 60, Amount: 3_000, Completed: false)
        };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("demand: d2 250 OG Kush (weed), due day 9, made 30, handed 10, paid $250", lines);
        Assert.Contains("demand eligible: ogkush (last week 1, next id 3)", lines);
        Assert.Contains("last demand: d1 missed, 60 short, $3,000 make-good", lines);
    }

    private sealed class FakeTarget : IChapter3QaTarget
    {
        public Chapter3State? State { get; set; }
        public Chapter3QaResult Result { get; set; } = new(true, "ok");
        public List<string> Calls { get; } = new();

        public Chapter3QaResult QaAcceptOpenOffer()
        {
            Calls.Add("accept");
            return Result;
        }

        public Chapter3QaResult QaFailActiveJob()
        {
            Calls.Add("fail");
            return Result;
        }

        public Chapter3QaResult QaPayMakeGood(int id)
        {
            Calls.Add($"pay:{id}");
            return Result;
        }

        public Chapter3QaResult QaRecordWarning(int id)
        {
            Calls.Add($"warn:{id}");
            return Result;
        }

        public Chapter3QaResult QaSimulateWeekPass()
        {
            Calls.Add("weekpass");
            return Result;
        }

        public Chapter3QaResult QaPayCut(int weekIndex)
        {
            Calls.Add($"paycut:{weekIndex}");
            return Result;
        }

        public Chapter3QaResult QaDeliverActiveJob()
        {
            Calls.Add("deliver");
            return Result;
        }

        public int? MinutesLeft { get; set; }

        public Chapter3QaResult QaOfferEmergency()
        {
            Calls.Add("offer");
            return Result;
        }

        public bool WarehouseOwned { get; set; }

        public bool QaWarehouseOwned() => WarehouseOwned;

        public Chapter3QaResult QaOfferWarehouse()
        {
            Calls.Add("warehouse");
            return Result;
        }

        public IReadOnlyList<string> QaWarehouseSpawns()
        {
            Calls.Add("warehouse-spawns");
            return new[] { "spawns" };
        }

        public Chapter3QaResult QaWarehouseSpawnHere(bool exterior)
        {
            Calls.Add(exterior ? "warehouse-spawn-here:exterior" : "warehouse-spawn-here:interior");
            return Result;
        }

        public int? QaEmergencyMinutesLeft() => MinutesLeft;

        public Chapter3QaResult QaArthurCome()
        {
            Calls.Add("arthur-come");
            return Result;
        }

        public Chapter3QaResult QaArthurSay(string text)
        {
            Calls.Add($"arthur-say:{text}");
            return Result;
        }

        public Chapter3QaResult QaArthurLeave()
        {
            Calls.Add("arthur-leave");
            return Result;
        }

        public IReadOnlyList<string> QaArthurStatus()
        {
            Calls.Add("arthur-status");
            return new[] { "arthur: parked" };
        }

        public Chapter3QaResult QaArthurHq(string mode)
        {
            Calls.Add($"arthur-hq:{mode}");
            return Result;
        }

        public Chapter3QaResult QaArthurHqHere()
        {
            Calls.Add("arthur-hq-here");
            return Result;
        }

        public Chapter3QaResult QaArthurVisit(string kind)
        {
            Calls.Add($"arthur-visit:{kind}");
            return Result;
        }

        public Chapter3QaResult QaArthurBeat()
        {
            Calls.Add("arthur-beat");
            return Result;
        }

        public IReadOnlyList<string> QaBeatSnapshot()
        {
            Calls.Add("beat-snapshot");
            return new[] { "snapshot" };
        }

        public IReadOnlyList<string> QaBeatInvincible(bool on)
        {
            Calls.Add($"beat-invincible:{on}");
            return new[] { "invincible" };
        }

        public Chapter3QaResult QaDemandNow()
        {
            Calls.Add("demand-now");
            return new Chapter3QaResult(true, "now");
        }

        public IReadOnlyList<string> QaDemandEligible()
        {
            Calls.Add("demand-eligible");
            return new[] { "eligible" };
        }

        public IReadOnlyList<string> QaDemandFresh()
        {
            Calls.Add("demand-fresh");
            return new[] { "fresh" };
        }

        public Chapter3QaResult QaDemandExpire()
        {
            Calls.Add("demand-expire");
            return new Chapter3QaResult(true, "expire");
        }

        public Chapter3QaResult QaDemandFreshAdd(int units)
        {
            Calls.Add($"demand-fresh-add:{units}");
            return new Chapter3QaResult(true, "fresh-add");
        }

        public Chapter3QaResult QaDemandEligibleAdd(string productId)
        {
            Calls.Add($"demand-eligible-add:{productId}");
            return new Chapter3QaResult(true, "eligible-add");
        }

        public IReadOnlyList<string> QaBeatAnim(string? trigger)
        {
            Calls.Add($"beat-anim:{trigger ?? "<list>"}");
            return new[] { "anim" };
        }

        public IReadOnlyList<string> QaCapacityStatus()
        {
            Calls.Add("capacity");
            return new[] { "capacity" };
        }

        public Chapter3QaResult QaCapacityUnlock()
        {
            Calls.Add("capacity-unlock");
            return new Chapter3QaResult(true, "unlock");
        }

        public Release1ChiefForcedWantedSnapshot? ForcedWantedSnapshot { get; set; }

        public Chapter3QaResult QaLockdownAge(double hours)
        {
            Calls.Add($"lockdown-age:{hours}");
            return Result;
        }

        public Release1ChiefForcedWantedSnapshot? QaForcedWantedSnapshot()
        {
            Calls.Add("wanted-snapshot");
            return ForcedWantedSnapshot;
        }

        public Release1ChiefProtectionSnapshot? ProtectionSnapshot { get; set; }

        public Chapter3QaResult QaProtectionOffer()
        {
            Calls.Add("protection-offer");
            return Result;
        }

        public Chapter3QaResult QaProtectionLapse()
        {
            Calls.Add("protection-lapse");
            return Result;
        }

        public Release1ChiefProtectionSnapshot? QaProtectionSnapshot()
        {
            Calls.Add("protection-snapshot");
            return ProtectionSnapshot;
        }

        public Chapter3QaResult QaCapacitySet(int tier)
        {
            Calls.Add($"capacity-set:{tier}");
            return new Chapter3QaResult(true, "set");
        }

        public IReadOnlyList<string> OfficersStatusLines { get; set; } = new[] { "officers: none" };

        public IReadOnlyList<string> QaOfficersStatus()
        {
            Calls.Add("officers-status");
            return OfficersStatusLines;
        }

        public Chapter3QaResult QaOfficersSpawn(int count)
        {
            Calls.Add($"officers-spawn:{count}");
            return Result;
        }

        public Chapter3QaResult QaOfficersClear()
        {
            Calls.Add("officers-clear");
            return Result;
        }

        public IReadOnlyList<string> CheckpointStatusLines { get; set; } = new[] { "checkpoints: none" };

        public IReadOnlyList<string> QaCheckpointStatus()
        {
            Calls.Add("checkpoint-status");
            return CheckpointStatusLines;
        }

        public Chapter3QaResult QaCheckpointSpotHere(string label)
        {
            Calls.Add($"checkpoint-spot-here:{label}");
            return Result;
        }

        public Chapter3QaResult QaCheckpointTest(string label)
        {
            Calls.Add($"checkpoint-test:{label}");
            return Result;
        }

        public Chapter3QaResult QaCheckpointClear()
        {
            Calls.Add("checkpoint-clear");
            return Result;
        }

        public Chapter3QaResult QaCheckpointPick()
        {
            Calls.Add("checkpoint-pick");
            return Result;
        }
    }

    [Fact]
    public void Arthur_come_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "coming") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "come" }, qaEnabled: true);

        Assert.Equal(new[] { "coming" }, lines);
        Assert.Equal(new[] { "arthur-come" }, target.Calls);
    }

    [Fact]
    public void Arthur_say_joins_the_text_and_forwards_it()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "said") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "say", "Nice", "place", "you", "got", "here." }, qaEnabled: true);

        Assert.Equal(new[] { "said" }, lines);
        Assert.Equal(new[] { "arthur-say:Nice place you got here." }, target.Calls);
    }

    [Fact]
    public void Arthur_say_without_text_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "say" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 arthur say <text>" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Arthur_leave_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "leaving") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "ARTHUR", "LEAVE" }, qaEnabled: true);

        Assert.Equal(new[] { "leaving" }, lines);
        Assert.Equal(new[] { "arthur-leave" }, target.Calls);
    }

    [Fact]
    public void Arthur_status_returns_the_target_lines()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "status" }, qaEnabled: true);

        Assert.Equal(new[] { "arthur: parked" }, lines);
        Assert.Equal(new[] { "arthur-status" }, target.Calls);
    }

    [Fact]
    public void Arthur_without_a_subcommand_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 arthur come | say <text> | leave | status | hq <on | off | auto | here> | visit <warn | coop | beat | call> | beat" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Arthur_with_an_unknown_subcommand_names_it()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "arthur", "dance" }, qaEnabled: true);

        Assert.Equal(new[] { "Unknown oc_ch3 arthur subcommand: 'dance'." }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Lockdown_age_forwards_the_hours()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "lockdown", "age", "24" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "lockdown-age:24" }, target.Calls);
    }

    [Fact]
    public void Lockdown_without_age_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "lockdown" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 lockdown age <hours>" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Lockdown_age_without_a_number_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "lockdown", "age", "soon" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 lockdown age <hours>" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Wanted_status_shows_every_input()
    {
        var target = new FakeTarget
        {
            ForcedWantedSnapshot = new Release1ChiefForcedWantedSnapshot(
                HasRecord: true, LockdownEngaged: true, LockdownAgeHours: 37.5, Warned: true,
                CurfewActive: true, Outdoors: true, Protected: false, PursuitLevel: 0,
                LastNightFired: 1, Fires: 1, Dispatches: 1, Refusals: 0,
                Armed: true, Held: false, TimeSinceSighted: 12.5f, SearchTime: 30f, Arrested: false)
        };

        var lines = Chapter3QaCommand.Execute(target, new[] { "wanted", "status" }, qaEnabled: true);

        Assert.Equal("wanted: lockdown engaged (age 37.5 h), warned yes", lines[0]);
        Assert.Equal("wanted: curfew yes, outdoors yes, protected no, pursuit None", lines[1]);
        Assert.Equal("wanted: armed yes, held no, sighted 12.5/30.0 s, arrested no", lines[2]);
        Assert.Equal("wanted: last night fired (info) night 1, fires 1, dispatched 1, refused 0", lines[3]);
    }

    [Fact]
    public void Wanted_status_shows_unknown_hold_values()
    {
        var target = new FakeTarget
        {
            ForcedWantedSnapshot = new Release1ChiefForcedWantedSnapshot(
                HasRecord: true, LockdownEngaged: true, LockdownAgeHours: null, Warned: false,
                CurfewActive: null, Outdoors: null, Protected: false, PursuitLevel: null,
                LastNightFired: null, Fires: 0, Dispatches: 0, Refusals: 0,
                Armed: false, Held: false, TimeSinceSighted: null, SearchTime: null, Arrested: null)
        };

        var lines = Chapter3QaCommand.Execute(target, new[] { "wanted", "status" }, qaEnabled: true);

        Assert.Equal("wanted: lockdown engaged (age unknown), warned no", lines[0]);
        Assert.Equal("wanted: curfew unknown, outdoors unknown, protected no, pursuit unknown", lines[1]);
        Assert.Equal("wanted: armed no, held no, sighted unknown, arrested unknown", lines[2]);
        Assert.Equal("wanted: last night fired (info) never, fires 0, dispatched 0, refused 0", lines[3]);
    }

    [Fact]
    public void Wanted_status_without_a_snapshot_says_unwired()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "wanted", "status" }, qaEnabled: true);

        Assert.Equal(new[] { "wanted: the Chief service is not wired." }, lines);
    }

    [Fact]
    public void Wanted_without_status_prints_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "wanted" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 wanted status" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Protection_offer_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "protection", "offer" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "protection-offer" }, target.Calls);
    }

    [Fact]
    public void Protection_lapse_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "protection", "lapse" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "protection-lapse" }, target.Calls);
    }

    [Fact]
    public void Protection_status_shows_offer_until_chapter_cover_and_counters()
    {
        // This test never touches the shared gate state: exact counter values belong to the
        // gate tests' class, and racing them across xUnit's parallel classes flakes. Here the
        // counters line only has to be present and shaped; the snapshot line is exact.
        var state = Chapter3State.ProcessDayTick(Chapter3State.CreateInitial("76561190000000001"), 11, envelopeSatisfied: true).State;
        var target = new FakeTarget
        {
            State = state,
            ProtectionSnapshot = new Release1ChiefProtectionSnapshot(Offered: true, ProtectedUntilDay: 19, CoveredNow: true)
        };

        var lines = Chapter3QaCommand.Execute(target, new[] { "protection", "status" }, qaEnabled: true);

        var chapterDay = state.ChapterDayAt(19);
        Assert.Equal($"protection: offered yes, until day 19 (chapter {chapterDay}), covered now yes", lines[0]);
        Assert.StartsWith("gates:", lines[1]);
        Assert.Contains("calls dropped", lines[1]);
        Assert.Contains("foot", lines[1]);
        Assert.Contains("vehicle", lines[1]);
        Assert.Contains("sentries skipped", lines[1]);
        Assert.Contains("checkpoints blocked", lines[1]);
    }

    [Fact]
    public void Protection_status_without_a_snapshot_says_unwired()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "protection", "status" }, qaEnabled: true);

        Assert.Equal("protection: the Chief service is not wired.", lines[0]);
        Assert.StartsWith("gates:", lines[1]);
    }

    [Fact]
    public void Officers_status_returns_the_target_lines()
    {
        var target = new FakeTarget { OfficersStatusLines = new[] { "OC officers: 1/8" } };

        var lines = Chapter3QaCommand.Execute(target, new[] { "officers", "status" }, qaEnabled: true);

        Assert.Equal(new[] { "OC officers: 1/8" }, lines);
        Assert.Equal(new[] { "officers-status" }, target.Calls);
    }

    [Fact]
    public void Officers_spawn_forwards_the_count()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "officers", "spawn", "3" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "officers-spawn:3" }, target.Calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("many")]
    public void Officers_spawn_rejects_out_of_range_counts(string count)
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "officers", "spawn", count }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 officers spawn <1-8>" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Officers_clear_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "officers", "clear" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "officers-clear" }, target.Calls);
    }

    [Fact]
    public void Officers_without_a_subcommand_shows_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "officers" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 officers status | spawn <1-8> | clear" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Officers_with_an_unknown_subcommand_names_it()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "officers", "promote" }, qaEnabled: true);

        Assert.Equal(new[] { "Unknown oc_ch3 officers subcommand: 'promote'." }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Checkpoint_status_returns_the_target_lines()
    {
        var target = new FakeTarget { CheckpointStatusLines = new[] { "checkpoints: 1/8" } };

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "status" }, qaEnabled: true);

        Assert.Equal(new[] { "checkpoints: 1/8" }, lines);
        Assert.Equal(new[] { "checkpoint-status" }, target.Calls);
    }

    [Fact]
    public void Checkpoint_spot_here_forwards_the_label()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "spot", "here", "outside-x" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "checkpoint-spot-here:outside-x" }, target.Calls);
    }

    [Theory]
    [InlineData("checkpoint", "spot")]
    [InlineData("checkpoint", "spot", "here")]
    [InlineData("checkpoint", "spot", "there", "outside-x")]
    public void Checkpoint_spot_here_without_a_label_shows_usage(params string[] args)
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, args, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 checkpoint spot here <label>" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Checkpoint_test_forwards_the_label()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "test", "outside-barn" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "checkpoint-test:outside-barn" }, target.Calls);
    }

    [Fact]
    public void Checkpoint_test_without_a_label_shows_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "test" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 checkpoint test <label>" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Checkpoint_clear_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "clear" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "checkpoint-clear" }, target.Calls);
    }

    [Fact]
    public void Checkpoint_pick_forwards()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "pick" }, qaEnabled: true);

        Assert.Equal(new[] { "ok" }, lines);
        Assert.Equal(new[] { "checkpoint-pick" }, target.Calls);
    }

    [Fact]
    public void Checkpoint_without_a_subcommand_shows_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 checkpoint status | spot here <label> | test <label> | clear | pick" }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Checkpoint_with_an_unknown_subcommand_names_it()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "checkpoint", "raid" }, qaEnabled: true);

        Assert.Equal(new[] { "Unknown oc_ch3 checkpoint subcommand: 'raid'." }, lines);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void Protection_without_a_subcommand_shows_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "protection" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 protection offer | status | lapse" }, lines);
        Assert.Empty(target.Calls);
    }

}
