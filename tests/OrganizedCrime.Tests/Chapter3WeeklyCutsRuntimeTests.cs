using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3WeeklyCutsRuntimeTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Week_pass_opens_a_cut_when_owned()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            logs.Add, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        logs.Clear();

        clock.RaiseWeek();

        var cut = Assert.Single(service.State!.Cuts.Entries);
        Assert.Equal(1, cut.WeekIndex);
        Assert.Equal(30_000, cut.Amount);
        Assert.Equal(0, cut.DueDay);
        Assert.Equal(2, cut.GraceUntilDay);
        Assert.Equal(Chapter3WeeklyCutStatus.Due, cut.Status);
        Assert.Contains("Chapter 3 weekly cut due: week 1, $30,000, grace until day 2", logs);
    }

    [Fact]
    public void Week_index_comes_from_elapsed_days_over_seven()
    {
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            null, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        clock.ElapsedDays = 18;
        service.OnDayPassed();
        clock.RaiseWeek();

        Assert.Equal(2, Assert.Single(service.State!.Cuts.Entries).WeekIndex);
    }

    [Fact]
    public void Week_pass_opens_nothing_while_not_owned()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            logs.Add, new FakeOwnership { Owned = false });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        logs.Clear();

        clock.RaiseWeek();

        Assert.Empty(service.State!.Cuts.Entries);
        Assert.DoesNotContain(logs, line => line.Contains("weekly cut"));
    }

    [Fact]
    public void Repeated_week_pass_is_a_noop()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            logs.Add, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        clock.RaiseWeek();
        clock.RaiseWeek();

        Assert.Single(service.State!.Cuts.Entries);
        Assert.Single(logs, line => line.StartsWith("Chapter 3 weekly cut due:"));
    }

    [Fact]
    public void Week_pass_before_the_start_is_ignored()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = false }, clock,
            logs.Add, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        clock.RaiseWeek();

        Assert.Empty(service.State!.Cuts.Entries);
        Assert.DoesNotContain(logs, line => line.Contains("weekly cut"));
    }

    [Fact]
    public void Missed_cut_opens_exactly_one_make_good_on_the_day_tick()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            logs.Add, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        clock.RaiseWeek();

        clock.ElapsedDays = 12;
        service.OnDayPassed();
        clock.ElapsedDays = 13;
        service.OnDayPassed();
        Assert.Empty(service.State!.Ledger.Entries);

        clock.ElapsedDays = 14;
        service.OnDayPassed();

        Assert.Equal(Chapter3WeeklyCutStatus.Missed, Assert.Single(service.State.Cuts.Entries).Status);
        var entry = Assert.Single(service.State.Ledger.Entries);
        Assert.Equal(new Chapter3MissedCutSource(1), entry.Source);
        Assert.Equal(37_500, entry.Amount);
        Assert.Contains("Chapter 3 weekly cut missed: week 1, $30,000 unpaid past day 2", logs);
        Assert.Contains("Chapter 3 make-good opened: missed week 1 cut, $37,500, due day 5", logs);

        clock.ElapsedDays = 15;
        service.OnDayPassed();
        Assert.Single(service.State.Ledger.Entries);
    }

    [Fact]
    public void Paid_cut_never_goes_missed_or_opens_a_make_good()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            logs.Add, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        clock.RaiseWeek();

        var pay = service.QaPayCut(1);

        Assert.True(pay.Ok);
        Assert.Contains("Chapter 3 weekly cut paid: week 1, $30,000", logs);

        clock.ElapsedDays = 14;
        service.OnDayPassed();

        Assert.Equal(Chapter3WeeklyCutStatus.Paid, Assert.Single(service.State!.Cuts.Entries).Status);
        Assert.Empty(service.State.Ledger.Entries);
        Assert.DoesNotContain(logs, line => line.Contains("cut missed"));
    }

    [Fact]
    public void Qa_weekpass_simulates_the_next_unrecorded_week()
    {
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            null, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        var first = service.QaSimulateWeekPass();
        var second = service.QaSimulateWeekPass();

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal(new[] { 1, 2 }, service.State!.Cuts.Entries.Select(entry => entry.WeekIndex).ToArray());
        Assert.Contains("week 1", first.Message);
        Assert.Contains("week 2", second.Message);
    }

    [Fact]
    public void Qa_weekpass_refuses_while_not_owned()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, new FakeClock { ElapsedDays = 11 },
            null, new FakeOwnership { Owned = false });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();

        var weekpass = service.QaSimulateWeekPass();

        Assert.False(weekpass.Ok);
        Assert.Contains("not owned", weekpass.Message);
        Assert.Empty(service.State!.Cuts.Entries);
    }

    [Fact]
    public void Qa_paycut_refuses_without_a_due_cut()
    {
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            null, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        clock.RaiseWeek();

        Assert.False(service.QaPayCut(9).Ok);

        Assert.True(service.QaPayCut(1).Ok);
        Assert.False(service.QaPayCut(1).Ok);
    }

    [Fact]
    public void Qa_cut_mutations_bump_the_revision()
    {
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, clock,
            null, new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        var before = service.State!.Revision;

        service.QaSimulateWeekPass();
        Assert.Equal(before + 1, service.State.Revision);

        service.QaPayCut(1);
        Assert.Equal(before + 2, service.State.Revision);
    }

    [Fact]
    public void Factory_constructed_service_runs_weekpass_and_paycut()
    {
        var logs = new List<string>();
        var clock = new FakeClock { ElapsedDays = 11 };
        var service = new Chapter3RuntimeService(
            new FakeContext(),
            _ => new FakeRepository(),
            new FakeGate { Satisfied = false },
            clock,
            logs.Add,
            new FakeOwnership { Owned = true });
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.True(service.TryQaStartNow());

        Assert.True(service.QaSimulateWeekPass().Ok);
        Assert.True(service.QaPayCut(1).Ok);

        Assert.Equal(Chapter3WeeklyCutStatus.Paid, Assert.Single(service.State!.Cuts.Entries).Status);
        Assert.Contains(logs, line => line.StartsWith("Chapter 3 weekly cut due:"));
        Assert.Contains(logs, line => line.StartsWith("Chapter 3 weekly cut paid:"));
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, PlayerId, System.IO.Path.GetTempPath());

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

    private sealed class FakeOwnership : IChapter3WarehouseOwnership
    {
        public bool Owned { get; set; }
        public bool IsOwned() => Owned;
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
        public void RaiseWeek() => WeekPassed?.Invoke();
    }
}
