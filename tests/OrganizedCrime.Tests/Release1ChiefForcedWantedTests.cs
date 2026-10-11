using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-74 (plan item D4): forced Wanted after a day-old unpaid lockdown. Drives
/// <see cref="Release1ChiefService"/> through the real story runtime with a fake world, ledger,
/// and the four forced-wanted seams (host, exposure, curfew, wanted, dispatch), pinning the
/// lockdown clock, the once-per-lockdown warning, every trigger gate, the once-per-night fire,
/// and the dispatch counters. Ledger-level facts pin the pure predicate directly.
/// </summary>
[Collection("OC-151 composition scope")]
public sealed class Release1ChiefForcedWantedTests
{
    // Curfew-night fixtures on the 21:00 anchor (S1API CurfewManager.CurfewStartTime 2100):
    // night key = floor((minutes - 1260) / 1440).
    private const double NightOne2200 = 2760d; // day 1, 22:00 -> night 1
    private const double NightOne0200 = 3000d; // day 2, 02:00 -> night 1 (same window)
    private const double NightTwo2200 = 4200d; // day 2, 22:00 -> night 2

    [Fact]
    public void Night_key_is_the_day_the_curfew_window_started()
    {
        Assert.Equal(-1, Release1ChiefLedger.ForcedWantedNightKey(0d));
        Assert.Equal(-1, Release1ChiefLedger.ForcedWantedNightKey(1259d));
        Assert.Equal(0, Release1ChiefLedger.ForcedWantedNightKey(1260d));
        Assert.Equal(0, Release1ChiefLedger.ForcedWantedNightKey(2699d));
        Assert.Equal(1, Release1ChiefLedger.ForcedWantedNightKey(2700d));
        Assert.Equal(1, Release1ChiefLedger.ForcedWantedNightKey(NightOne2200));
        Assert.Equal(1, Release1ChiefLedger.ForcedWantedNightKey(NightOne0200));
        Assert.Equal(2, Release1ChiefLedger.ForcedWantedNightKey(NightTwo2200));
    }

    [Fact]
    public void Warned_compares_warnings_against_lifts_so_it_rearms_per_lockdown()
    {
        var fresh = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.False(Release1ChiefLedger.IsForcedWantedWarned(fresh));

        var warned = fresh with { ForcedWantedWarnings = 1 };
        Assert.True(Release1ChiefLedger.IsForcedWantedWarned(warned));

        // A lift evens the score: the next lockdown starts unwarned without resetting.
        var lifted = warned with { PaidLifts = 1 };
        Assert.False(Release1ChiefLedger.IsForcedWantedWarned(lifted));
        Assert.True(Release1ChiefLedger.IsForcedWantedWarned(lifted with { ForcedWantedWarnings = 2 }));
    }

    [Fact]
    public void Stamp_lands_on_an_engaged_unstamped_record_only()
    {
        var engaged = Open(1) with { LockdownEngaged = true, LockdownAnnouncements = 1, Revision = 2 };
        var stamped = Release1ChiefLedger.StampLockdownEngaged(engaged, 500d);
        Assert.Equal(500d, stamped.Next.LockdownEngagedAtMinutes);
        Assert.Equal(3, stamped.Next.Revision);

        Assert.Same(engaged, Release1ChiefLedger.StampLockdownEngaged(engaged, double.NaN).Next);
        Assert.Same(engaged, Release1ChiefLedger.StampLockdownEngaged(engaged, -1d).Next);
        Assert.Same(stamped.Next, Release1ChiefLedger.StampLockdownEngaged(stamped.Next, 600d).Next);
        var plain = Open(1);
        Assert.Same(plain, Release1ChiefLedger.StampLockdownEngaged(plain, 500d).Next);
    }

    [Fact]
    public void Warn_fires_once_per_lockdown_at_24h_unpaid()
    {
        var engaged = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1,
            LockdownEngagedAtMinutes = 500d, Revision = 2
        };
        // Too young, and unstamped, warn nothing.
        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.WarnForcedWanted(engaged, 1939d).Action);
        Assert.Equal(Release1ChiefAction.None,
            Release1ChiefLedger.WarnForcedWanted(engaged with { LockdownEngagedAtMinutes = null }, 3000d).Action);

        var warned = Release1ChiefLedger.WarnForcedWanted(engaged, 1940d);
        Assert.Equal(Release1ChiefAction.WarnForcedWanted, warned.Action);
        Assert.Equal(1, warned.Next.ForcedWantedWarnings);
        Assert.True(Release1ChiefLedger.IsForcedWantedWarned(warned.Next));

        // Warned already: a second call finds nothing.
        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.WarnForcedWanted(warned.Next, 3000d).Action);
    }

    [Fact]
    public void A_fire_waits_an_hour_of_game_time_after_the_warning()
    {
        // OC-74 review and owner run: warn and fire used to be able to land on the same pass;
        // a curfew-night guard then held a morning warning back until 21:00, though the lockdown
        // holds curfew on all day. A fire now needs an hour of game time after the warning.
        var engaged = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1,
            LockdownEngagedAtMinutes = 500d, Revision = 2
        };
        var warned = Release1ChiefLedger.WarnForcedWanted(engaged, 2000d);
        Assert.Equal(2000d, warned.Next.ForcedWantedWarnedAtMinutes);

        var outside = new Chapter3ForcedWantedTrigger(
            IsHost: true, CurfewActive: true, Outdoors: true,
            PursuitLevel: Chapter3PursuitLevels.None, Night: Release1ChiefLedger.ForcedWantedNightKey(2000d), Arrested: false);
        Assert.False(Release1ChiefLedger.ShouldFireForcedWanted(warned.Next, 2000d, outside, isProtected: false));
        Assert.False(Release1ChiefLedger.ShouldFireForcedWanted(warned.Next, 2059d, outside, isProtected: false));
        Assert.True(Release1ChiefLedger.ShouldFireForcedWanted(warned.Next, 2060d, outside, isProtected: false));

        // Both lifts clear it with the rest of the lockdown.
        Assert.Null(Release1ChiefLedger.CoolBelowWatched(warned.Next).Next.ForcedWantedWarnedAtMinutes);
    }

    [Fact]
    public void A_save_with_earlier_lifts_is_warned_once_not_once_per_lift()
    {
        // OC-74 owner run: with one paid and one cooled lift already on the record, the old
        // warnings-versus-lifts test sent the warning three times in one pass.
        var engaged = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 3, PaidLifts = 1, CooledLifts = 1,
            LockdownEngagedAtMinutes = 500d, Revision = 2
        };
        var first = Release1ChiefLedger.WarnForcedWanted(engaged, 2000d);
        Assert.Equal(Release1ChiefAction.WarnForcedWanted, first.Action);
        Assert.Equal(1, first.Next.ForcedWantedWarnings);
        Assert.True(Release1ChiefLedger.IsForcedWantedWarned(first.Next));
        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.WarnForcedWanted(first.Next, 2001d).Action);
    }

    [Fact]
    public void Warn_needs_an_unpaid_record()
    {
        var paying = Open(1) with
        {
            State = Release1ChiefState.Paying,
            LockdownEngaged = true, LockdownAnnouncements = 1,
            LockdownEngagedAtMinutes = 500d, Revision = 2,
            AcceptedLogicalCorrelations = new[] { PayCorrelation(1) },
            NativeEffectIds = new[] { "chief-campbell-cash-v1-r1" }
        };

        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.WarnForcedWanted(paying, 3000d).Action);
    }

    [Fact]
    public void Record_fire_counts_each_fire_with_exactly_one_outcome_counter()
    {
        var engaged = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1,
            LockdownEngagedAtMinutes = 500d, ForcedWantedWarnings = 1, Revision = 5
        };
        var fired = Release1ChiefLedger.RecordForcedWantedFire(engaged, 1, dispatched: true);
        Assert.Equal(Release1ChiefAction.FireForcedWanted, fired.Action);
        Assert.Equal(1, fired.Next.LastForcedWantedNight);
        Assert.Equal(1, fired.Next.ForcedWantedFires);
        Assert.Equal(1, fired.Next.ForcedWantedDispatches);
        Assert.Equal(0, fired.Next.ForcedWantedDispatchRefusals);

        var refused = Release1ChiefLedger.RecordForcedWantedFire(engaged, 1, dispatched: false);
        Assert.Equal(1, refused.Next.ForcedWantedFires);
        Assert.Equal(0, refused.Next.ForcedWantedDispatches);
        Assert.Equal(1, refused.Next.ForcedWantedDispatchRefusals);

        // No per-night limit anymore: re-recording the same night counts again.
        var again = Release1ChiefLedger.RecordForcedWantedFire(fired.Next, 1, dispatched: true);
        Assert.Equal(Release1ChiefAction.FireForcedWanted, again.Action);
        Assert.Equal(2, again.Next.ForcedWantedFires);
        Assert.Equal(2, again.Next.ForcedWantedDispatches);
        Assert.Equal(0, again.Next.ForcedWantedDispatchRefusals);

        // A disengaged record records nothing.
        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.RecordForcedWantedFire(Open(1), 1, true).Action);
    }

    [Fact]
    public void Both_lifts_clear_the_stamp_and_the_night_marker_but_keep_the_counters()
    {
        var engaged = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1,
            LockdownEngagedAtMinutes = 500d, ForcedWantedWarnings = 1,
            LastForcedWantedNight = 1, ForcedWantedFires = 2,
            ForcedWantedDispatches = 1, ForcedWantedDispatchRefusals = 1, Revision = 9
        };
        var cooled = Release1ChiefLedger.CoolBelowWatched(engaged);
        Assert.Equal(Release1ChiefAction.LiftCooled, cooled.Action);
        Assert.Null(cooled.Next.LockdownEngagedAtMinutes);
        Assert.Null(cooled.Next.LastForcedWantedNight);
        Assert.Equal(2, cooled.Next.ForcedWantedFires);
        Assert.Equal(1, cooled.Next.ForcedWantedDispatches);
        Assert.Equal(1, cooled.Next.ForcedWantedDispatchRefusals);

        var paying = engaged with
        {
            State = Release1ChiefState.Paying,
            AcceptedLogicalCorrelations = new[] { PayCorrelation(1) },
            NativeEffectIds = new[] { "chief-campbell-cash-v1-r1" }
        };
        var settled = Release1ChiefLedger.Settle(paying);
        Assert.Equal(Release1ChiefAction.LiftPaid, settled.Action);
        Assert.Null(settled.Next.LockdownEngagedAtMinutes);
        Assert.Null(settled.Next.LastForcedWantedNight);
        Assert.Equal(2, settled.Next.ForcedWantedFires);
    }

    [Fact]
    public void Forced_wanted_record_fields_validate()
    {
        var adopted = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Throws<ArgumentException>(() => (adopted with { LockdownEngagedAtMinutes = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (adopted with { LockdownEngagedAtMinutes = -1d }).Validate());
        Assert.Throws<ArgumentException>(() => (adopted with { ForcedWantedWarnings = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (adopted with { ForcedWantedFires = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (adopted with { ForcedWantedDispatches = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (adopted with { ForcedWantedDispatchRefusals = -1 }).Validate());
        // Night -1 is the pre-21:00 slice of day zero; the key math needs it expressible.
        var night = adopted with { LastForcedWantedNight = -1 };
        night.Validate();
        Assert.Equal(-1, night.LastForcedWantedNight);
    }

    [Theory]
    [InlineData("all-true", true)]
    [InlineData("disengaged", false)]
    [InlineData("settled", false)]
    [InlineData("young", false)]
    [InlineData("unstamped", false)]
    [InlineData("unwarned", false)]
    [InlineData("not-host", false)]
    [InlineData("curfew-inactive", false)]
    [InlineData("indoors", false)]
    [InlineData("pursuit-arresting", false)]
    [InlineData("pursuit-unreadable", false)]
    [InlineData("arrested-true", false)]
    [InlineData("arrested-null", false)]
    [InlineData("protected", false)]
    [InlineData("grace", false)]
    [InlineData("minutes-nan", false)]
    [InlineData("minutes-negative", false)]
    public void ShouldFire_predicate_needs_every_gate(string caseName, bool expected)
    {
        var record = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1, LockdownEngagedAtMinutes = 500d,
            ForcedWantedWarnings = 1, ForcedWantedWarnedAtMinutes = 1940d, Revision = 4
        };
        var minutes = 2760d;
        var isProtected = false;
        var trigger = new Chapter3ForcedWantedTrigger(
            IsHost: true, CurfewActive: true, Outdoors: true,
            PursuitLevel: Chapter3PursuitLevels.None, Night: 1, Arrested: false);
        switch (caseName)
        {
            case "disengaged": record = record with { LockdownEngaged = false }; break;
            case "settled": record = record with { State = Release1ChiefState.Settled }; break;
            case "young": minutes = 1939d; break;
            case "unstamped": record = record with { LockdownEngagedAtMinutes = null }; break;
            case "unwarned": record = record with { ForcedWantedWarnings = 0, ForcedWantedWarnedAtMinutes = null }; break;
            case "not-host": trigger = trigger with { IsHost = false }; break;
            case "curfew-inactive": trigger = trigger with { CurfewActive = false }; break;
            case "indoors": trigger = trigger with { Outdoors = false }; break;
            case "pursuit-arresting": trigger = trigger with { PursuitLevel = Chapter3PursuitLevels.Arresting }; break;
            case "pursuit-unreadable": trigger = trigger with { PursuitLevel = null }; break;
            case "arrested-true": trigger = trigger with { Arrested = true }; break;
            case "arrested-null": trigger = trigger with { Arrested = null }; break;
            case "protected": isProtected = true; break;
            case "grace": record = record with { ForcedWantedWarnedAtMinutes = 2750d }; break;
            case "minutes-nan": minutes = double.NaN; break;
            case "minutes-negative": minutes = -5d; break;
        }

        Assert.Equal(expected, Release1ChiefLedger.ShouldFireForcedWanted(record, minutes, trigger, isProtected));
    }

    [Theory]
    [InlineData("all-true", true)]
    [InlineData("disengaged", false)]
    [InlineData("settled", false)]
    [InlineData("protected", false)]
    [InlineData("young", false)]
    [InlineData("unstamped", false)]
    [InlineData("unwarned", false)]
    [InlineData("grace-59", false)]
    [InlineData("grace-60", true)]
    [InlineData("warned-at-null-counter-warned", true)]
    [InlineData("minutes-nan", false)]
    [InlineData("minutes-negative", false)]
    public void IsForcedWantedArmed_needs_every_condition(string caseName, bool expected)
    {
        var record = Open(1) with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1, LockdownEngagedAtMinutes = 500d,
            ForcedWantedWarnings = 1, ForcedWantedWarnedAtMinutes = 1940d, Revision = 4
        };
        var minutes = 2760d;
        var isProtected = false;
        switch (caseName)
        {
            case "disengaged": record = record with { LockdownEngaged = false }; break;
            case "settled": record = record with { State = Release1ChiefState.Settled }; break;
            case "protected": isProtected = true; break;
            case "young": minutes = 1939d; break;
            case "unstamped": record = record with { LockdownEngagedAtMinutes = null }; break;
            case "unwarned": record = record with { ForcedWantedWarnings = 0, ForcedWantedWarnedAtMinutes = null }; break;
            case "grace-59": record = record with { ForcedWantedWarnedAtMinutes = minutes - 59d }; break;
            case "grace-60": record = record with { ForcedWantedWarnedAtMinutes = minutes - 60d }; break;
            case "warned-at-null-counter-warned": record = record with { ForcedWantedWarnedAtMinutes = null }; break;
            case "minutes-nan": minutes = double.NaN; break;
            case "minutes-negative": minutes = -5d; break;
        }

        Assert.Equal(expected, Release1ChiefLedger.IsForcedWantedArmed(record, minutes, isProtected));
    }

    [Theory]
    [InlineData(20f, 30f, 20f)]
    [InlineData(25f, 30f, 20f)]
    [InlineData(30f, 30f, 20f)]
    [InlineData(5f, 8f, 0f)]
    [InlineData(0f, 30f, 0f)]
    public void ClampTimeSinceSighted_holds_below_search_time_minus_ten(float timeSinceSighted, float searchTime, float expected)
    {
        Assert.Equal(expected, Release1ChiefLedger.ClampTimeSinceSighted(timeSinceSighted, searchTime));
    }

    [Fact]
    public void ClampTimeSinceSighted_leaves_non_finite_input_unchanged()
    {
        Assert.True(float.IsNaN(Release1ChiefLedger.ClampTimeSinceSighted(float.NaN, 30f)));
        Assert.Equal(float.PositiveInfinity, Release1ChiefLedger.ClampTimeSinceSighted(float.PositiveInfinity, 30f));
        Assert.Equal(20f, Release1ChiefLedger.ClampTimeSinceSighted(20f, float.NaN));
    }

    [Fact]
    public void Engage_stamps_the_lockdown_clock()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness, atMinutes: 500d);

        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(500d, harness.Record!.LockdownEngagedAtMinutes);
    }

    [Fact]
    public void Engaged_record_without_a_stamp_is_backfilled_never_escalated()
    {
        // An older save's engaged lockdown, or an engage on an unreadable clock: the first
        // readable minute stamps it, so the 24 h warning counts from now, never instantly.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        var demand = harness.Record!;
        Assert.True(harness.Story.TrySetChiefRecord(demand with
        {
            LockdownEngaged = true, LockdownAnnouncements = 1, Revision = demand.Revision + 1
        }).Accepted);
        SetLedgerHeat(harness, 80);
        harness.World.TotalMinutes = 5000d;

        harness.Service.Update();

        Assert.Equal(5000d, harness.Record!.LockdownEngagedAtMinutes);
        Assert.Equal(0, harness.Record!.ForcedWantedWarnings);
        Assert.Equal(0, harness.Record!.ForcedWantedFires);
        Assert.Equal(0, harness.Wanted.SetCalls);
    }

    [Fact]
    public void Warning_lands_at_24h_unpaid_exactly_once()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness, atMinutes: 500d);
        harness.World.TotalMinutes = 1938d;
        Tick(harness); // 1939: a minute short of a full day.
        Assert.Equal(0, harness.Record!.ForcedWantedWarnings);

        Tick(harness); // 1940: exactly 24 h.
        Assert.Equal(1, harness.Record!.ForcedWantedWarnings);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages,
            m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.ForcedWantedWarningText));

        Tick(harness);
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedWarnings);
    }

    [Fact]
    public void Warning_rearms_after_a_lift()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness, atMinutes: 500d);
        AgePastWarning(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedWarnings);
        CoolLockdown(harness);
        Assert.Equal(1, harness.Record!.CooledLifts);

        // A fresh Critical crossing announces again and re-engages; 24 h later the Chief
        // warns again, on the never-resetting counter's next value.
        SetLedgerHeat(harness, 80);
        harness.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        Tick(harness);
        Tick(harness);
        Assert.Equal(2, harness.Record!.LockdownAnnouncements);
        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(harness.PlayerId, harness.Record!);
        Assert.True(harness.Story.TryRecordPresentationReceipt(correlation).Accepted);
        var reengageAt = harness.World.TotalMinutes!.Value + 1d;
        Tick(harness);
        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(reengageAt, harness.Record!.LockdownEngagedAtMinutes);

        harness.World.TotalMinutes = reengageAt + 1440d - 1d;
        Tick(harness);
        Assert.Equal(2, harness.Record!.ForcedWantedWarnings);
    }

    [Fact]
    public void Paying_blocks_the_warning_and_the_fire()
    {
        // Paying is unpaid-false while the lockdown still holds: the warning never lands, so
        // the fire never arms, even days past the 24 h mark. The wipe refusal holds the
        // record in Paying; without it the pass after the debit would settle and lift.
        using var harness = Harness.Create();
        EngageLockdown(harness, atMinutes: 500d);
        Assert.True(harness.Service.TryPay());
        harness.Ledger.WipeResult = new(false, LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, null, "refused");
        harness.World.TotalMinutes = 4000d;

        Tick(harness);

        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(0, harness.Record!.ForcedWantedWarnings);
        Assert.Equal(0, harness.Record!.ForcedWantedFires);
        Assert.Equal(0, harness.Wanted.SetCalls);
    }

    [Fact]
    public void Fire_sets_arresting_reads_back_and_dispatches_exactly_once()
    {
        using var harness = ArmedHarness(NightOne2200);
        var receipts = new List<string>();
        var previous = OrganizedCrimeLog.Receipt;
        OrganizedCrimeLog.Receipt = receipts.Add;
        try { Tick(harness); }
        finally { OrganizedCrimeLog.Receipt = previous; }

        var record = harness.Record!;
        Assert.Equal(1, record.ForcedWantedFires);
        Assert.Equal(1, record.ForcedWantedDispatches);
        Assert.Equal(0, record.ForcedWantedDispatchRefusals);
        Assert.Equal(1, record.LastForcedWantedNight);
        Assert.Equal(1, harness.Wanted.SetCalls);
        Assert.Equal(1, harness.Dispatch.Calls);
        Assert.Equal("chief-forced-wanted-500-1", harness.Dispatch.LastCorrelation);
        Assert.Contains(receipts, r => r.Contains("Arresting", StringComparison.Ordinal));
        Assert.Contains(receipts, r => r.Contains("fire 1", StringComparison.Ordinal));
        Assert.Contains(receipts, r => r.Contains("dispatch accepted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-host")]
    [InlineData("curfew-inactive")]
    [InlineData("curfew-unreadable")]
    [InlineData("indoors")]
    [InlineData("exposure-unreadable")]
    [InlineData("pursuit-arresting")]
    [InlineData("arrested-true")]
    [InlineData("arrested-null")]
    [InlineData("protected")]
    [InlineData("young")]
    [InlineData("minutes-unreadable")]
    [InlineData("not-engaged")]
    public void Each_trigger_input_alone_blocks_a_fire(string caseName)
    {
        using var harness = caseName switch
        {
            "young" => YoungHarness(),
            "not-engaged" => UnengagedHarness(),
            _ => ArmedHarness(NightOne2200)
        };
        switch (caseName)
        {
            case "not-host": harness.IsHost = false; break;
            case "curfew-inactive": harness.Curfew.Active = false; break;
            case "curfew-unreadable": harness.Curfew.Readable = false; break;
            case "indoors": harness.Exposure.Outdoors = false; break;
            case "exposure-unreadable": harness.Exposure.Readable = false; break;
            case "pursuit-arresting": harness.Exposure.PursuitLevel = Chapter3PursuitLevels.Arresting; break;
            case "arrested-true": harness.Exposure.Arrested = true; break;
            case "arrested-null": harness.Exposure.Arrested = null; break;
            case "protected": Cover(harness, untilDay: 30); break;
            case "minutes-unreadable":
                harness.World.TotalMinutes = null;
                harness.Service.Update();
                Assert.Equal(0, harness.Record!.ForcedWantedFires);
                Assert.Equal(0, harness.Wanted.SetCalls);
                Assert.Equal(0, harness.Dispatch.Calls);
                return;
        }

        Tick(harness);

        Assert.Equal(0, harness.Record!.ForcedWantedFires);
        Assert.Equal(0, harness.Wanted.SetCalls);
        Assert.Equal(0, harness.Dispatch.Calls);
    }

    [Fact]
    public void Fires_again_on_the_same_day_once_pursuit_is_none_with_a_unique_correlation()
    {
        using var harness = ArmedHarness(NightOne2200);
        Tick(harness); // night 1 fires.
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(1, harness.Record!.LastForcedWantedNight);
        Assert.Equal("chief-forced-wanted-500-1", harness.Dispatch.LastCorrelation);

        // At Arresting the hold owns the pursuit: no re-fire while wanted.
        harness.Exposure.PursuitLevel = Chapter3PursuitLevels.Arresting;
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(1, harness.Wanted.SetCalls);

        // The player escapes (pursuit None again) inside the same window: it re-fires.
        harness.Exposure.PursuitLevel = Chapter3PursuitLevels.None;
        harness.World.TotalMinutes = NightOne0200 - 1d;
        Tick(harness);
        Assert.Equal(2, harness.Record!.ForcedWantedFires);
        Assert.Equal(1, harness.Record!.LastForcedWantedNight);
        Assert.Equal(2, harness.Wanted.SetCalls);
        Assert.Equal(2, harness.Dispatch.Calls);
        Assert.Equal("chief-forced-wanted-500-2", harness.Dispatch.LastCorrelation);

        // The next curfew night fires again with its own correlation.
        harness.World.TotalMinutes = NightTwo2200 - 1d;
        Tick(harness);
        Assert.Equal(3, harness.Record!.ForcedWantedFires);
        Assert.Equal(2, harness.Record!.LastForcedWantedNight);
        Assert.Equal(3, harness.Wanted.SetCalls);
        Assert.Equal(3, harness.Dispatch.Calls);
        Assert.Equal("chief-forced-wanted-500-3", harness.Dispatch.LastCorrelation);
    }

    [Theory]
    [InlineData("over", true)]
    [InlineData("under", false)]
    [InlineData("at-limit", false)]
    [InlineData("none", false)]
    [InlineData("investigating", false)]
    [InlineData("unarmed-young", false)]
    [InlineData("protected", false)]
    [InlineData("paid", false)]
    [InlineData("not-host", false)]
    [InlineData("unreadable-clock", false)]
    [InlineData("unreadable-hold", false)]
    public void Hold_writes_only_when_armed_wanted_and_over_the_limit(string caseName, bool expectWrite)
    {
        using var harness = caseName == "unarmed-young" ? YoungHarness() : ArmedHarness(NightOne2200);
        switch (caseName)
        {
            case "protected": Cover(harness, untilDay: 30); break;
            case "paid": Assert.True(harness.Service.TryPay()); break;
            case "not-host": harness.IsHost = false; break;
            case "unreadable-clock":
                harness.World.TotalMinutes = null;
                harness.Service.Update();

                Assert.Empty(harness.Hold.Writes);
                Assert.Equal(0, harness.Hold.ReadCalls);
                return;
        }

        harness.Hold.PursuitLevel = Chapter3PursuitLevels.Arresting;
        harness.Hold.TimeSinceSighted = 25f;
        harness.Hold.SearchTime = 30f;
        switch (caseName)
        {
            case "under": harness.Hold.TimeSinceSighted = 5f; break;
            case "at-limit": harness.Hold.TimeSinceSighted = 20f; break;
            case "none": harness.Hold.PursuitLevel = Chapter3PursuitLevels.None; break;
            case "investigating": harness.Hold.PursuitLevel = 1; break;
            case "unreadable-hold": harness.Hold.Readable = false; break;
        }

        Tick(harness);

        Assert.Equal(expectWrite ? 1 : 0, harness.Hold.Writes.Count);
        if (expectWrite) Assert.Equal(20f, Assert.Single(harness.Hold.Writes));
    }

    [Fact]
    public void Hold_runs_on_a_tick_the_throttle_skips()
    {
        using var harness = ArmedHarness(NightOne2200);
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Empty(harness.Hold.Writes);

        // Same game minute, so the pass may still run once on the moved revision; Arresting
        // blocks the re-fire while the hold clamps.
        harness.Exposure.PursuitLevel = Chapter3PursuitLevels.Arresting;
        harness.Hold.PursuitLevel = Chapter3PursuitLevels.Arresting;
        harness.Hold.TimeSinceSighted = 25f;
        harness.Service.Update();
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(20f, Assert.Single(harness.Hold.Writes));

        // Back to None: if this same-minute pass ran, it would fire. It stays skipped while
        // the hold still clamps, proving the hold runs ahead of the throttle.
        harness.Exposure.PursuitLevel = Chapter3PursuitLevels.None;
        harness.Service.Update();
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(1, harness.Wanted.SetCalls);
        Assert.Equal(1, harness.Dispatch.Calls);
        Assert.Equal(2, harness.Hold.Writes.Count);
        Assert.All(harness.Hold.Writes, w => Assert.Equal(20f, w));
    }

    [Fact]
    public void Hold_failures_warn_once_per_session_until_the_next_load()
    {
        using var harness = ArmedHarness(NightOne2200);
        harness.Hold.PursuitLevel = Chapter3PursuitLevels.Arresting;
        harness.Hold.TimeSinceSighted = 25f;
        harness.Hold.Readable = false;
        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            Tick(harness);
            harness.Service.Update(); // same minute: skipped pass, failing hold.
            harness.Hold.Readable = true;
            harness.Hold.Writable = false;
            Tick(harness); // the write fails too, still one warning.
        }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.Single(warnings, w => w.Contains("hold", StringComparison.OrdinalIgnoreCase));

        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            harness.Service.OnPreLoad();
            Tick(harness);
        }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.Equal(2, warnings.Count(w => w.Contains("hold", StringComparison.OrdinalIgnoreCase)));
    }
    [Fact]
    public void A_set_that_does_not_read_back_is_not_fired_and_retries()
    {
        using var harness = ArmedHarness(NightOne2200);
        harness.Wanted.Succeed = false;

        Tick(harness);
        Assert.Equal(0, harness.Record!.ForcedWantedFires);
        Assert.Null(harness.Record!.LastForcedWantedNight);
        Assert.Equal(0, harness.Dispatch.Calls);

        harness.Wanted.Succeed = true;
        harness.Wanted.ReadBack = Chapter3PursuitLevels.None;
        Tick(harness);
        Assert.Equal(0, harness.Record!.ForcedWantedFires);
        Assert.Null(harness.Record!.LastForcedWantedNight);
        Assert.Equal(0, harness.Dispatch.Calls);

        harness.Wanted.ReadBack = Chapter3PursuitLevels.Arresting;
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(1, harness.Record!.LastForcedWantedNight);
        Assert.Equal(1, harness.Dispatch.Calls);
    }

    [Fact]
    public void A_short_pool_fires_without_a_dispatch()
    {
        using var harness = ArmedHarness(NightOne2200);
        harness.Dispatch.Respond = correlation => new(
            NativeLawResponseResultState.UnavailableCapacity, correlation,
            "station preflight capacity was unavailable",
            new NativeLawResponseDiagnostics("station-1", 1, null, 0, null));
        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try { Tick(harness); }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(0, harness.Record!.ForcedWantedDispatches);
        Assert.Equal(1, harness.Record!.ForcedWantedDispatchRefusals);
        Assert.Equal(1, harness.Record!.LastForcedWantedNight);
        Assert.Contains(warnings, w => w.Contains("pool short, no dispatch", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_dispatch_counts_refused_and_logs_the_reason()
    {
        using var harness = ArmedHarness(NightOne2200);
        harness.Dispatch.Respond = correlation => new(
            NativeLawResponseResultState.Rejected, correlation, "MorePatrols is loaded; coexistence is not approved");
        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try { Tick(harness); }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.Equal(0, harness.Record!.ForcedWantedDispatches);
        Assert.Equal(1, harness.Record!.ForcedWantedDispatchRefusals);
        Assert.Contains(warnings, w => w.Contains("MorePatrols", StringComparison.Ordinal));
    }

    [Fact]
    public void Paid_lift_clears_the_stamp_and_the_night_marker()
    {
        using var harness = ArmedHarness(NightOne2200);
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        Assert.True(harness.Service.TryPay());
        Tick(harness);

        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.Equal(1, harness.Record!.PaidLifts);
        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Null(harness.Record!.LockdownEngagedAtMinutes);
        Assert.Null(harness.Record!.LastForcedWantedNight);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);

        Tick(harness);
        Assert.Equal(1, harness.Wanted.SetCalls);
    }

    [Fact]
    public void Cooled_lift_clears_the_stamp_and_the_night_marker()
    {
        using var harness = ArmedHarness(NightOne2200);
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);
        SetLedgerHeat(harness, 0);
        Tick(harness);

        Assert.Equal(1, harness.Record!.CooledLifts);
        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Null(harness.Record!.LockdownEngagedAtMinutes);
        Assert.Null(harness.Record!.LastForcedWantedNight);
        Assert.Equal(1, harness.Record!.ForcedWantedFires);

        Tick(harness);
        Assert.Equal(1, harness.Wanted.SetCalls);
    }

    [Fact]
    public void Qa_lockdown_age_backdates_the_stamp_and_warns_on_the_next_pass()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness, atMinutes: 500d);
        harness.World.TotalMinutes = 2000d;

        var result = harness.Service.QaLockdownAge(24d);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(560d, harness.Record!.LockdownEngagedAtMinutes);
        Tick(harness);
        Assert.Equal(1, harness.Record!.ForcedWantedWarnings);
    }

    [Fact]
    public void Qa_lockdown_age_is_refused_without_a_lockdown_a_clock_or_a_sane_age()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        Assert.False(harness.Service.QaLockdownAge(24d).Ok);

        EngageLockdown(harness, atMinutes: 500d);
        harness.World.TotalMinutes = null;
        Assert.False(harness.Service.QaLockdownAge(24d).Ok);
        harness.World.TotalMinutes = 1000d;
        Assert.False(harness.Service.QaLockdownAge(-1d).Ok);
        Assert.False(harness.Service.QaLockdownAge(24d).Ok);
        Assert.Equal(500d, harness.Record!.LockdownEngagedAtMinutes);
    }

    [Fact]
    public void Qa_forced_wanted_snapshot_reports_every_input()
    {
        using var harness = ArmedHarness(NightOne2200);
        var before = harness.Service.QaForcedWantedSnapshot();
        Assert.True(before.HasRecord);
        Assert.True(before.LockdownEngaged);
        // Arm leaves the clock a minute before the fire, so the next tick lands on it.
        Assert.Equal((NightOne2200 - 1d - 500d) / 60d, before.LockdownAgeHours);
        Assert.True(before.Warned);
        Assert.Equal(true, before.CurfewActive);
        Assert.Equal(true, before.Outdoors);
        Assert.False(before.Protected);
        Assert.Equal(Chapter3PursuitLevels.None, before.PursuitLevel);
        Assert.Null(before.LastNightFired);
        Assert.Equal(0, before.Fires);
        Assert.True(before.Armed);
        Assert.False(before.Held);
        Assert.Equal(5f, before.TimeSinceSighted);
        Assert.Equal(30f, before.SearchTime);
        Assert.Equal(false, before.Arrested);

        Tick(harness);
        harness.Hold.PursuitLevel = Chapter3PursuitLevels.Arresting;
        var after = harness.Service.QaForcedWantedSnapshot();
        Assert.Equal(1, after.LastNightFired);
        Assert.Equal(1, after.Fires);
        Assert.Equal(1, after.Dispatches);
        Assert.Equal(0, after.Refusals);
        Assert.True(after.Armed);
        Assert.True(after.Held);
        Assert.Equal(5f, after.TimeSinceSighted);
        Assert.Equal(30f, after.SearchTime);
        Assert.Equal(false, after.Arrested);
    }

    [Fact]
    public void Qa_forced_wanted_snapshot_without_a_record_reads_empty()
    {
        using var harness = Harness.Create();
        var snapshot = harness.Service.QaForcedWantedSnapshot();

        Assert.False(snapshot.HasRecord);
        Assert.False(snapshot.LockdownEngaged);
        Assert.Null(snapshot.LockdownAgeHours);
        Assert.False(snapshot.Warned);
        Assert.Null(snapshot.CurfewActive);
        Assert.Null(snapshot.Outdoors);
        Assert.False(snapshot.Protected);
        Assert.Null(snapshot.PursuitLevel);
        Assert.Null(snapshot.LastNightFired);
        Assert.Equal(0, snapshot.Fires);
        Assert.Equal(0, snapshot.Dispatches);
        Assert.Equal(0, snapshot.Refusals);
        Assert.False(snapshot.Armed);
        Assert.False(snapshot.Held);
        Assert.Null(snapshot.TimeSinceSighted);
        Assert.Null(snapshot.SearchTime);
        Assert.Null(snapshot.Arrested);
    }

    [Fact]
    public void Forced_wanted_flows_through_the_chief_composition()
    {
        // The OC-118 lesson: every input the model tests pass must also be passed at the
        // production call site. This drives the real composition (the constructor Mod.cs
        // uses) with a fake behind every seam and proves each one reaches the reconcile.
        using var driver = CompositionHarness.Create();
        Arm(driver, NightOne2200);
        Tick(driver);

        Assert.Equal(1, driver.Record!.ForcedWantedWarnings);
        Assert.Equal(1, driver.Record!.ForcedWantedFires);
        Assert.Equal(1, driver.Record!.ForcedWantedDispatches);
        Assert.Equal(1, driver.Record!.LastForcedWantedNight);
        Assert.True(driver.HostReads > 0);
        Assert.True(driver.Exposure.Calls > 0);
        Assert.True(driver.Curfew.Calls > 0);
        Assert.True(driver.Wanted.SetCalls > 0);
        Assert.True(driver.Dispatch.Calls > 0);
    }

    [Fact]
    public void Forced_wanted_hold_and_arrest_read_flow_through_the_chief_composition()
    {
        // The OC-118 lesson again: the hold adapter and the arrested-aware exposure read
        // reach the service through the real composition, not just the direct harness.
        using var driver = CompositionHarness.Create();
        Arm(driver, NightOne2200);
        driver.Hold.PursuitLevel = Chapter3PursuitLevels.Arresting;
        driver.Hold.TimeSinceSighted = 25f;
        Tick(driver);

        Assert.Equal(1, driver.Record!.ForcedWantedFires);
        Assert.Equal("chief-forced-wanted-500-1", driver.Dispatch.LastCorrelation);
        Assert.Equal(20f, Assert.Single(driver.Hold.Writes));

        driver.Exposure.Arrested = true;
        Tick(driver);
        Assert.Equal(1, driver.Record!.ForcedWantedFires);
    }

    private static Release1ChiefRecord Open(int round) => Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
    {
        State = Release1ChiefState.DemandOpen,
        DemandRound = round,
        Revision = 1
    };

    private static string PayCorrelation(int round) =>
        Release1LogicalCorrelation.Create(
            "76561190000000001", Release1MissionCatalog.ChiefCampbell, round,
            Release1TransitionKind.ChiefPaymentAccepted, $"chief-pay-r{round}").Value;

    private static void Tick(IDriver driver, double minutes = 1d)
    {
        driver.World.TotalMinutes = (driver.World.TotalMinutes ?? 0d) + minutes;
        driver.Pass();
    }

    private static void SetLedgerHeat(IDriver driver, int heat) =>
        driver.Ledger.State = new LocalPressureState(
            heat, true, driver.Ledger.State?.LastEvidenceGameTime, driver.Ledger.State?.QuietGraceUntil, driver.Ledger.State?.LastDecayEvaluation,
            driver.PlayerId, driver.Ledger.State?.Region, driver.Ledger.State?.PropertyCode, (driver.Ledger.State?.Revision ?? 0) + 1);

    private static void EngageLockdown(IDriver driver, double atMinutes)
    {
        Tick(driver); // adopt
        driver.Arrest();
        Tick(driver); // the demand opens
        SetLedgerHeat(driver, 80);
        driver.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        Tick(driver); // drains ArrestObserved
        Tick(driver); // drains RoseToCritical: the announcement
        Assert.Equal(1, driver.Record!.LockdownAnnouncements);
        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(driver.PlayerId, driver.Record!);
        Assert.True(driver.Story.TryRecordPresentationReceipt(correlation).Accepted);
        driver.World.TotalMinutes = atMinutes - 1d;
        Tick(driver);
        Assert.True(driver.Record!.LockdownEngaged);
    }

    private static void AgePastWarning(IDriver driver)
    {
        driver.Curfew.Active = false;
        var stamp = driver.Record!.LockdownEngagedAtMinutes!.Value;
        driver.World.TotalMinutes = stamp + 1440d - 1d;
        Tick(driver);
        driver.Curfew.Active = true;
    }

    private static void CoolLockdown(IDriver driver)
    {
        SetLedgerHeat(driver, 0);
        Tick(driver);
        Assert.False(driver.Record!.LockdownEngaged);
    }

    private static void Cover(IDriver driver, int untilDay)
    {
        var record = driver.Record!;
        Assert.True(driver.Story.TrySetChiefRecord(record with
        {
            ProtectedUntilDay = untilDay,
            Revision = record.Revision + 1
        }).Accepted);
    }

    private static void Arm(IDriver driver, double fireMinutes)
    {
        EngageLockdown(driver, atMinutes: 500d);
        driver.Curfew.Active = false;
        // OC-74 review: the fire waits an hour of game time after the warning, so the warning
        // lands two hours before the fire.
        driver.World.TotalMinutes = fireMinutes - 121d;
        Tick(driver); // the warning lands; the curfew read holds the fire back.
        Assert.Equal(1, driver.Record!.ForcedWantedWarnings);
        Assert.Equal(0, driver.Record!.ForcedWantedFires);
        Assert.Equal(fireMinutes - 120d, driver.Record!.ForcedWantedWarnedAtMinutes);
        driver.World.TotalMinutes = fireMinutes - 1d;
        driver.Curfew.Active = true;
    }

    private static Harness ArmedHarness(double fireMinutes)
    {
        var harness = Harness.Create();
        Arm(harness, fireMinutes);
        return harness;
    }

    private static Harness YoungHarness()
    {
        var harness = Harness.Create();
        EngageLockdown(harness, atMinutes: 500d);
        harness.World.TotalMinutes = 1938d; // the test's tick lands at 1939, a minute short.
        return harness;
    }

    private static Harness UnengagedHarness()
    {
        var harness = Harness.Create();
        harness.Pass();
        harness.Arrest();
        harness.Pass();
        harness.World.TotalMinutes = NightOne2200 - 1d;
        return harness;
    }

    private interface IDriver : IDisposable
    {
        Release1StoryRuntimeService Story { get; }
        FakeWorld World { get; }
        Release1ChiefTierObserver Observer { get; }
        FakeLedger Ledger { get; }
        Release1ChiefService Service { get; }
        FakeExposure Exposure { get; }
        FakeCurfew Curfew { get; }
        FakeWanted Wanted { get; }
        FakeDispatch Dispatch { get; }
        bool IsHost { get; set; }
        int HostReads { get; }
        string PlayerId { get; }
        Release1ChiefRecord? Record { get; }
        void Arrest(LocalPressureTier previousTier = LocalPressureTier.Quiet, LocalPressureTier currentTier = LocalPressureTier.Quiet);
        void Pass();
    }

    private sealed class HostFlag
    {
        public bool Value = true;
        public int Reads;
        public bool Read() { Reads++; return Value; }
    }

    private sealed class Harness : IDriver
    {
        private Harness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyDepositTests.FakeContext context,
            FakeWorld world,
            Release1ChiefTierObserver observer,
            FakeLedger ledger,
            Release1ChiefService service,
            FakeExposure exposure,
            FakeCurfew curfew,
            FakeWanted wanted,
            FakeDispatch dispatch,
            FakePursuitHold hold,
            HostFlag host)
        {
            Story = story;
            Context = context;
            World = world;
            Observer = observer;
            Ledger = ledger;
            Service = service;
            Exposure = exposure;
            Curfew = curfew;
            Wanted = wanted;
            Dispatch = dispatch;
            Hold = hold;
            Host = host;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyDepositTests.FakeContext Context { get; }
        public FakeWorld World { get; }
        public Release1ChiefTierObserver Observer { get; }
        public FakeLedger Ledger { get; }
        public Release1ChiefService Service { get; }
        public FakeExposure Exposure { get; }
        public FakeCurfew Curfew { get; }
        public FakeWanted Wanted { get; }
        public FakeDispatch Dispatch { get; }
        public FakePursuitHold Hold { get; }
        private HostFlag Host { get; }
        public bool IsHost { get => Host.Value; set => Host.Value = value; }
        public int HostReads => Host.Reads;

        public string PlayerId => Context.Snapshot.PlayerId;
        public Release1ChiefRecord? Record => Story.State?.ChiefRecord;

        public void Arrest(LocalPressureTier previousTier = LocalPressureTier.Quiet, LocalPressureTier currentTier = LocalPressureTier.Quiet) =>
            Observer.Publish(new(
                Context.Snapshot.SessionEpoch, Context.Snapshot.LoadEpoch, PlayerId, null,
                $"arrest-{Guid.NewGuid()}", previousTier, currentTier, null, null));

        public void Pass() => Service.Update();

        public static Harness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            const string receipt = "chief-forced-wanted-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var world = new FakeWorld();
            var observer = new Release1ChiefTierObserver();
            var ledger = new FakeLedger
            {
                SessionEpoch = context.Snapshot.SessionEpoch,
                LoadEpoch = context.Snapshot.LoadEpoch,
                State = LocalPressureState.Quiet(context.Snapshot.PlayerId)
            };
            var exposure = new FakeExposure();
            var curfew = new FakeCurfew();
            var wanted = new FakeWanted();
            var dispatch = new FakeDispatch();
            var hold = new FakePursuitHold();
            var host = new HostFlag();
            var service = new Release1ChiefService(
                story, world, observer, ledger.Read, ledger.Wipe, ledger.Epochs, LocalPressureProfile.Moderate,
                isAuthoritativeHost: () => host.Read(),
                exposureReader: exposure,
                curfewReader: curfew,
                wantedWriter: wanted,
                forcedWantedDispatch: dispatch,
                pursuitHold: hold);
            return new Harness(story, context, world, observer, ledger, service, exposure, curfew, wanted, dispatch, hold, host);
        }

        public void Dispose()
        {
            Service.Dispose();
            Story.Dispose();
        }
    }

    private sealed class CompositionHarness : IDriver
    {
        private CompositionHarness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyDepositTests.FakeContext context,
            FakeWorld world,
            Release1ChiefTierObserver observer,
            FakeLedger ledger,
            Release1ChiefComposition composition,
            FakeExposure exposure,
            FakeCurfew curfew,
            FakeWanted wanted,
            FakeDispatch dispatch,
            FakePursuitHold hold,
            HostFlag host)
        {
            Story = story;
            Context = context;
            World = world;
            Observer = observer;
            Ledger = ledger;
            Composition = composition;
            Exposure = exposure;
            Curfew = curfew;
            Wanted = wanted;
            Dispatch = dispatch;
            Hold = hold;
            Host = host;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyDepositTests.FakeContext Context { get; }
        public FakeWorld World { get; }
        public Release1ChiefTierObserver Observer { get; }
        public FakeLedger Ledger { get; }
        public Release1ChiefComposition Composition { get; }
        public Release1ChiefService Service => Composition.Service;
        public FakeExposure Exposure { get; }
        public FakeCurfew Curfew { get; }
        public FakeWanted Wanted { get; }
        public FakeDispatch Dispatch { get; }
        public FakePursuitHold Hold { get; }
        private HostFlag Host { get; }
        public bool IsHost { get => Host.Value; set => Host.Value = value; }
        public int HostReads => Host.Reads;

        public string PlayerId => Context.Snapshot.PlayerId;
        public Release1ChiefRecord? Record => Story.State?.ChiefRecord;

        public void Arrest(LocalPressureTier previousTier = LocalPressureTier.Quiet, LocalPressureTier currentTier = LocalPressureTier.Quiet) =>
            Observer.Publish(new(
                Context.Snapshot.SessionEpoch, Context.Snapshot.LoadEpoch, PlayerId, null,
                $"arrest-{Guid.NewGuid()}", previousTier, currentTier, null, null));

        public void Pass() => Composition.Update();

        public static CompositionHarness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            const string receipt = "chief-forced-wanted-composition-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var world = new FakeWorld();
            var observer = new Release1ChiefTierObserver();
            var ledger = new FakeLedger
            {
                SessionEpoch = context.Snapshot.SessionEpoch,
                LoadEpoch = context.Snapshot.LoadEpoch,
                State = LocalPressureState.Quiet(context.Snapshot.PlayerId)
            };
            var exposure = new FakeExposure();
            var curfew = new FakeCurfew();
            var wanted = new FakeWanted();
            var dispatch = new FakeDispatch();
            var hold = new FakePursuitHold();
            var host = new HostFlag();
            var composition = new Release1ChiefComposition(
                story, world, observer, ledger.Read, ledger.Wipe, ledger.Epochs,
                isAuthoritativeHost: () => host.Read(),
                exposureReader: exposure,
                curfewReader: curfew,
                wantedWriter: wanted,
                forcedWantedDispatch: dispatch,
                pursuitHold: hold);
            return new CompositionHarness(story, context, world, observer, ledger, composition, exposure, curfew, wanted, dispatch, hold, host);
        }

        public void Dispose()
        {
            Composition.Dispose();
            Story.Dispose();
        }
    }

    private sealed class FakeLedger
    {
        public LocalPressureState? State { get; set; }
        public int WipeCalls { get; private set; }
        public LocalPressureEvidenceWriteResult WipeResult { get; set; } =
            new(true, LocalPressureEvidenceWriteRejectReason.None, null, "wiped");
        public Guid SessionEpoch { get; set; }
        public long LoadEpoch { get; set; }

        public LocalPressureState? Read(string playerId) => State;

        public LocalPressureEvidenceWriteResult Wipe(string playerId)
        {
            WipeCalls++;
            return WipeResult;
        }

        public bool Epochs(out Guid sessionEpoch, out long loadEpoch)
        {
            sessionEpoch = SessionEpoch;
            loadEpoch = LoadEpoch;
            return true;
        }
    }

    private sealed class FakeWorld : Release1ChiefWorldStub
    {
        public float CashBalance { get; set; } = 100_000f;
        public double? TotalMinutes { get; set; } = 0d;
        public int DebitCalls { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus DebitResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public int EngageCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            if (TotalMinutes is { } minutes)
            {
                totalMinutes = minutes;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public override Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = CashBalance;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
        {
            DebitCalls++;
            if (DebitResult == Release1SmallCourtesyWorldMutationStatus.Succeeded) CashBalance += amount;
            return DebitResult;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            EngageCalls++;
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            ReleaseCalls++;
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
    }

    private sealed class FakeExposure : IChapter3PlayerExposureReader
    {
        public bool Readable = true;
        public bool OnFoot = true;
        public bool Outdoors = true;
        public int? PursuitLevel = Chapter3PursuitLevels.None;
        public bool? Arrested = false;
        public int Calls;

        public bool TryRead(out Chapter3PlayerExposure exposure)
        {
            Calls++;
            exposure = Readable
                ? new Chapter3PlayerExposure(OnFoot, Outdoors, PursuitLevel, Arrested, "fake exposure")
                : new Chapter3PlayerExposure(false, false, null, null, "fake exposure unreadable");
            return Readable;
        }
    }

    private sealed class FakePursuitHold : IChapter3PursuitHold
    {
        public bool Readable = true;
        public bool Writable = true;
        public int PursuitLevel = Chapter3PursuitLevels.None;
        public float TimeSinceSighted = 5f;
        public float SearchTime = 30f;
        public int ReadCalls;
        public readonly List<float> Writes = new();

        public bool TryRead(out int pursuitLevel, out float timeSinceSighted, out float searchTime)
        {
            ReadCalls++;
            pursuitLevel = PursuitLevel;
            timeSinceSighted = TimeSinceSighted;
            searchTime = SearchTime;
            return Readable;
        }

        public bool TryWriteTimeSinceSighted(float value)
        {
            if (Writable) Writes.Add(value);
            return Writable;
        }
    }

    private sealed class FakeCurfew : IChapter3CurfewReader
    {
        public bool Readable = true;
        public bool Active = true;
        public int Calls;

        public bool TryReadActive(out bool active)
        {
            Calls++;
            active = Active;
            return Readable;
        }
    }

    private sealed class FakeWanted : IChapter3WantedWriter
    {
        public bool Succeed = true;
        public int ReadBack = Chapter3PursuitLevels.Arresting;
        public int SetCalls;

        public bool TrySetArresting(out int readBackLevel)
        {
            SetCalls++;
            readBackLevel = ReadBack;
            return Succeed;
        }
    }

    private sealed class FakeDispatch : IChapter3ForcedWantedDispatch
    {
        public Func<string, NativeLawResponseResult> Respond =
            correlation => new(NativeLawResponseResultState.Accepted, correlation, "fake dispatch accepted");
        public bool Succeed = true;
        public int Calls;
        public string? LastCorrelation;

        public bool TryDispatch(string playerId, Guid sessionEpoch, long loadEpoch, string correlationId, out NativeLawResponseResult? result)
        {
            Calls++;
            LastCorrelation = correlationId;
            result = Succeed ? Respond(correlationId) : null;
            return Succeed;
        }
    }
}
