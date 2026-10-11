using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RuntimeServiceTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Factory_constructor_used_by_the_mod_shell_constructs_and_binds_at_load()
    {
        // Review regression: the factory constructor chained a null repository into the
        // direct constructor's null check, so OnInitializeMelon threw and the whole mod failed.
        var repository = new FakeRepository();
        string? requestedFolder = null;
        var service = new Chapter3RuntimeService(
            new FakeContext(),
            folder => { requestedFolder = folder; return repository; },
            new FakeGate(),
            new FakeClock());

        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.NotNull(requestedFolder);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void PreLoad_resets_without_the_host_check()
    {
        // OC-108 lesson: PreLoad fires before the server starts, so the reset
        // must not consult host authority, and previous-scene state is dropped.
        var context = new FakeContext();
        var service = new Chapter3RuntimeService(context, new FakeRepository(), new FakeGate(), new FakeClock());
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);

        context.Status = Release1StoryHostContextReadStatus.Pending;
        var readsBefore = context.ReadCount;

        var result = service.OnPreLoad();

        Assert.Equal(Chapter3RuntimeRejectReason.None, result.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.AwaitingLoad, service.Phase);
        Assert.Null(service.State);
        Assert.Equal(readsBefore, context.ReadCount);
    }

    [Fact]
    public void Reload_keeps_the_start_day_and_the_processed_day()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var gate = new FakeGate { Satisfied = true };
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(context, repository, gate, clock);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        service.OnSaveStart();
        service.OnSaveComplete();
        Assert.Equal(11, repository.Stored!.StartElapsedDay);

        var reloaded = new Chapter3RuntimeService(context, repository, gate, clock);
        reloaded.OnPreLoad();
        var load = reloaded.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.None, load.RejectReason);
        Assert.Equal(11, reloaded.State!.StartElapsedDay);
        Assert.Equal(11, reloaded.State.LastProcessedElapsedDay);

        clock.ElapsedDays = 12;
        reloaded.OnDayPassed();

        Assert.Equal(12, reloaded.State.LastProcessedElapsedDay);
        Assert.Equal(1, reloaded.State.ChapterDayAt(12));
    }

    [Fact]
    public void Day_tick_logs_the_start_line_then_the_day_line()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock, logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();

        clock.ElapsedDays = 11;
        clock.Raise();
        clock.ElapsedDays = 12;
        clock.Raise();

        var offer = Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board.OpenOffer!;

        Assert.Equal(
            new[]
            {
                "Chapter 3 started; day 0 is elapsed day 11",
                "Chapter 3 day 0",
                $"Chapter 3 offer: DeadDrop, reward ${offer.Reward:N0}, respond by day 1",
                "Chapter 3 day 1"
            },
            logs);
    }

    [Fact]
    public void Stale_tick_changes_nothing_and_logs_nothing()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock, logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        logs.Clear();

        clock.ElapsedDays = 11;
        service.OnDayPassed();
        clock.ElapsedDays = 9;
        service.OnDayPassed();

        Assert.Equal(11, service.State!.LastProcessedElapsedDay);
        Assert.Empty(logs);
    }

    [Fact]
    public void Ticks_are_ignored_while_inactive()
    {
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, new FakeClock { ElapsedDays = 11 }, logs.Add);

        service.OnDayPassed();

        Assert.Null(service.State);
        Assert.Empty(logs);
    }

    [Fact]
    public void Corrupt_sidecar_quarantines_and_preserves_bytes_on_save_start()
    {
        const string corrupt = "{ this is not valid chapter 3 json";
        var repository = new FakeRepository { LoadFailureContents = corrupt };
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());
        service.OnPreLoad();

        var load = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.SidecarLoadFailed, load.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);

        var saveStart = service.OnSaveStart();

        Assert.Equal(corrupt, repository.PreservedRawContents);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(Chapter3RuntimeRejectReason.Quarantined, saveStart.RejectReason);
    }

    [Fact]
    public void Identity_mismatch_quarantines()
    {
        var repository = new FakeRepository
        {
            Stored = Chapter3State.CreateInitial("76561190000000002")
        };
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());
        service.OnPreLoad();

        var load = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.IdentityMismatch, load.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);
    }

    [Fact]
    public void Qa_start_begins_immediately_even_with_an_unsatisfied_gate()
    {
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = false }, new FakeClock { ElapsedDays = 20 }, logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.True(service.TryQaStartNow());

        var offer = Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board.OpenOffer!;

        Assert.Equal(Chapter3Phase.Active, service.State!.Phase);
        Assert.Equal(20, service.State.StartElapsedDay);
        Assert.Equal(
            new[]
            {
                "Chapter 3 started; day 0 is elapsed day 20",
                "Chapter 3 day 0",
                $"Chapter 3 offer: DeadDrop, reward ${offer.Reward:N0}, respond by day 1"
            },
            logs);
    }

    [Fact]
    public void Qa_start_refuses_while_inactive()
    {
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock());

        Assert.False(service.TryQaStartNow());
    }

    [Fact]
    public void Envelope_gate_reads_the_release1_satisfied_state()
    {
        Assert.True(new Chapter3StoryGate(() => StoryWithEnvelope(Release1MissionState.Satisfied)).IsEnvelopeSatisfied());
        Assert.False(new Chapter3StoryGate(() => StoryWithEnvelope(Release1MissionState.Active)).IsEnvelopeSatisfied());
        Assert.False(new Chapter3StoryGate(() => null).IsEnvelopeSatisfied());
    }

    private static Release1StoryState StoryWithEnvelope(Release1MissionState envelopeState) =>
        new(
            PlayerId, 20, Release1RelationshipState.Accepted, false,
            new[] { "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1" }, Array.Empty<string>(),
            Release1MissionCatalog.All.Select(mission =>
                new Release1MissionRecord(
                    mission.MissionKey,
                    string.Equals(mission.MissionKey, Release1MissionCatalog.TheEnvelope, StringComparison.Ordinal) ? envelopeState : Release1MissionState.Locked,
                    1, null, null, null,
                    envelopeState == Release1MissionState.Satisfied && string.Equals(mission.MissionKey, Release1MissionCatalog.TheEnvelope, StringComparison.Ordinal) ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    0, Array.Empty<string>(), 0, Release1RecoveryMode.None,
                    envelopeState == Release1MissionState.Satisfied && string.Equals(mission.MissionKey, Release1MissionCatalog.TheEnvelope, StringComparison.Ordinal) ? "test-receipt-1" : null,
                    false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, 0)).ToArray(),
            Array.Empty<Release1NativeEffectJournalEntry>(), 0);

    [Fact]
    public void Day_tick_logs_each_scheduler_effect()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock, logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();

        service.OnDayPassed();
        clock.ElapsedDays = 12;
        service.OnDayPassed();
        clock.ElapsedDays = 13;
        service.OnDayPassed();

        var offer = Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board.OpenOffer!;
        Assert.Equal(
            new[]
            {
                "Chapter 3 started; day 0 is elapsed day 11",
                "Chapter 3 day 0",
                $"Chapter 3 offer: DeadDrop, reward ${offer.Reward:N0}, respond by day 1",
                "Chapter 3 day 1",
                "Chapter 3 day 2",
                "Chapter 3 offer expired: DeadDrop; refusal streak 1"
            },
            logs);
    }

    [Fact]
    public void Factory_constructed_service_logs_the_first_offer()
    {
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(
            new FakeContext(),
            _ => new FakeRepository(),
            new FakeGate { Satisfied = true },
            new FakeClock { ElapsedDays = 11 },
            logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();

        service.OnDayPassed();

        Assert.Contains(logs, line => line.StartsWith("Chapter 3 offer: DeadDrop, reward $"));
    }
    [Fact]
    public void Failed_job_feeds_the_ledger_and_logs_the_opening()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock, logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        Assert.True(service.QaAcceptOpenOffer().Ok);
        var fail = service.QaFailActiveJob();

        Assert.True(fail.Ok);
        var reward = Chapter3JobScheduler.OnDayTick(Chapter3JobBoard.Empty, PlayerId, 0).Board.OpenOffer!.Reward;
        var entry = Assert.Single(service.State!.Ledger.Entries);
        Assert.Equal(new Chapter3FailedJobSource(1, Chapter3JobType.DeadDrop), entry.Source);
        Assert.Equal(reward, entry.Amount);
        Assert.Equal(2, entry.DueDay);
        Assert.Equal($"Chapter 3 make-good opened: failed DeadDrop job, ${reward:N0}, due day 2",
            logs.First(line => line.StartsWith("Chapter 3 make-good opened:")));
        // The oc_ch3 command prints the QA result, so the service doesn't log it a second time.
        Assert.StartsWith("Chapter 3 QA: failed job 1 (DeadDrop); make-good 1 opened", fail.Message);
        Assert.DoesNotContain(logs, line => line.StartsWith("Chapter 3 QA:"));
    }

    [Fact]
    public void Ledger_tick_logs_overdue_and_warning_due()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock, logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        service.QaAcceptOpenOffer();
        service.QaFailActiveJob();
        var reward = Assert.Single(service.State!.Ledger.Entries).Amount;
        logs.Clear();

        clock.ElapsedDays = 12;
        service.OnDayPassed();
        clock.ElapsedDays = 13;
        service.OnDayPassed();
        Assert.DoesNotContain(logs, line => line.Contains("overdue"));

        clock.ElapsedDays = 14;
        service.OnDayPassed();

        Assert.Contains($"Chapter 3 make-good 1 overdue: ${reward:N0} unpaid past day 2", logs);
        Assert.Contains("Chapter 3 make-good 1: Arthur warning due", logs);
        Assert.Equal(Chapter3MakeGoodUrgency.WarningDue, service.State.Ledger.MostUrgentStage);
    }

    [Fact]
    public void Factory_constructed_service_runs_qa_accept_fail_pay()
    {
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(
            new FakeContext(),
            _ => new FakeRepository(),
            new FakeGate { Satisfied = false },
            new FakeClock { ElapsedDays = 11 },
            logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.True(service.TryQaStartNow());

        Assert.True(service.QaAcceptOpenOffer().Ok);
        Assert.True(service.QaFailActiveJob().Ok);
        var reward = Assert.Single(service.State!.Ledger.Entries).Amount;

        var pay = service.QaPayMakeGood(1);

        Assert.True(pay.Ok);
        Assert.Equal(Chapter3MakeGoodStatus.Paid, Assert.Single(service.State.Ledger.Entries).Status);
        Assert.Contains($"Chapter 3 make-good 1 paid: ${reward:N0}", logs);
        Assert.False(service.QaRecordWarning(1).Ok);
    }

    [Fact]
    public void Qa_methods_refuse_without_targets()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, new FakeClock { ElapsedDays = 11 });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        var fail = service.QaFailActiveJob();
        Assert.False(fail.Ok);
        Assert.Contains("there is no active job", fail.Message);

        Assert.True(service.QaAcceptOpenOffer().Ok);
        var accept = service.QaAcceptOpenOffer();
        Assert.False(accept.Ok);
        Assert.Contains("there is no open offer", accept.Message);

        Assert.False(service.QaPayMakeGood(999).Ok);
        Assert.False(service.QaPayMakeGood(1).Ok);

        var warn = service.QaRecordWarning(1);
        Assert.False(warn.Ok);
        Assert.Contains("no warning is due", warn.Message);
    }
    [Fact]
    public void Qa_mutations_bump_the_revision_so_later_saves_succeed()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, new FakeClock { ElapsedDays = 11 });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        var before = service.State!.Revision;

        service.QaAcceptOpenOffer();
        Assert.Equal(before + 1, service.State.Revision);

        service.QaFailActiveJob();
        Assert.Equal(before + 2, service.State.Revision);

        service.QaPayMakeGood(1);
        Assert.Equal(before + 3, service.State.Revision);
    }
    [Fact]
    public void Overnight_line_serves_once_per_night_through_the_factory_constructor()
    {
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(),
            _ => new FakeRepository(),
            new FakeGate { Satisfied = true },
            clock);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        clock.ElapsedDays = 12;
        service.OnDayPassed();
        clock.ElapsedDays = 13;
        service.OnDayPassed();

        Assert.Equal(Chapter3OvernightCopy.FamilyWondering, service.GetOvernightLineForSleep());
        Assert.Null(service.GetOvernightLineForSleep());

        clock.ElapsedDays = 14;
        service.OnDayPassed();
        Assert.Equal(Chapter3OvernightCopy.FamilyWondering, service.GetOvernightLineForSleep());
    }

    [Fact]
    public void Overnight_line_is_null_when_inactive_unstarted_or_unreadable()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock { ElapsedDays = 11 });
        Assert.Null(service.GetOvernightLineForSleep());

        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        Assert.Null(service.GetOvernightLineForSleep());

        var clock = new FakeClock { ElapsedDays = 11, Readable = false };
        var unreadable = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock);
        unreadable.OnPreLoad();
        unreadable.OnLoadComplete();
        Assert.Null(unreadable.GetOvernightLineForSleep());
    }

    [Fact]
    public void Overnight_line_prefers_Arthur()
    {
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        service.QaAcceptOpenOffer();
        service.QaFailActiveJob();
        clock.ElapsedDays = 12;
        service.OnDayPassed();
        clock.ElapsedDays = 13;
        service.OnDayPassed();
        clock.ElapsedDays = 14;
        service.OnDayPassed();

        Assert.Equal(Chapter3OvernightCopy.ArthurWarningDue, service.GetOvernightLineForSleep());
    }

    [Fact]
    public void PreLoad_resets_the_overnight_guard()
    {
        var repository = new FakeRepository();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), repository, new FakeGate { Satisfied = true }, clock);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        clock.ElapsedDays = 12;
        service.OnDayPassed();
        clock.ElapsedDays = 13;
        service.OnDayPassed();
        Assert.NotNull(service.GetOvernightLineForSleep());

        repository.Stored = service.State;
        service.OnPreLoad();
        service.OnLoadComplete();

        Assert.NotNull(service.GetOvernightLineForSleep());
    }

    [Fact]
    public void Checkpoint_target_reads_empty_before_any_pick()
    {
        var service = ActiveRuntimeService();

        var target = service.ReadCheckpointTarget();

        Assert.Null(target.PropertyCode);
        Assert.Null(target.PickDay);
    }

    [Fact]
    public void TryWriteCheckpointTarget_stores_code_and_day_and_bumps_revision()
    {
        var service = ActiveRuntimeService();
        var before = service.State!.Revision;

        Assert.True(service.TryWriteCheckpointTarget(new Chapter3CheckpointTarget("barn", 12)));

        var target = service.ReadCheckpointTarget();
        Assert.Equal("barn", target.PropertyCode);
        Assert.Equal(12, target.PickDay);
        Assert.Equal(before + 1, service.State!.Revision);
    }

    [Fact]
    public void TryWriteCheckpointTarget_stores_a_no_property_pick()
    {
        var service = ActiveRuntimeService();

        Assert.True(service.TryWriteCheckpointTarget(new Chapter3CheckpointTarget(null, 12)));

        var target = service.ReadCheckpointTarget();
        Assert.Null(target.PropertyCode);
        Assert.Equal(12, target.PickDay);
    }

    [Fact]
    public void Checkpoint_target_read_fails_closed_and_write_fails_when_not_active()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, new FakeClock { ElapsedDays = 11 });

        Assert.False(service.TryWriteCheckpointTarget(new Chapter3CheckpointTarget("barn", 12)));
        var target = service.ReadCheckpointTarget();
        Assert.Null(target.PropertyCode);
        Assert.Null(target.PickDay);
    }

    private static Chapter3RuntimeService ActiveRuntimeService()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, new FakeClock { ElapsedDays = 11 });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
        Assert.NotNull(service.State);
        return service;
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public int ReadCount { get; private set; }
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, PlayerId, System.IO.Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            ReadCount++;
            snapshot = Snapshot;
            return Status;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3State? Stored { get; set; }
        public string? LoadFailureContents { get; set; }
        public string? PreservedRawContents { get; private set; }
        public int UpdateCallCount { get; private set; }

        public Chapter3StoreLoadResult Load()
        {
            if (LoadFailureContents is not null)
                return new(false, Chapter3StoreLoadStatus.Failed, null, Chapter3StoreFailureReason.InvalidState, "Fake corrupt sidecar.", LoadFailureContents);
            return Stored is null
                ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
                : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");
        }

        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            UpdateCallCount++;
            Stored = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
        }

        public Chapter3StoreUpdateResult PreserveRawContents(string rawContents)
        {
            PreservedRawContents = rawContents;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
        }
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; }
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public bool Readable { get; set; } = true;
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return Readable;
        }

        public void Raise() => DayPassed?.Invoke();
    }
}
