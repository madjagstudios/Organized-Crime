using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

// OC-135: Arthur's beating, staged like the native hospital wake-up
// but without the death. The direct tests pin the coordinator's
// order and fail-safes; the service tests run attacks and scheduled
// visits through the real engine, pump, ledger, and planner.
public sealed class Chapter3ArthurBeatingTests
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

    private static Chapter3MakeGoodLedger BeatingDueLedger(params int[] amounts)
    {
        var ledger = WarningDueLedger(amounts);
        foreach (var entry in ledger.Entries.ToList())
            ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, entry.Id, chapterDay: 3).Ledger;
        return Chapter3MakeGoods.OnDayTick(ledger, 3 + Chapter3MakeGoodTuning.WarningToBeatingDays).Ledger;
    }

    private static Chapter3JobBoard CooperationBoard() =>
        Chapter3JobBoard.Empty with { CooperationVisitIssued = true, RefusalStreak = 5 };

    [Fact]
    public void Scheduled_beating_runs_lock_to_nell_in_order()
    {
        var direct = new Direct();

        direct.RunToDone();

        Assert.True(direct.Beating.IsDone);
        Assert.True(direct.Beating.CompletedOk);
        Assert.Equal(
            new[] { "lock", "readhealth", "jolt", "sethealth", "jolt", "sethealth", "jolt", "sethealth", "jolt", "sethealth", "fadeout", "move", "fadein", "bill", "unlock" },
            direct.World.Calls);
        Assert.Equal(
            new[] { "face", $"say:{Chapter3ArthurCopy.BeatingScheduledLine}", "anim:Punch", "anim:Punch", "anim:Punch", "anim:Punch" },
            direct.Arthur.Mutations);
        Assert.Equal(new[] { 81.25f, 62.5f, 43.75f, 25f }, direct.World.HealthSets);
        Assert.Equal(25f, direct.World.Health);
        Assert.Equal(1, direct.Records);
        Assert.Equal(1, direct.Notes);
        Assert.Equal(1, direct.Parks);
        Assert.Equal(0, direct.Hq.Exits);
        Assert.Contains(direct.Logs, line => line.Contains("complete; Nell's text is queued.", StringComparison.Ordinal));
    }

    [Fact]
    public void Attack_beating_speaks_the_attack_line()
    {
        var direct = new Direct(Chapter3BeatingKind.Attack);

        direct.RunToDone();

        Assert.True(direct.Beating.CompletedOk);
        Assert.Contains($"say:{Chapter3ArthurCopy.BeatingAttackLine}", direct.Arthur.Mutations);
    }

    [Fact]
    public void At_hq_beating_faces_directly_skips_park_but_exits()
    {
        var direct = new Direct(Chapter3BeatingKind.Attack, parkArthur: false);
        direct.Hq.Inside = true;

        direct.RunToDone();

        Assert.True(direct.Beating.CompletedOk);
        Assert.Contains("facehq", direct.Arthur.Mutations);
        Assert.DoesNotContain("face", direct.Arthur.Mutations);
        Assert.Equal(0, direct.Parks);
        Assert.Equal(1, direct.Hq.Exits);
        Assert.Equal(1, direct.Records);
    }

    [Fact]
    public void Outside_hq_skips_the_exit()
    {
        var direct = new Direct();
        direct.Hq.Inside = false;

        direct.RunToDone();

        Assert.True(direct.Beating.CompletedOk);
        Assert.Equal(0, direct.Hq.Exits);
    }

    [Fact]
    public void Timeout_aborts_unlocks_and_records_nothing()
    {
        var direct = new Direct();
        direct.Tick(0f);
        direct.Tick(0f);

        direct.Tick(Chapter3ArthurBeating.TimeoutSeconds + 1f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(0, direct.Records);
        Assert.Equal(0, direct.Notes);
        Assert.Contains(direct.Logs, line => line.Contains("timed out", StringComparison.Ordinal));
    }

    [Fact]
    public void Pump_change_mid_beating_aborts()
    {
        var direct = new Direct();
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);

        direct.Tick(3.1f, Chapter3ArthurVisitState.Parked);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(0, direct.Records);
        Assert.Contains(direct.Logs, line => line.Contains("Parked", StringComparison.Ordinal));
    }

    [Fact]
    public void Attack_mid_approach_tolerates_his_arrival()
    {
        var direct = new Direct(Chapter3BeatingKind.Attack, pumpAtStart: Chapter3ArthurVisitState.Coming);
        direct.Tick(0f, Chapter3ArthurVisitState.Coming);
        direct.Tick(0f, Chapter3ArthurVisitState.Arrived);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);
        direct.Tick(6f);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);

        Assert.True(direct.Beating.CompletedOk);
        Assert.Equal(1, direct.Records);
        Assert.DoesNotContain(direct.Logs, line => line.Contains("pump left", StringComparison.Ordinal));
    }

    [Fact]
    public void Attack_mid_approach_still_aborts_if_he_parks_before_the_move()
    {
        var direct = new Direct(Chapter3BeatingKind.Attack, pumpAtStart: Chapter3ArthurVisitState.Coming);
        direct.Tick(0f, Chapter3ArthurVisitState.Coming);
        direct.Tick(0f, Chapter3ArthurVisitState.Parked);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Equal(0, direct.Records);
        Assert.Contains("unlock", direct.World.Calls);
    }

    [Fact]
    public void Pump_change_after_move_is_tolerated()
    {
        var direct = new Direct();
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);
        direct.Tick(6f);
        Assert.Equal(1, direct.Parks);

        direct.Tick(6.7f, Chapter3ArthurVisitState.Leaving);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);
        direct.Tick(6.7f, Chapter3ArthurVisitState.Parked);

        Assert.True(direct.Beating.CompletedOk);
        Assert.Equal(1, direct.Records);
        Assert.Equal(1, direct.Notes);
    }

    [Fact]
    public void Failed_lock_aborts_and_records_nothing()
    {
        var direct = new Direct();
        direct.World.Failures.Add("lock");

        direct.Tick(0f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Equal(new[] { "lock", "unlock" }, direct.World.Calls);
        Assert.Equal(0, direct.Records);
        Assert.Contains(direct.Logs, line => line.Contains("the lock failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_move_records_nothing()
    {
        var direct = new Direct();
        direct.World.Failures.Add("move");
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);
        direct.Tick(6f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("move", direct.World.Calls);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(0, direct.Records);
        Assert.Contains(direct.Logs, line => line.Contains("the move failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_jolt_aborts_and_unlocks()
    {
        var direct = new Direct();
        direct.World.Failures.Add("jolt");
        direct.Tick(0f);
        direct.Tick(0f);

        direct.Tick(2f);

        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(0, direct.Records);
    }

    [Fact]
    public void Failed_bill_after_the_move_still_records()
    {
        var direct = new Direct();
        direct.World.Failures.Add("bill");
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);
        direct.Tick(6f);

        direct.Tick(6.7f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(1, direct.Records);
        Assert.Equal(1, direct.Notes);
        Assert.Contains(direct.Logs, line => line.Contains("finished with a fault after the wake: the bill failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_fade_in_after_the_move_still_records()
    {
        var direct = new Direct();
        direct.World.Failures.Add("fadein");
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);

        direct.Tick(6f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(1, direct.Records);
        Assert.Equal(1, direct.Notes);
        Assert.Contains(direct.Logs, line => line.Contains("finished with a fault after the wake: the fade in failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_unlock_after_the_move_still_records()
    {
        var direct = new Direct();
        direct.World.Failures.Add("unlock");
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);
        direct.Tick(6f);
        direct.Tick(6.7f);

        direct.Tick(6.7f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Equal(2, direct.World.Calls.Count(call => call == "unlock"));
        Assert.Equal(1, direct.Records);
        Assert.Equal(1, direct.Notes);
        Assert.Contains(direct.Logs, line => line.Contains("finished with a fault after the wake: the unlock failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Timeout_after_the_move_still_records()
    {
        var direct = new Direct();
        direct.Tick(0f);
        direct.Tick(0f);
        direct.Tick(2f);
        direct.Tick(3.1f);
        direct.Tick(4.2f);
        direct.Tick(5.3f);
        direct.Tick(5.3f);
        direct.Tick(6f);

        direct.Tick(Chapter3ArthurBeating.TimeoutSeconds + 1f);

        Assert.True(direct.Beating.IsDone);
        Assert.False(direct.Beating.CompletedOk);
        Assert.Contains("unlock", direct.World.Calls);
        Assert.Equal(1, direct.Records);
        Assert.Equal(1, direct.Notes);
        Assert.Contains(direct.Logs, line => line.Contains("finished with a fault after the wake: timed out", StringComparison.Ordinal));
    }

    [Fact]
    public void Abort_is_idempotent_and_unlocks_once()
    {
        var direct = new Direct();
        direct.Tick(0f);

        direct.Beating.AbortOutside("first");
        direct.Beating.AbortOutside("second");

        Assert.True(direct.Beating.IsDone);
        Assert.Equal(new[] { "lock", "readhealth", "unlock" }, direct.World.Calls);
        Assert.Single(direct.Logs, line => line.Contains("aborted", StringComparison.Ordinal));
    }

    [Fact]
    public void Low_health_never_rises_through_the_punches()
    {
        var direct = new Direct();
        direct.World.Health = 20f;

        direct.RunToDone();

        Assert.True(direct.Beating.CompletedOk);
        Assert.Equal(new[] { 20f, 20f, 20f, 20f }, direct.World.HealthSets);
        Assert.Equal(20f, direct.World.Health);
    }

    [Fact]
    public void Dead_player_aborts_at_lock()
    {
        var direct = new Direct();
        direct.World.Health = 0f;

        direct.Tick(0f);

        Assert.False(direct.Beating.CompletedOk);
        Assert.Equal(new[] { "lock", "readhealth", "unlock" }, direct.World.Calls);
        Assert.Equal(0, direct.Records);
    }

    [Fact]
    public void Attack_during_warning_visit_beats_then_records_everything()
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

        harness.Arthur.Health = 90f;
        harness.PollAt(4f);

        Assert.Contains("Chapter 3 Arthur was attacked (visit).", harness.Logs);
        Assert.Contains(harness.Logs, line => line.Contains("beating begins (attack)", StringComparison.Ordinal));
        Assert.DoesNotContain("leave", harness.Arthur.Mutations);
        Assert.Null(Assert.Single(harness.Service.State!.Ledger.Entries).WarningDeliveredDay);
        harness.Arthur.Mutations.Clear();
        var linesBefore = harness.Arthur.SaidTexts.Count;

        harness.DriveBeating();

        var entry = Assert.Single(harness.Service.State!.Ledger.Entries);
        Assert.Equal(5, entry.WarningDeliveredDay);
        Assert.Equal(5, entry.BeatingDeliveredDay);
        Assert.Equal(Chapter3MakeGoodStage.None, entry.Stage);
        Assert.Equal(1, harness.Service.State.ArthurBeatings);
        Assert.Contains("park", harness.Arthur.Mutations);
        Assert.Equal(linesBefore + 1, harness.Arthur.SaidTexts.Count);
        Assert.Equal(Chapter3ArthurCopy.BeatingAttackLine, harness.Arthur.SaidTexts[^1]);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingDebtsText(1_250));
        harness.PollAt(harness.Now + 2f);
        Assert.Equal(Chapter3ArthurVisitKind.None, harness.Service.State.ArthurVisitPending);
        Assert.Single(harness.Logs, line => line.Contains("beating begins", StringComparison.Ordinal));
    }

    [Fact]
    public void Attack_with_no_debts_still_beats_and_nell_rebukes()
    {
        var harness = new Harness();
        harness.Seed(Chapter3MakeGoodLedger.Empty, CooperationBoard(), chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);

        harness.Arthur.Health = 90f;
        harness.PollAt(4f);
        harness.DriveBeating();

        Assert.Equal(1, harness.Service.State!.ArthurBeatings);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingNoDebtText);
    }

    [Fact]
    public void Second_attack_mid_beating_is_ignored()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.Arthur.Health = 90f;
        harness.PollAt(4f);

        harness.Arthur.Health = 100f;
        harness.PollAt(5.2f);
        harness.Arthur.Health = 80f;
        harness.PollAt(6.4f);

        Assert.Single(harness.Logs, line => line.Contains("beating begins", StringComparison.Ordinal));
        harness.DriveBeating();
        Assert.Equal(1, harness.Service.State!.ArthurBeatings);
    }

    [Fact]
    public void Scheduled_beating_visits_records_and_texts_nell()
    {
        var harness = new Harness();
        harness.Seed(BeatingDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        Assert.Contains("come", harness.Arthur.Mutations);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);

        Assert.Contains(harness.Logs, line => line.Contains("beating begins (scheduled)", StringComparison.Ordinal));
        Assert.Empty(harness.Arthur.SaidTexts);
        harness.Arthur.Mutations.Clear();

        harness.DriveBeating();

        var entry = Assert.Single(harness.Service.State!.Ledger.Entries);
        Assert.Equal(3, entry.WarningDeliveredDay);
        Assert.Equal(5, entry.BeatingDeliveredDay);
        Assert.Equal(Chapter3MakeGoodStage.None, entry.Stage);
        Assert.Equal(1, harness.Service.State.ArthurBeatings);
        Assert.Contains("park", harness.Arthur.Mutations);
        Assert.Contains("Chapter 3 Arthur beating: Nell's text is queued.", harness.Logs);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingDebtsText(1_250));
    }

    [Fact]
    public void Visit_beat_refuses_without_beating_due()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);

        var result = harness.Service.QaArthurVisit("beat");

        Assert.False(result.Ok);
        Assert.Contains("no beating visit is pending", result.Message);
    }

    [Fact]
    public void Visit_call_refuses_when_a_beating_is_pending()
    {
        var harness = new Harness();
        harness.Seed(BeatingDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);

        var result = harness.Service.QaArthurVisit("call");

        Assert.False(result.Ok);
        Assert.Contains("never phone", result.Message);
        Assert.Empty(harness.Phone.Calls);
    }

    [Fact]
    public void Qa_beat_refuses_until_arrived_then_starts()
    {
        var harness = new Harness();
        harness.Seed(Chapter3MakeGoodLedger.Empty, Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);

        var parked = harness.Service.QaArthurBeat();
        Assert.False(parked.Ok);
        Assert.Contains("not arrived", parked.Message);

        Assert.True(harness.Service.QaArthurCome().Ok);
        harness.Arthur.Distance = 2f;
        harness.PollAt(1f);
        harness.PollAt(2.5f);
        harness.PollAt(4f);

        var arrived = harness.Service.QaArthurBeat();
        Assert.True(arrived.Ok);
        harness.DriveBeating();
        Assert.Equal(1, harness.Service.State!.ArthurBeatings);
    }

    [Fact]
    public void Status_reports_beating_widened_and_days()
    {
        var harness = new Harness();
        harness.Seed(BeatingDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);

        var idle = harness.Service.QaArthurStatus();
        Assert.Contains("beating: none", idle);
        Assert.Contains("visit pending: beating (missed evenings: 0, widened: no)", idle);
        Assert.Contains("beating days: none", idle);
        Assert.Contains("beatings delivered: 0", idle);

        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        var running = harness.Service.QaArthurStatus();
        Assert.Contains("beating: scheduled running", running);

        harness.DriveBeating();
        var done = harness.Service.QaArthurStatus();
        Assert.Contains("beating: none", done);
        Assert.Contains("beating days: 5", done);
        Assert.Contains("beatings delivered: 1", done);
    }

    [Fact]
    public void Widened_beating_comes_off_window_and_never_phones()
    {
        var harness = new Harness();
        harness.Seed(BeatingDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.Conditions.Outdoors = false;
        harness.Conditions.Detail = "the player is inside a property.";

        harness.SetMinutes(5, 23 * 60 + 30);
        harness.PollAt(1f);
        Assert.Equal(1, harness.Service.State!.ArthurVisitMissedEvenings);
        harness.SetMinutes(6, 23 * 60 + 30);
        harness.PollAt(2f);
        Assert.Equal(2, harness.Service.State!.ArthurVisitMissedEvenings);
        Assert.Contains("widened: yes", string.Join(" ", harness.Service.QaArthurStatus()));

        harness.Conditions.Outdoors = true;
        harness.Conditions.Detail = "on foot, outdoors, clear of pursuit";
        harness.SetMinutes(7, 9 * 60);
        harness.PollAt(3f);

        Assert.Contains("come", harness.Arthur.Mutations);
        Assert.Empty(harness.Phone.Calls);
        Assert.Equal(Chapter3ArthurVisitKind.Beating, harness.Service.State!.ArthurVisitPending);
    }

    [Fact]
    public void Beating_paid_before_arrival_cancels()
    {
        var harness = new Harness();
        harness.Seed(BeatingDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        Assert.Contains("come", harness.Arthur.Mutations);

        Assert.True(harness.Service.QaPayMakeGood(1).Ok);
        harness.PollAt(2.5f);

        Assert.Contains(harness.Logs, line => line.Contains("paid before he arrived", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs, line => line.Contains("beating begins", StringComparison.Ordinal));
        Assert.Equal(0, harness.Service.State!.ArthurBeatings);
    }

    [Fact]
    public void Preload_mid_beating_unlocks_and_records_nothing()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.Arthur.Health = 90f;
        harness.PollAt(4f);
        Assert.Contains(harness.Logs, line => line.Contains("beating begins (attack)", StringComparison.Ordinal));

        harness.Service.OnPreLoad();

        Assert.Contains("unlock", harness.Beat.Calls);
        Assert.False(harness.Beat.Locked);
        Assert.Contains(harness.Logs, line => line.Contains("the scene is unloading", StringComparison.Ordinal));
        Assert.Contains("beating: none", harness.Service.QaArthurStatus());
        harness.Service.OnLoadComplete();
        Assert.Equal(0, harness.Service.State!.ArthurBeatings);
        Assert.Null(Assert.Single(harness.Service.State.Ledger.Entries).BeatingDeliveredDay);
    }

    [Fact]
    public void Attack_at_hq_beats_and_the_pump_parks_after_the_exit()
    {
        var harness = new Harness();
        harness.Seed(Chapter3MakeGoodLedger.Empty, Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.Presence.Inside = true;
        harness.Arthur.Distance = 2f;
        harness.PollAt(1f);
        harness.PollAt(2.5f);
        harness.PollAt(4f);
        harness.Arthur.Health = 90f;
        harness.PollAt(5.5f);

        Assert.Contains("Chapter 3 Arthur was attacked (HQ).", harness.Logs);
        Assert.Contains(harness.Logs, line => line.Contains("beating begins (attack)", StringComparison.Ordinal));
        harness.Arthur.Mutations.Clear();

        harness.DriveBeating();

        // The exit clears the same inside flag the pump reads, so the
        // pump parks Arthur itself; the coordinator skips its own park
        // but still runs the fade-in, bill, unlock, record, and Nell.
        Assert.Equal(1, harness.BeatHq.Exits);
        Assert.False(harness.Presence.Inside);
        Assert.Contains(harness.Logs, line => line.Contains("left the HQ spot", StringComparison.Ordinal));
        Assert.Contains("park", harness.Arthur.Mutations);
        Assert.Contains("facehq", harness.Arthur.Mutations);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("beating: Arthur parked.", StringComparison.Ordinal));
        Assert.Contains("fadein", harness.Beat.Calls);
        Assert.Contains("bill", harness.Beat.Calls);
        Assert.Contains("unlock", harness.Beat.Calls);
        Assert.Equal(1, harness.Service.State!.ArthurBeatings);
        Assert.Contains("Chapter 3 Arthur beating: Nell's text is queued.", harness.Logs);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingNoDebtText);
    }

    [Fact]
    public void Beating_world_gone_aborts()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.Arthur.Health = 90f;
        harness.PollAt(4f);

        harness.Service.BeatingWorld = null;
        harness.PollAt(5.2f);

        Assert.Contains(harness.Logs, line => line.Contains("a beating world went away", StringComparison.Ordinal));
        Assert.Contains("unlock", harness.Beat.Calls);
        Assert.Equal(0, harness.Service.State!.ArthurBeatings);
    }

    [Fact]
    public void Preload_after_the_move_unlocks_and_records_nothing()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.Arthur.Health = 90f;
        harness.PollAt(4f);
        for (var i = 0; i < 12 && !harness.Beat.Calls.Contains("move"); i++)
            harness.PollAt(harness.Now + 1.2f);
        Assert.Contains("move", harness.Beat.Calls);

        harness.Service.OnPreLoad();

        Assert.Contains("unlock", harness.Beat.Calls);
        Assert.False(harness.Beat.Locked);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("finished with a fault", StringComparison.Ordinal));
        harness.Service.OnLoadComplete();
        Assert.Equal(0, harness.Service.State!.ArthurBeatings);
        Assert.Null(Assert.Single(harness.Service.State.Ledger.Entries).BeatingDeliveredDay);
    }

    [Fact]
    public void Beating_world_gone_after_the_move_still_records()
    {
        var harness = new Harness();
        harness.Seed(WarningDueLedger(1_250), Chapter3JobBoard.Empty, chapterDay: 5);
        harness.Activate(chapterDay: 5);
        harness.SetMinutes(5, 19 * 60);
        harness.Arthur.Distance = 20f;
        harness.PollAt(1f);
        harness.Arthur.Distance = 2f;
        harness.PollAt(2.5f);
        harness.Arthur.Health = 90f;
        harness.PollAt(4f);
        for (var i = 0; i < 12 && !harness.Beat.Calls.Contains("move"); i++)
            harness.PollAt(harness.Now + 1.2f);
        Assert.Contains("move", harness.Beat.Calls);

        harness.Service.BeatingWorld = null;
        harness.PollAt(harness.Now + 1.2f);

        Assert.Contains(harness.Logs, line => line.Contains("finished with a fault after the wake: a beating world went away", StringComparison.Ordinal));
        Assert.Contains("unlock", harness.Beat.Calls);
        Assert.Equal(1, harness.Service.State!.ArthurBeatings);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State, release1HasNellDecision: false, warehouseOwned: false);
        Assert.Contains(plan.Messages, message => message.Text == Chapter3NellCopy.ArthurBeatingDebtsText(1_250));
    }

    private sealed class Direct
    {
        public FakeBeatingWorld World { get; } = new();
        public FakeArthurWorld Arthur { get; } = new();
        public FakeBeatingHq Hq { get; } = new();
        public List<string> Logs { get; } = new();
        public int Records;
        public int Notes;
        public int Parks;
        public Chapter3ArthurBeating Beating { get; }

        public Direct(
            Chapter3BeatingKind kind = Chapter3BeatingKind.Scheduled,
            bool parkArthur = true,
            float startedAt = 0f,
            Chapter3ArthurVisitState pumpAtStart = Chapter3ArthurVisitState.Arrived)
        {
            Beating = new Chapter3ArthurBeating(
                World, Arthur, Hq, kind, parkArthur, pumpAtStart, startedAt,
                Logs.Add, () => Records++, () => Notes++, () => Parks++);
        }

        public void Tick(float now, Chapter3ArthurVisitState pump = Chapter3ArthurVisitState.Arrived) =>
            Beating.Tick(now, pump);

        public void RunToDone()
        {
            Tick(0f);
            Tick(0f);
            Tick(2f);
            Tick(3.1f);
            Tick(4.2f);
            Tick(5.3f);
            Tick(5.3f);
            Tick(6f);
            Tick(6f);
            Tick(6.7f);
            Tick(6.7f);
            Tick(6.7f);
            Tick(6.7f);
        }
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
        public FakeBeatingWorld Beat { get; } = new();
        public FakeBeatingHq BeatHq { get; } = new();
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
            Service.BeatingWorld = Beat;
            Service.BeatingHq = BeatHq;
            BeatHq.Presence = Presence;
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

        public void DriveBeating()
        {
            for (var i = 0; i < 14; i++)
                PollAt(Now + 1.2f);
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

    private sealed class FakeBeatingWorld : IChapter3BeatingWorld
    {
        public bool Host = true;
        public bool Spawned = true;
        public float Health = 100f;
        public float MaxHealth = 100f;
        public bool Locked;
        public (float X, float Y, float Z) WakeAt = (10f, 0f, 20f);
        public string WakeCode = "stash-house";
        public HashSet<string> Failures { get; } = new(StringComparer.Ordinal);
        public List<string> Calls { get; } = new();
        public List<float> HealthSets { get; } = new();

        public bool IsHost => Host;
        public bool IsPlayerSpawned => Spawned;

        public bool TryReadHealth(out float health, out float maxHealth, out string reason)
        {
            Calls.Add("readhealth");
            health = Health;
            maxHealth = MaxHealth;
            reason = string.Empty;
            return !Failures.Contains("readhealth");
        }

        public bool TrySetHealth(float health, out string reason)
        {
            Calls.Add("sethealth");
            reason = string.Empty;
            if (Failures.Contains("sethealth"))
            {
                reason = "sethealth failed.";
                return false;
            }
            Health = health;
            HealthSets.Add(health);
            return true;
        }

        public bool TryLock(out string reason)
        {
            Calls.Add("lock");
            reason = string.Empty;
            if (Failures.Contains("lock"))
            {
                reason = "lock failed.";
                return false;
            }
            Locked = true;
            return true;
        }

        public bool TryUnlock(out string reason)
        {
            Calls.Add("unlock");
            reason = string.Empty;
            if (Failures.Contains("unlock"))
            {
                reason = "unlock failed.";
                return false;
            }
            Locked = false;
            return true;
        }

        public bool TryFadeOut(out string reason)
        {
            Calls.Add("fadeout");
            reason = Failures.Contains("fadeout") ? "fadeout failed." : string.Empty;
            return !Failures.Contains("fadeout");
        }

        public bool TryFadeIn(out string reason)
        {
            Calls.Add("fadein");
            reason = Failures.Contains("fadein") ? "fadein failed." : string.Empty;
            return !Failures.Contains("fadein");
        }

        public bool TryJoltCamera(out string reason)
        {
            Calls.Add("jolt");
            reason = Failures.Contains("jolt") ? "jolt failed." : string.Empty;
            return !Failures.Contains("jolt");
        }

        public bool TryMoveToWake(out (float X, float Y, float Z) wokeAt, out string propertyCode, out string reason)
        {
            Calls.Add("move");
            wokeAt = WakeAt;
            propertyCode = WakeCode;
            reason = Failures.Contains("move") ? "move failed." : string.Empty;
            return !Failures.Contains("move");
        }

        public bool TryOpenBill(out string reason)
        {
            Calls.Add("bill");
            reason = Failures.Contains("bill") ? "bill failed." : string.Empty;
            return !Failures.Contains("bill");
        }
    }

    private sealed class FakeBeatingHq : IChapter3BeatingHq
    {
        public bool Inside;
        public int Exits;
        // Wired to the pump presence by the harness, as Mod.cs wires
        // both to the HQ service's inside state; the exit then clears
        // the same flag the pump reads. Direct tests leave this null
        // and use the local flag.
        public FakePresence? Presence;

        public bool PlayerInsideHq => Presence?.Inside ?? Inside;

        public bool TryExit(out string reason)
        {
            Exits++;
            Inside = false;
            if (Presence is not null)
                Presence.Inside = false;
            reason = "left the HQ through its own exit.";
            return true;
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
