using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PresentationPlannerTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void No_messages_before_the_start()
    {
        var plan = Chapter3PresentationPlanner.Build(Chapter3State.CreateInitial(PlayerId), release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Yields_to_release1_decision()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 1, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: true, warehouseOwned: false);

        Assert.Null(plan.Decision);
        Assert.Single(plan.Messages);
    }

    [Fact]
    public void Offer_sends_text_with_accept_decline()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 3, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(
            "Got work if you want it: a dead drop delivery. Pays $15,000. You get 2 days once you say yes. Tell me by tomorrow, don't make me fucking chase you.",
            plan.Decision!.Prompt);
        Assert.Equal(new[] { "Accept", "Decline" }, plan.Decision!.Options.Select(o => o.Label).ToArray());
        Assert.Equal(
            new Chapter3PresentationCommand[] { new Chapter3AcceptCommand(), new Chapter3DeclineCommand() },
            plan.Decision.Options.Select(o => o.Command).ToArray());
        Assert.Equal("oc11-ch3/v1/76561190000000001/offer-3", plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.AnswerRepromptText, plan.Decision.RepromptText);
    }

    [Fact]
    public void Emergency_offer_names_the_product_with_accept_decline()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 5, Chapter3JobType.EmergencySupply, reward: 15_500, offeredDay: 9);
        var offer = state.JobBoard.OpenOffer! with { ProductId = "rich", ProductName = "Rich" };
        state = state with { JobBoard = state.JobBoard with { OpenOffer = offer } };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(
            "The family needs 2 bricks of Rich, and we don't make that shit ourselves. No pay on this one. It comes back to you, trust me. You get 12 hours once you say yes.",
            plan.Decision!.Prompt);
        Assert.Equal(new[] { "Accept", "Decline" }, plan.Decision!.Options.Select(o => o.Label).ToArray());
        Assert.Equal("oc11-ch3/v1/76561190000000001/offer-5", plan.Decision.PromptCorrelationId);
    }

    [Fact]
    public void Emergency_offer_without_a_product_names_the_product()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 5, Chapter3JobType.EmergencySupply, reward: 15_500, offeredDay: 9);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            "The family needs 2 bricks of the product, and we don't make that shit ourselves. No pay on this one. It comes back to you, trust me. You get 12 hours once you say yes.",
            plan.Decision!.Prompt);
    }

    [Fact]
    public void Emergency_accept_sends_the_frozen_terms_without_pay()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 1, Chapter3JobType.EmergencySupply, reward: 15_000, offeredDay: 0);
        var accepted = Chapter3JobScheduler.Accept(state.JobBoard, 1, chapterDay: 0);
        var job = accepted.Board.ActiveJob! with
        {
            Delivery = new Chapter3DeadDropDelivery(
                "drop-a", "Drop A", 1d, 2d, 3d, "rich", "Rich", "brick", "Brick", 2),
            AcceptedGameMinutes = 1_000d,
            ExpiresGameMinutes = 1_720d
        };
        state = state with { JobBoard = accepted.Board with { ActiveJob = job } };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "2 bricks of Rich at Drop A. Twelve hours. Move your ass." },
            plan.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Emergency_completion_says_the_family_wont_forget()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with
            {
                OpenOffer = null,
                ActiveJob = null,
                LastCompletedJob = new Chapter3JobCompletion(1, Chapter3JobType.EmergencySupply, 17_000)
            }
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "Got it. The family won't forget that." },
            plan.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Warehouse_offer_sends_text_with_the_buy_button()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(
            "You've been pulling your weight. There's a warehouse on the docks, right next to the Docks Warehouse. $300,000 and it's fucking yours. No rush, this one stays open, and I'll check in every week.",
            plan.Decision!.Prompt);
        var option = Assert.Single(plan.Decision!.Options);
        Assert.Equal("Buy the warehouse ($300,000)", option.Label);
        Assert.IsType<Chapter3BuyWarehouseCommand>(option.Command);
        Assert.Equal($"oc11-ch3/v1/{PlayerId}/warehouse-offer", plan.Decision.PromptCorrelationId);
    }

    [Fact]
    public void Warehouse_button_price_matches_the_payment_tuning()
    {
        Assert.Equal(300_000, Chapter3PaymentTuning.WarehousePrice);
        Assert.Equal(
            $"Buy the warehouse (${Chapter3PaymentTuning.WarehousePrice:N0})",
            Chapter3NellCopy.BuyWarehouseLabel(Chapter3PaymentTuning.WarehousePrice));
    }

    [Fact]
    public void Warehouse_button_coexists_with_job_buttons_after_them()
    {
        var state = ActiveState();
        state = state with { WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18) };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.NotNull(plan.Decision);
        Assert.Equal(
            new[] { "Accept", "Decline", "Buy the warehouse ($300,000)" },
            plan.Decision!.Options.Select(o => o.Label).ToArray());
        // The expiring job offer wins the prompt; the standing warehouse
        // offer reaches the player as a message.
        Assert.Contains(Chapter3NellCopy.WarehouseOfferText, plan.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Warehouse_button_hides_while_a_make_good_is_open_and_returns_when_paid()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_000, openedDay: 4, dueDay: 6, Chapter3MakeGoodStatus.Open) }, 4, 2),
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18)
        };

        var owing = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain(
            owing.Decision!.Options, o => o.Command is Chapter3BuyWarehouseCommand);

        var cleared = state with
        {
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_000, openedDay: 4, dueDay: 6, Chapter3MakeGoodStatus.Paid) }, 4, 2)
        };
        var paid = Chapter3PresentationPlanner.Build(cleared, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(paid.Decision!.Options, o => o.Command is Chapter3BuyWarehouseCommand);
    }

    [Fact]
    public void Warehouse_reminder_becomes_the_prompt_by_week()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, LastReminderWeek: 2)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            "That warehouse is still sitting there. $300,000 whenever you're ready.",
            plan.Decision!.Prompt);
        Assert.Equal($"oc11-ch3/v1/{PlayerId}/warehouse-reminder-2", plan.Decision.PromptCorrelationId);
        Assert.Contains(Chapter3NellCopy.WarehouseOfferText, plan.Messages.Select(m => m.Text));
        Assert.Single(plan.Messages, m => m.Text == Chapter3NellCopy.WarehouseReminderText);
    }

    [Fact]
    public void Warehouse_button_fits_the_cap_after_the_job_buttons()
    {
        var state = ActiveState();
        state = state with
        {
            Cuts = new Chapter3WeeklyCuts(
                new[] { new Chapter3WeeklyCut(2, 30_000, 0, 2, Chapter3WeeklyCutStatus.Due) }, 0),
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "Pay $30,000", "Accept", "Decline", "Buy the warehouse ($300,000)" },
            plan.Decision!.Options.Select(o => o.Label).ToArray());
    }

    [Fact]
    public void Warehouse_button_comes_before_buy_time()
    {
        var state = AcceptOffer(ActiveState(), offerId: 1, jobId: 1);
        state = state with
        {
            JobBoard = state.JobBoard with { SuccessfulJobs = 3 },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "Buy the warehouse ($300,000)", "Buy more time ($3,750)" },
            plan.Decision!.Options.Select(o => o.Label).ToArray());
    }

    [Fact]
    public void Warehouse_bought_sends_the_keys_text_and_drops_the_button()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, Purchased: true, PurchasedDay: 20)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "Keys are yours. Place'll be ready in the morning. Welcome to the fucking big leagues. The family takes $30,000 a week, and don't make me chase it." },
            plan.Messages.Select(m => m.Text).ToArray());
        Assert.True(plan.Decision is null || !plan.Decision.Options.Any(o => o.Command is Chapter3BuyWarehouseCommand));
    }

    [Fact]
    public void Warehouse_built_sends_the_ready_text_once_owned()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, Purchased: true, PurchasedDay: 20)
        };

        var pending = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);
        var built = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: true);

        Assert.DoesNotContain(
            "Your place is ready. Go take a look, and don't fuck it up.",
            pending.Messages.Select(m => m.Text));
        Assert.Contains(
            "Your place is ready. Go take a look, and don't fuck it up.",
            built.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Warehouse_fell_through_sends_the_line_and_returns_the_button()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, FellThroughCount: 2)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(2, plan.Messages.Count(m => m.Text == "Deal fell through. Your money's back. I'll sort it out."));
        Assert.Contains(plan.Decision!.Options, o => o.Command is Chapter3BuyWarehouseCommand);
    }

    [Fact]
    public void Warehouse_not_ready_sends_the_hold_line_and_keeps_the_button()
    {
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null },
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, NotReadyCount: 1)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(
            "Hold on, the place isn't ready. Try me again in a minute.",
            plan.Messages.Select(m => m.Text));
        Assert.Contains(plan.Decision!.Options, o => o.Command is Chapter3BuyWarehouseCommand);
    }

    [Fact]
    public void Emergency_job_offers_no_buy_time_button()
    {
        var job = new Chapter3ActiveJob(2, 1, Chapter3JobType.EmergencySupply, 15_000, AcceptedDay: 0, DeadlineDay: 2, ExtensionUsed: false,
            new Chapter3DeadDropDelivery("drop-a", "Drop A", 1d, 2d, 3d, "rich", "Rich", "brick", "Brick", 2),
            null, 1_000d, 1_720d);
        var state = ActiveState();
        state = state with
        {
            JobBoard = state.JobBoard with { OpenOffer = null, ActiveJob = job, SuccessfulJobs = 3 }
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Receipted_offer_keeps_the_subject_prompt_with_an_answer_reprompt()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 3, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/offer-3");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(
            "Got work if you want it: a dead drop delivery. Pays $15,000. You get 2 days once you say yes. Tell me by tomorrow, don't make me fucking chase you.",
            plan.Decision!.Prompt);
        Assert.Equal("oc11-ch3/v1/76561190000000001/offer-3", plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.AnswerRepromptText, plan.Decision.RepromptText);
    }

    [Fact]
    public void Prompt_stays_on_the_offer_while_a_make_good_queues_as_a_message()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 1, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);
        state = state with
        {
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_500, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Open) }, 0, 2)
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/offer-1");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            "Got work if you want it: a dead drop delivery. Pays $15,000. You get 2 days once you say yes. Tell me by tomorrow, don't make me fucking chase you.",
            plan.Decision!.Prompt);
        Assert.Equal(new[] { "Pay $17,500", "Accept", "Decline" }, plan.Decision.Options.Select(o => o.Label).ToArray());
        Assert.Equal(Chapter3NellCopy.PayRepromptText, plan.Decision.RepromptText);
        Assert.Equal(
            new[] { "That dead drop delivery went to shit. The family wants $17,500 within 2 days. Don't make them ask twice." },
            plan.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Accepted_job_sends_acknowledgement()
    {
        var state = ActiveState();
        state = AcceptOffer(state, offerId: 1, jobId: 2);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(new[] { "Good. I'll send the details." }, plan.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Declined_offer_sends_your_call()
    {
        var state = ActiveState();
        state = DeclineOffer(state, offerId: 1);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(new[] { "Your fucking call." }, plan.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Expired_offer_sends_taken_as_no()
    {
        var state = ActiveState();
        state = ExpireOffer(state, offerId: 1);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(new[] { "No answer? I'll take that as a fuck no." }, plan.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Nell_hint_sends_once()
    {
        var state = ActiveState() with
        {
            JobBoard = ActiveState().JobBoard with { OpenOffer = null, NellHintIssued = true }
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(new[] { "You keep passing. People are starting to fucking notice." }, plan.Messages.Select(m => m.Text).ToArray());
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Trust_messages_fire_at_three_and_five_successes()
    {
        var three = ActiveState() with { JobBoard = ActiveState().JobBoard with { SuccessfulJobs = 3 } };
        var five = ActiveState() with { JobBoard = ActiveState().JobBoard with { SuccessfulJobs = 5 } };

        Assert.Equal(
            new[] { "Look at you. The family's starting to know your name." },
            Chapter3PresentationPlanner.Build(three, release1HasNellDecision: false, warehouseOwned: false).Messages.Select(m => m.Text).ToArray());
        Assert.Equal(
            new[]
            {
                "Look at you. The family's starting to know your name.",
                "The family's noticed. Don't let it go to your fucking head."
            },
            Chapter3PresentationPlanner.Build(five, release1HasNellDecision: false, warehouseOwned: false).Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Make_good_opened_is_the_prompt_with_a_pay_button()
    {
        var state = ActiveState() with
        {
            JobBoard = ActiveState().JobBoard with { OpenOffer = null },
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_500, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Open) }, 0, 2)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(
            "That dead drop delivery went to shit. The family wants $17,500 within 2 days. Don't make them ask twice.",
            plan.Decision!.Prompt);
        Assert.Equal("oc11-ch3/v1/76561190000000001/mg-1-opened", plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.PayRepromptText, plan.Decision.RepromptText);
        var option = Assert.Single(plan.Decision.Options);
        Assert.Equal("Pay $17,500", option.Label);
        Assert.Equal(new Chapter3PayMakeGoodCommand(1), option.Command);
    }

    [Fact]
    public void Make_good_overdue_text_replaces_the_opened_prompt()
    {
        var state = ActiveState() with
        {
            JobBoard = ActiveState().JobBoard with { OpenOffer = null },
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_500, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Overdue) }, 3, 2)
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/mg-1-opened");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.Equal("You're late. Arthur's been told, and he is not a patient man.", plan.Decision!.Prompt);
        Assert.Equal("oc11-ch3/v1/76561190000000001/mg-1-overdue", plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.PayRepromptText, plan.Decision.RepromptText);
    }

    [Fact]
    public void Cut_due_is_the_prompt_with_a_pay_button()
    {
        var state = ActiveState() with
        {
            JobBoard = ActiveState().JobBoard with { OpenOffer = null },
            Cuts = new Chapter3WeeklyCuts(
                new[] { new Chapter3WeeklyCut(2, 30_000, 14, 16, Chapter3WeeklyCutStatus.Due) }, 14)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.Equal("Family's cut this week: $30,000. Pay up.", plan.Decision!.Prompt);
        Assert.Equal("oc11-ch3/v1/76561190000000001/cut-2-due", plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.PayRepromptText, plan.Decision.RepromptText);
        var option = Assert.Single(plan.Decision.Options);
        Assert.Equal("Pay $30,000", option.Label);
        Assert.Equal(new Chapter3PayCutCommand(2), option.Command);
    }

    [Fact]
    public void Paid_debts_send_received()
    {
        var state = ActiveState() with
        {
            JobBoard = ActiveState().JobBoard with { OpenOffer = null },
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_500, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Paid) }, 0, 2),
            Cuts = new Chapter3WeeklyCuts(
                new[] { new Chapter3WeeklyCut(2, 30_000, 14, 16, Chapter3WeeklyCutStatus.Paid) }, 14)
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/mg-1-opened");
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/cut-2-due");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(new[] { "Received.", "Received." }, plan.Messages.Select(m => m.Text).ToArray());
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Each_shortfall_sends_youre_short()
    {
        var state = ActiveState() with { PaymentShortfallCount = 2 };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/short-1");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        var message = Assert.Single(plan.Messages);
        Assert.Equal("You're short. Don't waste my fucking time.", message.Text);
        Assert.Equal("oc11-ch3/v1/76561190000000001/short-2", message.CorrelationId);
    }

    [Fact]
    public void Deadline_close_carries_the_buy_button()
    {
        var job = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 15_000, AcceptedDay: 0, DeadlineDay: 2, ExtensionUsed: false);
        var state = ActiveState() with
        {
            LastProcessedElapsedDay = 12,
            JobBoard = ActiveState().JobBoard with { OpenOffer = null, ActiveJob = job, SuccessfulJobs = 3 }
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/job-1-accepted");
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/trust-3");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.Equal("Clock's running on that job. Move your ass.", plan.Decision!.Prompt);
        Assert.Equal("oc11-ch3/v1/76561190000000001/job-1-deadline-close", plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.BuyTimeRepromptText, plan.Decision.RepromptText);
        var option = Assert.Single(plan.Decision.Options);
        Assert.Equal("Buy more time ($3,750)", option.Label);
        Assert.Equal(new Chapter3BuyTimeCommand(), option.Command);
    }

    [Fact]
    public void Buy_button_without_a_close_deadline_reprompts_with_no_scope()
    {
        var job = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 15_000, AcceptedDay: 0, DeadlineDay: 10, ExtensionUsed: false);
        var state = ActiveState() with
        {
            LastProcessedElapsedDay = 12,
            JobBoard = ActiveState().JobBoard with { OpenOffer = null, ActiveJob = job, SuccessfulJobs = 3 }
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/job-1-accepted");
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/trust-3");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(Chapter3NellCopy.BuyTimeRepromptText, plan.Decision!.Prompt);
        Assert.Null(plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.BuyTimeRepromptText, plan.Decision.RepromptText);
        var option = Assert.Single(plan.Decision.Options);
        Assert.Equal(new Chapter3BuyTimeCommand(), option.Command);
    }

    [Fact]
    public void Buy_button_hides_until_the_third_success()
    {
        var job = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 15_000, AcceptedDay: 0, DeadlineDay: 2, ExtensionUsed: false);
        var state = ActiveState() with
        {
            LastProcessedElapsedDay = 12,
            JobBoard = ActiveState().JobBoard with { OpenOffer = null, ActiveJob = job, SuccessfulJobs = 2 }
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/job-1-accepted");
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/trust-3");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(new[] { "Clock's running on that job. Move your ass." }, plan.Messages.Select(m => m.Text).ToArray());
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Extension_bought_sends_text_and_hides_buy()
    {
        var job = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 15_000, AcceptedDay: 0, DeadlineDay: 3, ExtensionUsed: true);
        var state = ActiveState() with
        {
            LastProcessedElapsedDay = 12,
            JobBoard = ActiveState().JobBoard with { OpenOffer = null, ActiveJob = job, SuccessfulJobs = 3 }
        };
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/job-1-accepted");
        state = WithReceipt(state, "oc11-ch3/v1/76561190000000001/trust-3");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "Fine. One more day. Don't make a fucking habit of it." },
            plan.Messages.Select(m => m.Text).ToArray());
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Buttons_order_pay_make_good_pay_cut_accept_decline()
    {
        var state = ActiveState();
        state = WithOffer(state, offerId: 1, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);
        state = state with
        {
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_500, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Open) }, 0, 2),
            Cuts = new Chapter3WeeklyCuts(
                new[] { new Chapter3WeeklyCut(2, 30_000, 0, 2, Chapter3WeeklyCutStatus.Due) }, 0)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(
            new[] { "Pay $17,500", "Pay $30,000", "Accept", "Decline" },
            plan.Decision!.Options.Select(o => o.Label).ToArray());
        Assert.StartsWith("ch3-nell:", plan.Decision.Id);
    }

    [Fact]
    public void Five_options_cap_at_four_with_buy_dropped()
    {
        var job = new Chapter3ActiveJob(2, 1, Chapter3JobType.DeadDrop, 15_000, AcceptedDay: 0, DeadlineDay: 2, ExtensionUsed: false);
        var state = ActiveState();
        state = WithOffer(state, offerId: 3, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);
        state = state with
        {
            JobBoard = state.JobBoard with { ActiveJob = job, SuccessfulJobs = 3 },
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 17_500, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Open) }, 0, 2),
            Cuts = new Chapter3WeeklyCuts(
                new[] { new Chapter3WeeklyCut(2, 30_000, 0, 2, Chapter3WeeklyCutStatus.Due) }, 0)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Equal(4, plan.Decision!.Options.Count);
        Assert.DoesNotContain(plan.Decision.Options, o => o.Command is Chapter3BuyTimeCommand);
    }

    [Fact]
    public void Most_urgent_make_good_prefers_overdue_then_earliest_due()
    {
        var open = new Chapter3MakeGoodLedger(
            new[]
            {
                MakeGood(1, 17_500, openedDay: 0, dueDay: 5, Chapter3MakeGoodStatus.Open),
                MakeGood(2, 17_500, openedDay: 0, dueDay: 3, Chapter3MakeGoodStatus.Open)
            }, 0, 3);
        var plan = Chapter3PresentationPlanner.Build(ActiveState() with { JobBoard = ActiveState().JobBoard with { OpenOffer = null }, Ledger = open }, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Equal(new Chapter3PayMakeGoodCommand(2), Assert.Single(plan.Decision!.Options).Command);

        var overdue = new Chapter3MakeGoodLedger(
            new[]
            {
                MakeGood(1, 17_500, openedDay: 0, dueDay: 5, Chapter3MakeGoodStatus.Overdue),
                MakeGood(2, 17_500, openedDay: 0, dueDay: 3, Chapter3MakeGoodStatus.Open)
            }, 6, 3);
        plan = Chapter3PresentationPlanner.Build(ActiveState() with { JobBoard = ActiveState().JobBoard with { OpenOffer = null }, Ledger = overdue }, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Equal(new Chapter3PayMakeGoodCommand(1), Assert.Single(plan.Decision!.Options).Command);
    }

    [Fact]
    public void Identical_pay_labels_keep_the_make_good_only()
    {
        var state = ActiveState() with
        {
            JobBoard = ActiveState().JobBoard with { OpenOffer = null },
            Ledger = new Chapter3MakeGoodLedger(
                new[] { MakeGood(1, 30_000, openedDay: 0, dueDay: 2, Chapter3MakeGoodStatus.Open) }, 0, 2),
            Cuts = new Chapter3WeeklyCuts(
                new[] { new Chapter3WeeklyCut(2, 30_000, 0, 2, Chapter3WeeklyCutStatus.Due) }, 0)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        var option = Assert.Single(plan.Decision!.Options);
        Assert.Equal("Pay $30,000", option.Label);
        Assert.Equal(new Chapter3PayMakeGoodCommand(1), option.Command);
    }

    [Fact]
    public void Decision_id_is_stable_per_targets()
    {
        var first = ActiveState();
        first = WithOffer(first, offerId: 1, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);
        var second = ActiveState();
        second = WithOffer(second, offerId: 2, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);

        var firstId = Chapter3PresentationPlanner.Build(first, release1HasNellDecision: false, warehouseOwned: false).Decision!.Id;
        var repeatId = Chapter3PresentationPlanner.Build(first, release1HasNellDecision: false, warehouseOwned: false).Decision!.Id;
        var secondId = Chapter3PresentationPlanner.Build(second, release1HasNellDecision: false, warehouseOwned: false).Decision!.Id;

        Assert.Equal(firstId, repeatId);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void DeadDrop_accepted_text_names_bricks_product_and_drop()
    {
        var state = WithDelivery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1), 2, "Best", "Drop A");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(
            "Good. 2 bricks of Best at Drop A. $15,000 when they land. Don't fuck it up.",
            plan.Messages.Select(m => m.Text));
    }

    [Fact]
    public void DeadDrop_accepted_text_falls_back_without_names()
    {
        var state = WithDelivery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1), 3, "", "  ");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(
            "Good. 3 bricks of your best product at the drop. $15,000 when they land. Don't fuck it up.",
            plan.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Unfrozen_accepted_job_keeps_the_generic_text()
    {
        var state = AcceptOffer(ActiveState(), offerId: 1, jobId: 1);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(Chapter3NellCopy.AcceptedText, plan.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Completed_job_sends_they_landed_once()
    {
        var state = AcceptOffer(ActiveState(), offerId: 1, jobId: 1);
        var job = state.JobBoard.ActiveJob!;
        var completed = Chapter3JobScheduler.Complete(state.JobBoard, job.JobId, succeeded: true, chapterDay: 1);
        state = state with { JobBoard = completed.Board };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains("They landed. $15,000's yours. Not bad.", plan.Messages.Select(m => m.Text));

        var receipted = WithReceipt(state, $"oc11-ch3/v1/{PlayerId}/job-1-completed");
        var again = Chapter3PresentationPlanner.Build(receipted, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain("They landed. $15,000's yours. Not bad.", again.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Refused_offer_sends_cant_accept_once()
    {
        var state = WithOffer(ActiveState(), offerId: 1, Chapter3JobType.DeadDrop, 15_000, 0);
        state = state with { JobBoard = state.JobBoard with { LastRefusedOfferId = 1 } };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains("You've got nothing I can move right now.", plan.Messages.Select(m => m.Text));

        var receipted = WithReceipt(state, $"oc11-ch3/v1/{PlayerId}/offer-1-refused");
        var again = Chapter3PresentationPlanner.Build(receipted, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain("You've got nothing I can move right now.", again.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Stale_refusal_marker_sends_nothing()
    {
        var state = WithOffer(ActiveState(), offerId: 2, Chapter3JobType.DeadDrop, 15_000, 0);
        state = state with { JobBoard = state.JobBoard with { LastRefusedOfferId = 1 } };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain("You've got nothing I can move right now.", plan.Messages.Select(m => m.Text));
    }

    private static Chapter3State WithDelivery(
        Chapter3State state, int bricks, string productName, string dropName,
        double x = 1d, double y = 2d, double z = 3d)
    {
        var job = state.JobBoard.ActiveJob!;
        return state with
        {
            JobBoard = state.JobBoard with
            {
                ActiveJob = job with
                {
                    Delivery = new Chapter3DeadDropDelivery(
                        "drop-a", dropName, x, y, z, "best", productName, "brick", "Brick", bricks)
                }
            }
        };
    }

    private static Chapter3State WithRecovery(
        Chapter3State state, bool custody = false, double handoffX = 4d)
    {
        var job = state.JobBoard.ActiveJob!;
        return state with
        {
            JobBoard = state.JobBoard with
            {
                ActiveJob = job with
                {
                    JobType = Chapter3JobType.PackageRecovery,
                    Recovery = new Chapter3PackageRecovery(
                        new Chapter3RecoveryDrop("drop-a", "Drop A", 1d, 2d, 3d),
                        new Chapter3RecoveryDrop("drop-b", "Drop B", handoffX, 5d, 6d),
                        "best", "Best", "brick", "Brick", 2, 0, true, custody)
                }
            }
        };
    }

    [Fact]
    public void Frozen_job_desires_a_quest_with_the_drop_marker()
    {
        var state = AcceptOffer(ActiveState(), offerId: 1, jobId: 1);
        state = WithDelivery(state, bricks: 2, productName: "Best", dropName: "Drop A");

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.NotNull(plan.Quest);
        Assert.Equal(Chapter3QuestCopy.QuestKey, plan.Quest!.Key);
        Assert.Equal("Family Work", plan.Quest.Title);
        var entry = Assert.Single(plan.Quest.Entries);
        Assert.Equal("Drop 2 bricks of Best at Drop A", entry.Text);
        Assert.Equal(Release1DesiredEntryState.Active, entry.State);
        Assert.Equal(new Release1WorldPoint(1f, 2f, 3f), entry.Marker);
    }

    [Fact]
    public void Quest_entry_falls_back_on_blank_names()
    {
        Assert.Equal(
            "Drop 3 bricks of your best product at the drop",
            Chapter3QuestCopy.DeadDropEntryText(3, "  ", null));
    }

    [Fact]
    public void No_quest_without_an_active_job()
    {
        var state = WithOffer(ActiveState(), offerId: 1, Chapter3JobType.DeadDrop, reward: 15_000, offeredDay: 0);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Null(plan.Quest);
    }

    [Fact]
    public void No_quest_for_an_unfrozen_legacy_job()
    {
        var state = AcceptOffer(ActiveState(), offerId: 1, jobId: 1);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Null(plan.Quest);
    }

    [Fact]
    public void No_quest_when_the_frozen_position_is_not_finite()
    {
        var state = AcceptOffer(ActiveState(), offerId: 1, jobId: 1);
        state = WithDelivery(state, bricks: 2, productName: "Best", dropName: "Drop A", x: double.NaN);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Null(plan.Quest);
    }

    [Fact]
    public void Quest_key_cannot_collide_with_release1_catalog_keys()
    {
        var catalogKeys = new[]
        {
            Release1MissionCatalog.IntroScopeKey,
            Release1MissionCatalog.SmallCourtesy,
            Release1MissionCatalog.WrongAddress,
            Release1MissionCatalog.RoomWithNoName,
            Release1MissionCatalog.ShortNotice,
            Release1MissionCatalog.KeepTheLightsOff,
            Release1MissionCatalog.TheEnvelope,
            Release1MissionCatalog.ChiefCampbell
        };

        Assert.DoesNotContain(Chapter3QuestCopy.QuestKey, catalogKeys);
        Assert.StartsWith("chapter3.", Chapter3QuestCopy.QuestKey);
    }

    [Fact]
    public void Frozen_recovery_desires_two_entries_with_both_markers()
    {
        var state = WithRecovery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1));

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.NotNull(plan.Quest);
        Assert.Equal(Chapter3QuestCopy.QuestKey, plan.Quest!.Key);
        Assert.Equal(2, plan.Quest.Entries.Count);
        Assert.Equal("Pick up the package at Drop A", plan.Quest.Entries[0].Text);
        Assert.Equal(Release1DesiredEntryState.Active, plan.Quest.Entries[0].State);
        Assert.Equal(new Release1WorldPoint(1f, 2f, 3f), plan.Quest.Entries[0].Marker);
        Assert.Equal("Bring it to Drop B", plan.Quest.Entries[1].Text);
        Assert.Equal(Release1DesiredEntryState.Active, plan.Quest.Entries[1].State);
        Assert.Equal(new Release1WorldPoint(4f, 5f, 6f), plan.Quest.Entries[1].Marker);
    }

    [Fact]
    public void Recovery_pickup_entry_completes_at_custody()
    {
        var state = WithRecovery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1), custody: true);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.NotNull(plan.Quest);
        Assert.Equal(Release1DesiredEntryState.Complete, plan.Quest!.Entries[0].State);
        Assert.Equal(Release1DesiredEntryState.Active, plan.Quest.Entries[1].State);
    }

    [Fact]
    public void No_quest_when_a_recovery_position_is_not_finite()
    {
        var state = WithRecovery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1), handoffX: double.NaN);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Null(plan.Quest);
    }

    [Fact]
    public void Recovery_entries_fall_back_on_blank_names()
    {
        Assert.Equal(
            "Pick up the package at the drop",
            Chapter3QuestCopy.RecoveryPickupEntryText("  "));
        Assert.Equal(
            "Bring it to the drop",
            Chapter3QuestCopy.RecoveryHandoffEntryText(null));
    }

    [Fact]
    public void Recovery_accepted_text_names_pickup_handoff_bricks_and_product()
    {
        var state = WithRecovery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1));

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(
            "Some idiot left a package at Drop A: 2 bricks of Best. Get it to Drop B. $15,000 when it lands.",
            plan.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Custody_sends_the_handoff_line_once()
    {
        var state = WithRecovery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1), custody: true);

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains("You've got it. Now get it to Drop B, and don't get fucking cute.", plan.Messages.Select(m => m.Text));

        var receipted = WithReceipt(state, $"oc11-ch3/v1/{PlayerId}/job-1-custody");
        var again = Chapter3PresentationPlanner.Build(receipted, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain("You've got it. Now get it to Drop B, and don't get fucking cute.", again.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Failed_recovery_make_good_names_the_package_recovery()
    {
        var state = ActiveState();
        state = state with
        {
            Ledger = new Chapter3MakeGoodLedger(
                new[]
                {
                    new Chapter3MakeGood(
                        1, new Chapter3FailedJobSource(2, Chapter3JobType.PackageRecovery),
                        15_500, 4, 6, Chapter3MakeGoodStatus.Open, Chapter3MakeGoodStage.None, null, null)
                },
                4, 2)
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(
            "That package recovery went to shit. The family wants $15,500 within 2 days. Don't make them ask twice.",
            plan.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Completed_recovery_sends_it_landed_once()
    {
        var state = WithRecovery(AcceptOffer(ActiveState(), offerId: 1, jobId: 1));
        var job = state.JobBoard.ActiveJob!;
        var completed = Chapter3JobScheduler.Complete(state.JobBoard, job.JobId, succeeded: true, chapterDay: 1);
        state = state with { JobBoard = completed.Board };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains("It landed. $15,000's yours.", plan.Messages.Select(m => m.Text));
    }

    private static Chapter3MakeGood MakeGood(int id, int amount, int openedDay, int dueDay, Chapter3MakeGoodStatus status) =>
        new(id, new Chapter3FailedJobSource(1, Chapter3JobType.DeadDrop), amount, openedDay, dueDay,
            status, Chapter3MakeGoodStage.None, null, null);

    [Fact]
    public void Arthur_warning_followup_and_ran_off_texts_go_once_each()
    {
        var state = ActiveState() with { ArthurWarningFollowups = 1, ArthurRanOffs = 1 };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurWarningFollowupText);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurRanOffText);
    }

    [Fact]
    public void Arthur_followup_receipt_hides_only_that_followup()
    {
        var state = ActiveState() with { ArthurWarningFollowups = 2 };
        state = WithReceipt(state, Chapter3PresentationReceipt.Correlation(PlayerId, "arthur-warning-followup-1"));

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain(plan.Messages, message => message.CorrelationId.EndsWith("arthur-warning-followup-1", StringComparison.Ordinal));
        Assert.Contains(plan.Messages, message => message.CorrelationId.EndsWith("arthur-warning-followup-2", StringComparison.Ordinal));
    }

    private static Chapter3State ActiveState() =>
        Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;

    private static Chapter3State WithOffer(Chapter3State state, int offerId, Chapter3JobType type, int reward, int offeredDay)
    {
        var offer = new Chapter3JobOffer(offerId, type, reward, offeredDay, offeredDay + 1);
        return state with { JobBoard = state.JobBoard with { OpenOffer = offer } };
    }

    private static Chapter3State WithReceipt(Chapter3State state, string correlationId) =>
        state with
        {
            PresentationReceipts = state.PresentationReceipts.Append(
                new Chapter3PresentationReceipt(correlationId, state.Revision + 1)).ToArray()
        };

    private static Chapter3State AcceptOffer(Chapter3State state, int offerId, int jobId)
    {
        var offered = WithOffer(state, offerId, Chapter3JobType.DeadDrop, 15_000, 0);
        var accepted = Chapter3JobScheduler.Accept(offered.JobBoard, offerId, chapterDay: 0);
        return offered with { JobBoard = accepted.Board };
    }

    private static Chapter3State DeclineOffer(Chapter3State state, int offerId)
    {
        var offered = WithOffer(state, offerId, Chapter3JobType.DeadDrop, 15_000, 0);
        var refused = Chapter3JobScheduler.Refuse(offered.JobBoard, offerId, chapterDay: 0);
        return offered with { JobBoard = refused.Board };
    }

    private static Chapter3State ExpireOffer(Chapter3State state, int offerId)
    {
        var offered = WithOffer(state, offerId, Chapter3JobType.DeadDrop, 15_000, 0);
        var board = offered.JobBoard;
        for (var day = 1; day <= 2; day++)
            board = Chapter3JobScheduler.OnDayTick(board, PlayerId, day).Board;
        return offered with { JobBoard = board };
    }
    [Fact]
    public void Beating_text_names_the_total_still_owed()
    {
        var beaten = MakeGood(1, 1_000, 0, 2, Chapter3MakeGoodStatus.Overdue) with { BeatingDeliveredDay = 5 };
        var open = MakeGood(2, 250, 4, 6, Chapter3MakeGoodStatus.Open);
        var state = ActiveState() with { ArthurBeatings = 1 };
        state = state with { Ledger = state.Ledger with { Entries = new[] { beaten, open } } };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingDebtsText(1_250));
    }

    [Fact]
    public void Beating_text_without_a_beating_on_record_is_the_attack_rebuke()
    {
        var state = ActiveState() with { ArthurBeatings = 1 };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingNoDebtText);
    }

    [Fact]
    public void Protection_cover_adds_a_markerless_quest_line_in_chapter_days()
    {
        var state = ActiveState() with { LastProcessedElapsedDay = 12 };
        var chapterDay = state.ChapterDayAt(19);
        Assert.NotNull(chapterDay);

        var plan = Chapter3PresentationPlanner.Build(
            state, release1HasNellDecision: false, warehouseOwned: false,
            protection: new Release1ChiefProtectionSnapshot(Offered: true, ProtectedUntilDay: 19, CoveredNow: true));

        Assert.NotNull(plan.Quest);
        var line = Assert.Single(plan.Quest!.Entries, entry => entry.Text == $"Campbell's protection: until day {chapterDay}");
        Assert.Null(line.Marker);
        Assert.Equal(Release1DesiredEntryState.Active, line.State);
    }

    [Fact]
    public void Protection_line_disappears_on_lapse()
    {
        var state = ActiveState() with { LastProcessedElapsedDay = 19 };

        var plan = Chapter3PresentationPlanner.Build(
            state, release1HasNellDecision: false, warehouseOwned: false,
            protection: new Release1ChiefProtectionSnapshot(Offered: true, ProtectedUntilDay: 19, CoveredNow: false));

        Assert.DoesNotContain(plan.Quest?.Entries ?? Array.Empty<Release1DesiredEntry>(), entry => entry.Text.StartsWith("Campbell's protection", StringComparison.Ordinal));
    }

    [Fact]
    public void No_protection_line_without_a_snapshot()
    {
        var state = ActiveState() with { LastProcessedElapsedDay = 12 };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);

        Assert.DoesNotContain(plan.Quest?.Entries ?? Array.Empty<Release1DesiredEntry>(), entry => entry.Text.StartsWith("Campbell's protection", StringComparison.Ordinal));
    }

}
