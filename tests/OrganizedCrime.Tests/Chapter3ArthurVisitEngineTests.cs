using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurVisitEngineTests
{
    private const string PlayerId = "76561190000000001";
    private const int StartElapsedDay = 11;

    private static Chapter3MakeGoodLedger WarningDueLedger(params int[] amounts)
    {
        var ledger = Chapter3MakeGoodLedger.Empty;
        var jobId = 0;
        foreach (var amount in amounts)
        {
            jobId++;
            ledger = Chapter3MakeGoods.OpenForFailedJob(ledger, jobId, Chapter3JobType.DeadDrop, amount, chapterDay: 0).Ledger;
        }
        return Chapter3MakeGoods.OnDayTick(ledger, chapterDay: 3).Ledger;
    }

    private static Chapter3JobBoard CooperationBoard() =>
        Chapter3JobBoard.Empty with { CooperationVisitIssued = true, RefusalStreak = 5 };

    [Fact]
    public void Evening_visit_starts_when_a_warning_is_due_and_conditions_hold()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;

        harness.PollAt(1f);

        Assert.Contains(harness.Arthur.Mutations, mutation => mutation == "come");
        Assert.Equal(Chapter3ArthurVisitKind.Warning, harness.Service.State!.ArthurVisitPending);
        Assert.Equal(5, harness.Service.State.ArthurVisitLastAttemptedDay);
    }

    [Fact]
    public void Vehicle_blocks_the_visit()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Conditions.OnFoot = false;
        harness.Conditions.Detail = "the player is in a vehicle.";

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, harness.Service.State!.ArthurVisitPending);
    }

    [Fact]
    public void Indoors_blocks_the_visit()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Conditions.Outdoors = false;
        harness.Conditions.Detail = "the player is inside a property.";

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);
    }

    [Fact]
    public void Pursuit_blocks_the_visit()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Conditions.Clear = false;
        harness.Conditions.Detail = "the player is at pursuit level Arresting.";

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);
    }

    [Fact]
    public void Hq_inside_blocks_the_visit()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Presence.Inside = true;

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);
    }

    [Fact]
    public void Unreadable_conditions_block_the_visit()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Conditions.Readable = false;

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);
    }

    [Fact]
    public void Warning_records_every_entry_lines_and_nells_followup()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250, 750), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        Assert.Contains("come", harness.Arthur.Mutations);

        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.PollAt(7f);
        harness.PollAt(11.5f);
        harness.PollAt(16f);

        Assert.Equal(
            new[]
            {
                "Evening. Nell tells me you're behind.",
                "$2,000 to the family. That's what you owe.",
                "Two days. I won't be this polite next time."
            },
            harness.Arthur.SaidTexts);
        var state = harness.Service.State!;
        Assert.All(state.Ledger.Entries, entry => Assert.Equal(5, entry.WarningDeliveredDay));
        Assert.Equal(1, state.ArthurWarningFollowups);
        Assert.Equal(0, state.ArthurRanOffs);
        Assert.Contains("leave", harness.Arthur.Mutations);
        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurWarningFollowupText);
    }

    [Fact]
    public void Running_off_still_counts_and_queues_the_ran_off_text()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        Assert.Single(harness.Arthur.SaidTexts);

        harness.Arthur.Distance = 20f;
        harness.PollAt(4f);

        var state = harness.Service.State!;
        Assert.Equal(5, Assert.Single(state.Ledger.Entries).WarningDeliveredDay);
        Assert.Equal(1, state.ArthurRanOffs);
        Assert.Equal(0, state.ArthurWarningFollowups);
        Assert.Contains("leave", harness.Arthur.Mutations);
        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurRanOffText);
    }

    [Fact]
    public void Vehicle_mid_lines_counts_as_ran_off()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);

        harness.Conditions.OnFoot = false;
        harness.PollAt(4f);

        Assert.Equal(1, harness.Service.State!.ArthurRanOffs);
        Assert.Equal(5, Assert.Single(harness.Service.State.Ledger.Entries).WarningDeliveredDay);
    }

    [Fact]
    public void Paid_before_arrival_cancels_without_recording()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        Assert.Contains("come", harness.Arthur.Mutations);

        Assert.True(harness.Service.QaPayMakeGood(1).Ok);
        harness.PollAt(2.5f);

        Assert.Contains("leave", harness.Arthur.Mutations);
        var entry = Assert.Single(harness.Service.State!.Ledger.Entries);
        Assert.Equal(Chapter3MakeGoodStatus.Paid, entry.Status);
        Assert.Null(entry.WarningDeliveredDay);
        Assert.Equal(0, harness.Service.State.ArthurWarningFollowups);
    }

    [Fact]
    public void Paid_mid_visit_finishes_the_lines_without_recording()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        Assert.Single(harness.Arthur.SaidTexts);

        Assert.True(harness.Service.QaPayMakeGood(1).Ok);
        harness.PollAt(7f);
        harness.PollAt(11.5f);
        harness.PollAt(16f);

        Assert.Equal(3, harness.Arthur.SaidTexts.Count);
        var entry = Assert.Single(harness.Service.State!.Ledger.Entries);
        Assert.Equal(Chapter3MakeGoodStatus.Paid, entry.Status);
        Assert.Null(entry.WarningDeliveredDay);
        Assert.Equal(0, harness.Service.State.ArthurWarningFollowups);
        Assert.Contains("leave", harness.Arthur.Mutations);
    }

    [Fact]
    public void Failed_approach_retries_another_evening_without_delivery()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);

        harness.PollAt(32f);
        harness.Arthur.Distance = 30f;
        harness.PollAt(33f);
        harness.PollAt(34f);

        Assert.Contains("leave", harness.Arthur.Mutations);
        Assert.Null(Assert.Single(harness.Service.State!.Ledger.Entries).WarningDeliveredDay);
        Assert.Equal(0, harness.Service.State.ArthurVisitMissedEvenings);
        var comes = harness.Arthur.Mutations.Count(mutation => mutation == "come");
        Assert.Equal(1, comes);
    }

    [Fact]
    public void Two_missed_evenings_phone_on_the_third()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.Conditions.Outdoors = false;
        harness.Conditions.Detail = "the player is inside a property.";

        harness.SetMinutes(5, 23 * 60 + 30);
        harness.PollAt(1f);
        Assert.Equal(1, harness.Service.State!.ArthurVisitMissedEvenings);
        harness.SetMinutes(6, 23 * 60 + 30);
        harness.PollAt(2f);
        Assert.Equal(2, harness.Service.State!.ArthurVisitMissedEvenings);

        harness.SetMinutes(7, 19 * 60);
        harness.PollAt(3f);

        var call = Assert.Single(harness.Phone.Calls);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, call.Kind);
        Assert.Equal(
            new[] { "Couldn't find you tonight. You owe the family $1,250. Two days, then I find you." },
            call.Stages);
        Assert.Equal(7, call.ChapterDay);
        Assert.Equal(7, Assert.Single(harness.Service.State!.Ledger.Entries).WarningDeliveredDay);
        Assert.Equal(1, harness.Service.State.ArthurWarningFollowups);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State!, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurWarningFollowupText);
    }

    [Fact]
    public void Warning_wins_then_cooperation_runs_when_the_warning_clears()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), CooperationBoard(), chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;

        harness.PollAt(1f);

        Assert.Equal(Chapter3ArthurVisitKind.Warning, harness.Service.State!.ArthurVisitPending);
        Assert.True(harness.Service.QaPayMakeGood(1).Ok);
        harness.PollAt(2.5f);
        harness.Arthur.Distance = 30f;
        harness.PollAt(4f);
        harness.PollAt(5f);

        Assert.Equal(Chapter3ArthurVisitKind.Cooperation, harness.Service.State!.ArthurVisitPending);
        Assert.Equal(2, harness.Arthur.Mutations.Count(mutation => mutation == "come"));
    }

    [Fact]
    public void Cooperation_is_once_per_streak_and_never_touches_the_ledger()
    {
        var harness = new Harness();
        harness.Seed(Chapter3MakeGoodLedger.Empty, CooperationBoard(), chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.PollAt(7f);
        harness.PollAt(11.5f);

        Assert.Equal(
            new[]
            {
                "Nell says you keep telling her no.",
                "The family doesn't like no. Take the next one."
            },
            harness.Arthur.SaidTexts);
        Assert.True(harness.Service.State!.ArthurCooperationDelivered);
        Assert.Empty(harness.Service.State.Ledger.Entries);
        harness.Arthur.Distance = 30f;
        harness.PollAt(13f);
        harness.PollAt(14f);

        var comes = harness.Arthur.Mutations.Count(mutation => mutation == "come");
        harness.SetMinutes(6, 19 * 60);
        harness.PollAt(15f);
        Assert.Equal(comes, harness.Arthur.Mutations.Count(mutation => mutation == "come"));
    }

    [Fact]
    public void Cooperation_phone_marks_delivered_without_touching_the_ledger()
    {
        var harness = new Harness();
        harness.Seed(Chapter3MakeGoodLedger.Empty, CooperationBoard(), chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.Conditions.Outdoors = false;

        harness.SetMinutes(5, 23 * 60 + 30);
        harness.PollAt(1f);
        harness.SetMinutes(6, 23 * 60 + 30);
        harness.PollAt(2f);
        harness.SetMinutes(7, 19 * 60);
        harness.PollAt(3f);

        var call = Assert.Single(harness.Phone.Calls);
        Assert.Equal(Chapter3ArthurVisitKind.Cooperation, call.Kind);
        Assert.Equal(new[] { Chapter3ArthurCopy.PhoneCooperation }, call.Stages);
        Assert.True(harness.Service.State!.ArthurCooperationDelivered);
        Assert.Empty(harness.Service.State.Ledger.Entries);
    }

    [Fact]
    public void Reload_mid_visit_retries_later_and_delivers_once()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        Assert.Single(harness.Arthur.SaidTexts);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        harness.Now = 100f;
        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();
        harness.PollAt(100f);

        var comes = harness.Arthur.Mutations.Count(mutation => mutation == "come");
        harness.PollAt(101f);
        Assert.Equal(comes, harness.Arthur.Mutations.Count(mutation => mutation == "come"));
        harness.SetMinutes(6, 19 * 60);
        harness.PollAt(102f);
        Assert.Equal(comes + 1, harness.Arthur.Mutations.Count(mutation => mutation == "come"));
        harness.PollAt(103.5f);
        harness.PollAt(108f);
        harness.PollAt(112.5f);
        harness.PollAt(117f);

        Assert.Equal(6, Assert.Single(harness.Service.State!.Ledger.Entries).WarningDeliveredDay);
        Assert.Equal(1, harness.Service.State.ArthurWarningFollowups);
        Assert.Equal(4, harness.Arthur.SaidTexts.Count);
    }

    [Fact]
    public void Overdue_make_good_flows_to_an_evening_visit_and_nells_text()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 1, Chapter3JobType.DeadDrop, 1_250, chapterDay: 0).Ledger;
        var harness = new Harness();
        harness.Seed(ledger, Chapter3JobBoard.Empty, chapterDay: 2);
        harness.Activate(chapterDay: 2);
        harness.Clock.ElapsedDays = StartElapsedDay + 3;
        harness.Service.OnDayPassed();

        Assert.Equal(Chapter3MakeGoodStage.WarningDue, Assert.Single(harness.Service.State!.Ledger.Entries).Stage);
        harness.SetMinutes(3, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        Assert.Contains("come", harness.Arthur.Mutations);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.PollAt(7f);
        harness.PollAt(11.5f);
        harness.PollAt(16f);

        Assert.Equal(3, Assert.Single(harness.Service.State!.Ledger.Entries).WarningDeliveredDay);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State!, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurWarningFollowupText);
    }

    [Fact]
    public void Qa_visit_warn_ignores_the_window_but_checks_conditions()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 10 * 60);

        var started = harness.Service.QaArthurVisit("warn");

        Assert.True(started.Ok);
        Assert.Contains("come", harness.Arthur.Mutations);

        var blocked = new Harness();
        blocked.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        blocked.Activate(chapterDay: 5);
        blocked.SetMinutes(5, 10 * 60);
        blocked.Conditions.OnFoot = false;
        blocked.Conditions.Detail = "the player is in a vehicle.";

        var refused = blocked.Service.QaArthurVisit("warn");

        Assert.False(refused.Ok);
        Assert.Contains("in a vehicle", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("come", blocked.Arthur.Mutations);
    }

    [Fact]
    public void Qa_visit_refuses_without_anything_pending_or_with_a_bogus_kind()
    {
        var harness = new Harness();
        harness.Seed(Chapter3MakeGoodLedger.Empty, Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 10 * 60);

        Assert.False(harness.Service.QaArthurVisit("warn").Ok);
        Assert.False(harness.Service.QaArthurVisit("coop").Ok);
        Assert.False(harness.Service.QaArthurVisit("call").Ok);
        var bogus = harness.Service.QaArthurVisit("bogus");
        Assert.False(bogus.Ok);
        Assert.Contains("Usage", bogus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Qa_visit_call_forces_the_phone_for_whatever_is_pending()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 10 * 60);

        var result = harness.Service.QaArthurVisit("call");

        Assert.True(result.Ok);
        var call = Assert.Single(harness.Phone.Calls);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, call.Kind);
        Assert.Equal(5, Assert.Single(harness.Service.State!.Ledger.Entries).WarningDeliveredDay);
    }

    [Fact]
    public void Status_shows_the_pending_kind_the_misses_and_the_delivered_flags()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.Conditions.Outdoors = false;
        harness.SetMinutes(5, 23 * 60 + 30);
        harness.PollAt(1f);

        var pending = Chapter3QaCommand.Execute(harness.Service, new[] { "status" }, qaEnabled: true);

        Assert.Contains("arthur visit: warning pending, missed 1, last attempt day 5; delivered: cooperation no, warnings 0", pending);

        harness.Conditions.Outdoors = true;
        harness.SetMinutes(6, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(2f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(3.5f);
        harness.PollAt(8f);
        harness.PollAt(12.5f);
        harness.PollAt(17f);
        harness.Arthur.Distance = 30f;
        harness.PollAt(18f);
        harness.PollAt(19f);

        var delivered = Chapter3QaCommand.Execute(harness.Service, new[] { "status" }, qaEnabled: true);

        Assert.Contains("arthur visit: none pending; delivered: cooperation no, warnings 1", delivered);
    }

    [Fact]
    public void Unwired_engine_never_starts_and_never_calls()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Service.VisitConditions = null;
        harness.Service.ArthurPhone = null;
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);
        Assert.Empty(harness.Phone.Calls);
    }

    [Fact]
    public void Engine_idles_without_host_authority_or_an_active_chapter()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.HasAuthority = false;

        harness.PollAt(1f);

        Assert.DoesNotContain("come", harness.Arthur.Mutations);

        var idle = new Harness();
        idle.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        idle.SetMinutes(5, 19 * 60);
        idle.PollAt(1f);

        Assert.DoesNotContain("come", idle.Arthur.Mutations);
    }

    private sealed class Harness
    {
        public DeadDropFakeWorld World { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = StartElapsedDay };
        public FakeArthurWorld Arthur { get; } = new();
        public FakeConditions Conditions { get; } = new();
        public FakePhone Phone { get; } = new();
        public FakePresence Presence { get; } = new();
        public List<string> Logs { get; } = new();
        public float Now;
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.DeadDropWorld = World;
            Service.ArthurWorld = Arthur;
            Service.ArthurClock = () => Now;
            Service.HqPresence = Presence;
            Service.VisitConditions = Conditions;
            Service.ArthurPhone = Phone;
        }

        public void Seed(Chapter3MakeGoodLedger ledger, Chapter3JobBoard board, int chapterDay) =>
            Repository.Stored = new Chapter3State(
                PlayerId, Chapter3Phase.Active, StartElapsedDay, StartElapsedDay + chapterDay, 0, board, ledger);

        public void Activate(int chapterDay)
        {
            Clock.ElapsedDays = StartElapsedDay + chapterDay;
            SetMinutes(chapterDay, 9 * 60);
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
            PollAt(0f);
        }

        public void SetMinutes(int chapterDay, int minuteOfDay) =>
            World.CanonicalMinutes = (StartElapsedDay + chapterDay) * 1440d + minuteOfDay;

        public void PollAt(float now)
        {
            Now = now;
            Service.PollArthurVisit();
        }
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } =
            new(Guid.Parse("77777777-7777-7777-7777-777777777777"), 1, PlayerId, System.IO.Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3State? Stored { get; set; }

        public Chapter3StoreLoadResult Load() => Stored is null
            ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
            : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");

        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Stored = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
        }

        public Chapter3StoreUpdateResult PreserveRawContents(string rawContents) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; }
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return true;
        }

        public void RaiseDay() => DayPassed?.Invoke();

        public void RaiseWeek() => WeekPassed?.Invoke();
    }

    private sealed class FakePresence : IChapter3HqPresence
    {
        public bool Chapter = true;
        public bool Inside;
        public bool ChapterActive => Chapter;
        public bool PlayerInsideHq => Inside;
    }

    private sealed class FakeConditions : IChapter3ArthurVisitConditions
    {
        public bool Readable = true;
        public bool OnFoot = true;
        public bool Outdoors = true;
        public bool Clear = true;
        public string Detail = "on foot, outdoors, clear of pursuit";

        public bool TryRead(out Chapter3ArthurVisitConditions conditions)
        {
            conditions = new Chapter3ArthurVisitConditions(OnFoot, Outdoors, Clear, Detail);
            return Readable;
        }
    }

    private sealed class FakePhone : IChapter3ArthurPhone
    {
        public sealed record Call(Chapter3ArthurVisitKind Kind, IReadOnlyList<string> Stages, int ChapterDay);

        public bool Succeed = true;
        public List<Call> Calls { get; } = new();

        public bool TryCall(Chapter3ArthurVisitKind kind, IReadOnlyList<string> stages, int chapterDay, out string reason)
        {
            reason = Succeed ? "called" : "the phone failed.";
            if (Succeed)
                Calls.Add(new Call(kind, stages.ToArray(), chapterDay));
            return Succeed;
        }
    }

    private sealed class FakeArthurWorld : IChapter3ArthurWorld
    {
        public bool HasAuthority = true;
        public bool Present = true;
        public bool Spawned = true;
        public bool Moving = true;
        public float Distance = 20f;
        public float Health = 100f;
        public bool InView = true;
        public Chapter3ArthurMutationStatus ComeStatus = Chapter3ArthurMutationStatus.Succeeded;
        public List<string> Mutations { get; } = new();
        public List<string> SaidTexts { get; } = new();

        bool IChapter3ArthurWorld.HasAuthority => HasAuthority;

        public bool PollArthurImpact() => false;

        public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
        {
            snapshot = new Chapter3ArthurSnapshot(
                Present, Spawned, true, Moving, true, (0f, 0f, 0f), Distance, 1, Health, InView);
            return Chapter3ArthurReadStatus.Ready;
        }

        public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason)
        {
            Mutations.Add("move");
            reason = "moved out of sight";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryParkArthur(out string reason)
        {
            Mutations.Add("park");
            reason = "parked";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason)
        {
            Mutations.Add("verify");
            reason = "paths verified";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryComeArthur(out string reason)
        {
            Mutations.Add("come");
            reason = ComeStatus == Chapter3ArthurMutationStatus.Succeeded ? "coming" : "the come failed.";
            return ComeStatus;
        }

        public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason)
        {
            Mutations.Add($"say:{text}");
            SaidTexts.Add(text);
            reason = "said";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason)
        {
            Mutations.Add("leave");
            reason = "leaving";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryFacePlayer(out string reason)
        {
            Mutations.Add("face");
            reason = "faced";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
        {
            Mutations.Add("facehq");
            reason = "faced directly";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)
        {
            Mutations.Add("capture");
            local = (0f, 0f, 0f);
            yawDegrees = 0f;
            reason = "captured";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason)
        {
            Mutations.Add("place");
            reason = "placed at the HQ spot";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryResumeWalk(out string reason)
        {
            Mutations.Add("resume");
            reason = "re-issued";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason)
        {
            Mutations.Add("dedupe");
            reason = "deduped";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason)
        {
            Mutations.Add($"anim:{trigger}");
            reason = $"played {trigger}.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

    }
}
