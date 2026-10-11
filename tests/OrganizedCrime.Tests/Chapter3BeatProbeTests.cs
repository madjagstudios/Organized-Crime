using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

// Covers the owner QA beat probe (OwnerQaKeys only).
public sealed class Chapter3BeatProbeTests
{
    private const string PlayerId = "76561190000000001";
    private const int StartElapsedDay = 11;

    [Theory]
    [InlineData("snapshot")]
    public void Beat_probe_forwards_single_word_probes(string probe)
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "beat", "probe", probe }, qaEnabled: true);

        Assert.Equal($"beat-{probe}", Assert.Single(target.Calls));
        Assert.NotEmpty(lines);
    }

    [Fact]
    public void Beat_without_probe_word_returns_usage()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "beat", "snapshot" }, qaEnabled: true);

        Assert.Empty(target.Calls);
        Assert.Contains("oc_ch3 beat probe", Assert.Single(lines));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("ON", true)]
    public void Beat_probe_invincible_forwards_on_and_off(string text, bool on)
    {
        var target = new FakeTarget();

        Chapter3QaCommand.Execute(target, new[] { "beat", "probe", "invincible", text }, qaEnabled: true);

        Assert.Equal($"beat-invincible:{on}", Assert.Single(target.Calls));
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    public void Beat_probe_invincible_refuses_other_words(string text)
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "beat", "probe", "invincible", text }, qaEnabled: true);

        Assert.Empty(target.Calls);
        Assert.Contains("invincible <on | off>", Assert.Single(lines));
    }

    [Fact]
    public void Beat_probe_anim_without_trigger_lists()
    {
        var target = new FakeTarget();

        Chapter3QaCommand.Execute(target, new[] { "beat", "probe", "anim" }, qaEnabled: true);

        Assert.Equal("beat-anim:<list>", Assert.Single(target.Calls));
    }

    [Fact]
    public void Beat_probe_anim_joins_multiword_triggers()
    {
        var target = new FakeTarget();

        Chapter3QaCommand.Execute(target, new[] { "beat", "probe", "anim", "Big", "Punch" }, qaEnabled: true);

        Assert.Equal("beat-anim:Big Punch", Assert.Single(target.Calls));
    }

    [Fact]
    public void Beat_probe_unknown_word_names_itself()
    {
        var target = new FakeTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "beat", "probe", "dance" }, qaEnabled: true);

        Assert.Empty(target.Calls);
        Assert.Contains("'dance'", Assert.Single(lines));
    }

    [Fact]
    public void Snapshot_refuses_while_chapter_3_is_inactive()
    {
        var harness = new Harness();
        harness.Seed();
        // Never activated: no state, no phase.

        var lines = harness.Service.QaBeatSnapshot();

        Assert.Contains("not active", Assert.Single(lines));
    }

    [Fact]
    public void Snapshot_refuses_without_a_wired_world()
    {
        var harness = new Harness();
        harness.Seed();
        harness.Activate();
        harness.Service.BeatProbeWorld = null;

        var lines = harness.Service.QaBeatSnapshot();

        Assert.Contains("not wired up", Assert.Single(lines));
    }

    [Fact]
    public void Snapshot_refuses_when_not_host()
    {
        var harness = new Harness();
        harness.Seed();
        harness.Activate();
        harness.Beat.IsHost = false;

        var lines = harness.Service.QaBeatSnapshot();

        Assert.Contains("host only", Assert.Single(lines));
    }

    [Fact]
    public void Snapshot_reports_the_world_and_changes_no_chapter_3_state()
    {
        var harness = new Harness();
        harness.Seed();
        harness.Activate();
        var before = harness.Repository.Stored;

        var lines = harness.Service.QaBeatSnapshot();

        Assert.Same(before, harness.Repository.Stored);
        Assert.Contains(lines, line => line.Contains("health: 100 / 100"));
        Assert.Contains(lines, line => line.Contains("cash: $500, bank: $12000"));
        Assert.Contains(lines, line => line.Contains("inventory: 2 slots (cocaine x5, jar x1)"));
        Assert.Contains(lines, line => line.Contains("position: (1.0, 2.0, 3.0)"));
        Assert.Contains(lines, line => line.Contains("pursuit: None"));
    }

    [Fact]
    public void Anim_lists_triggers_without_an_argument()
    {
        var harness = new Harness();
        harness.Seed();
        harness.Activate();

        var lines = harness.Service.QaBeatAnim(null);

        Assert.Contains("Punch", Assert.Single(lines));
    }

    [Fact]
    public void Anim_refuses_while_arthur_is_parked()
    {
        var harness = new Harness();
        harness.Seed();
        harness.Activate();

        var lines = harness.Service.QaBeatAnim("Punch");

        Assert.Contains("not out on a visit", Assert.Single(lines));
        Assert.DoesNotContain("anim:Punch", harness.Arthur.Mutations);
    }

    [Fact]
    public void Anim_plays_once_arthur_is_out()
    {
        var harness = new Harness();
        harness.Seed();
        harness.Activate();
        harness.Service.QaArthurCome();

        var lines = harness.Service.QaBeatAnim("Punch");

        Assert.Contains("anim:Punch", harness.Arthur.Mutations);
        Assert.Contains("played Punch", Assert.Single(lines));
    }

    [Fact]
    public void Diff_reports_wake_time_money_inventory_health_and_bill()
    {
        var before = new Chapter3BeatSnapshot(100f, 100f, true, false, 500f, 12000f,
            new[] { new Chapter3BeatSlot("cocaine", 5), new Chapter3BeatSlot("jar", 1) },
            1f, 2f, 3f, 11, "7:30 PM", "None");
        var after = new Chapter3BeatSnapshot(100f, 100f, true, false, 250f, 12000f,
            new[] { new Chapter3BeatSlot("cocaine", 5), new Chapter3BeatSlot("jar", 1) },
            10f, 20f, 30f, 11, "7:35 PM", "None");

        var lines = Chapter3BeatProbe.DescribeDiff(before, after, billShown: true, timedOut: false);

        Assert.Contains(lines, line => line.Contains("revived through the native flow"));
        Assert.Contains(lines, line => line.Contains("woke at (10.0, 20.0, 30.0)"));
        Assert.Contains(lines, line => line.Contains("cash $500 -> $250 (-250)"));
        Assert.Contains(lines, line => line.Contains("bank $12000 -> $12000 (+0)"));
        Assert.Contains(lines, line => line.Contains("2 slots, unchanged"));
        Assert.Contains(lines, line => line.Contains("health after: 100 / 100"));
        Assert.Contains(lines, line => line.Contains("bill screen showed: yes"));
    }

    [Fact]
    public void Diff_reports_inventory_changes_and_timeouts()
    {
        var before = new Chapter3BeatSnapshot(100f, 100f, true, false, 500f, 12000f,
            new[] { new Chapter3BeatSlot("cocaine", 5) }, 1f, 2f, 3f, 11, "7:30 PM", "None");
        var after = new Chapter3BeatSnapshot(0f, 100f, false, false, 500f, 12000f,
            Array.Empty<Chapter3BeatSlot>(), 1f, 2f, 3f, 12, "7:30 PM", "None");

        var lines = Chapter3BeatProbe.DescribeDiff(before, after, billShown: false, timedOut: true);

        Assert.Contains(lines, line => line.Contains("timed out after 60 s"));
        Assert.Contains(lines, line => line.Contains("cocaine 5 -> 0"));
        Assert.Contains(lines, line => line.Contains("bill screen showed: no"));
    }

    [Fact]
    public void Invincible_on_then_off_restores_the_exact_prior_state()
    {
        var world = new FakeBeatWorld { Invincible = false };
        var probe = new Chapter3BeatInvincibleProbe(world);

        var on = probe.TurnOn();
        Assert.True(world.Invincible);
        Assert.Contains("on.", Assert.Single(on));

        var off = probe.TurnOff();
        Assert.False(world.Invincible);
        Assert.Contains("restored", Assert.Single(off));
        Assert.Equal(new[] { true, false }, world.InvincibleSets);
    }

    [Fact]
    public void Invincible_off_without_on_leaves_a_foreign_on_alone()
    {
        var world = new FakeBeatWorld { Invincible = true };
        var probe = new Chapter3BeatInvincibleProbe(world);

        var lines = probe.TurnOff();

        Assert.True(world.Invincible);
        Assert.Empty(world.InvincibleSets);
        Assert.Contains("not by this probe", Assert.Single(lines));
    }

    [Fact]
    public void Invincible_on_when_already_on_writes_nothing_and_off_after_refuses()
    {
        var world = new FakeBeatWorld { Invincible = true };
        var probe = new Chapter3BeatInvincibleProbe(world);

        var on = probe.TurnOn();
        Assert.Contains("already on", Assert.Single(on));
        Assert.Empty(world.InvincibleSets);

        var off = probe.TurnOff();
        Assert.True(world.Invincible);
        Assert.Contains("not by this probe", Assert.Single(off));
    }

    [Fact]
    public void Invincible_off_when_already_off_reports_without_writing()
    {
        var world = new FakeBeatWorld { Invincible = false };
        var probe = new Chapter3BeatInvincibleProbe(world);

        var lines = probe.TurnOff();

        Assert.Contains("already off", Assert.Single(lines));
        Assert.Empty(world.InvincibleSets);
    }

    private sealed class FakeTarget : IChapter3QaTarget
    {
        public Chapter3State? State { get; set; }
        public List<string> Calls { get; } = new();

        public Chapter3QaResult QaAcceptOpenOffer() => new(true, "ok");
        public Chapter3QaResult QaFailActiveJob() => new(true, "ok");
        public Chapter3QaResult QaPayMakeGood(int id) => new(true, "ok");
        public Chapter3QaResult QaRecordWarning(int id) => new(true, "ok");
        public Chapter3QaResult QaSimulateWeekPass() => new(true, "ok");
        public Chapter3QaResult QaPayCut(int weekIndex) => new(true, "ok");
        public Chapter3QaResult QaDeliverActiveJob() => new(true, "ok");
        public Chapter3QaResult QaOfferEmergency() => new(true, "ok");
        public int? QaEmergencyMinutesLeft() => null;
        public Chapter3QaResult QaOfferWarehouse() => new(true, "ok");
        public bool QaWarehouseOwned() => false;
        public Chapter3QaResult QaArthurCome() => new(true, "ok");
        public Chapter3QaResult QaArthurSay(string text) => new(true, "ok");
        public Chapter3QaResult QaArthurLeave() => new(true, "ok");
        public IReadOnlyList<string> QaArthurStatus() => new[] { "ok" };
        public Chapter3QaResult QaArthurHq(string mode) => new(true, "ok");
        public Chapter3QaResult QaArthurHqHere() => new(true, "ok");
        public Chapter3QaResult QaArthurVisit(string kind) => new(true, "ok");
        public Chapter3QaResult QaArthurBeat() => new(true, "ok");

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

        public IReadOnlyList<string> QaCapacityStatus() => new[] { "capacity" };
        public Chapter3QaResult QaCapacityUnlock() => new(true, "ok");
        public Chapter3QaResult QaCapacitySet(int tier) => new(true, "ok");
    }

    private sealed class Harness
    {
        public DeadDropFakeWorld World { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = StartElapsedDay };
        public FakeArthurWorld Arthur { get; } = new();
        public FakeBeatWorld Beat { get; } = new();
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.DeadDropWorld = World;
            Service.ArthurWorld = Arthur;
            Service.ArthurClock = () => 0f;
            Service.BeatProbeWorld = Beat;
        }

        public void Seed() =>
            Repository.Stored = new Chapter3State(
                PlayerId, Chapter3Phase.Active, StartElapsedDay, StartElapsedDay + 5, 0,
                Chapter3JobBoard.Empty, Chapter3MakeGoodLedger.Empty);

        public void Activate()
        {
            Clock.ElapsedDays = StartElapsedDay + 5;
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
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

    private sealed class FakeArthurWorld : IChapter3ArthurWorld
    {
        public bool HasAuthority = true;
        public List<string> Mutations { get; } = new();

        bool IChapter3ArthurWorld.HasAuthority => HasAuthority;

        public bool PollArthurImpact() => false;

        public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
        {
            snapshot = new Chapter3ArthurSnapshot(true, true, true, true, true, (0f, 0f, 0f), 20f, 1, 100f, true);
            return Chapter3ArthurReadStatus.Ready;
        }

        public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason)
        {
            reason = "moved";
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
            reason = "verified";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryComeArthur(out string reason)
        {
            Mutations.Add("come");
            reason = "coming";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason)
        {
            reason = "said";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason)
        {
            reason = "leaving";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryFacePlayer(out string reason)
        {
            reason = "faced";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
        {
            reason = "faced";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)
        {
            local = (0f, 0f, 0f);
            yawDegrees = 0f;
            reason = "captured";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason)
        {
            reason = "placed";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryResumeWalk(out string reason)
        {
            reason = "resumed";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason)
        {
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

    private sealed class FakeBeatWorld : IChapter3BeatProbeWorld
    {
        public bool IsHost = true;
        public bool Invincible;
        public List<bool> InvincibleSets { get; } = new();

        bool IChapter3BeatProbeWorld.IsHost => IsHost;

        public bool TryReadSnapshot(out Chapter3BeatSnapshot snapshot, out string reason)
        {
            snapshot = new Chapter3BeatSnapshot(100f, 100f, true, Invincible, 500f, 12000f,
                new[] { new Chapter3BeatSlot("cocaine", 5), new Chapter3BeatSlot("jar", 1) },
                1f, 2f, 3f, StartElapsedDay + 5, "7:30 PM", "None");
            reason = string.Empty;
            return true;
        }

        public bool TryReadInvincible(out bool on, out string reason)
        {
            on = Invincible;
            reason = string.Empty;
            return true;
        }

        public bool TrySetInvincible(bool on, out string reason)
        {
            Invincible = on;
            InvincibleSets.Add(on);
            reason = string.Empty;
            return true;
        }

    }
}
