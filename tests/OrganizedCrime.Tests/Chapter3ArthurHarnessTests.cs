using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurHarnessTests
{
    [Fact]
    public void Come_runs_the_world_and_marks_coming()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var result = Chapter3ArthurHarness.TryCome(world, visit);

        Assert.True(result.Ok);
        Assert.Contains("come", result.Message);
        Assert.Equal(Chapter3ArthurVisitState.Coming, visit.State);
        Assert.Contains("come", world.Mutations);
    }

    [Fact]
    public void Come_refuses_when_not_parked_and_touches_nothing()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);
        Assert.True(visit.BeginCome());

        var result = Chapter3ArthurHarness.TryCome(world, visit);

        Assert.False(result.Ok);
        Assert.Contains("not parked", result.Message);
        Assert.Empty(world.Mutations);
        Assert.Empty(world.Reads);
    }

    [Fact]
    public void Come_stays_parked_when_the_world_refuses()
    {
        var world = new FakeWorld { ComeStatus = Chapter3ArthurMutationStatus.Ambiguous };
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var result = Chapter3ArthurHarness.TryCome(world, visit);

        Assert.False(result.Ok);
        Assert.Equal(Chapter3ArthurVisitState.Parked, visit.State);
    }

    [Fact]
    public void Come_refuses_without_authority_or_wiring()
    {
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        Assert.Contains("host only", Chapter3ArthurHarness.TryCome(new FakeWorld { Authority = false }, visit).Message);
        Assert.Contains("not wired up", Chapter3ArthurHarness.TryCome(null, visit).Message);
        Assert.Contains("not wired up", Chapter3ArthurHarness.TryCome(new FakeWorld(), null).Message);
    }

    [Fact]
    public void Say_forwards_the_text_when_active()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);
        Assert.True(visit.BeginCome());

        var result = Chapter3ArthurHarness.TrySay(world, visit, "Nice place you got here.");

        Assert.True(result.Ok);
        Assert.Contains("say:Nice place you got here.", world.Mutations);
    }

    [Fact]
    public void Say_refuses_while_parked_with_no_text_or_without_authority()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        Assert.Contains("parked", Chapter3ArthurHarness.TrySay(world, visit, "hi").Message);
        Assert.True(visit.BeginCome());
        Assert.Contains("Usage", Chapter3ArthurHarness.TrySay(world, visit, "  ").Message);
        Assert.Contains("Usage", Chapter3ArthurHarness.TrySay(world, visit, null).Message);
        Assert.Contains("host only", Chapter3ArthurHarness.TrySay(new FakeWorld { Authority = false }, visit, "hi").Message);
        Assert.DoesNotContain(world.Mutations, mutation => mutation.StartsWith("say:", StringComparison.Ordinal));
    }

    [Fact]
    public void Leave_runs_the_world_and_marks_leaving()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);
        Assert.True(visit.BeginCome());

        var result = Chapter3ArthurHarness.TryLeave(world, visit);

        Assert.True(result.Ok);
        Assert.Equal(Chapter3ArthurVisitState.Leaving, visit.State);
        Assert.Contains("leave", world.Mutations);
    }

    [Fact]
    public void Leave_refuses_while_parked_and_touches_nothing()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var result = Chapter3ArthurHarness.TryLeave(world, visit);

        Assert.False(result.Ok);
        Assert.Contains("parked", result.Message);
        Assert.Empty(world.Mutations);
        Assert.Empty(world.Reads);
    }

    [Fact]
    public void Status_reports_state_position_distance_readiness_and_count()
    {
        var world = new FakeWorld
        {
            Next = new Chapter3ArthurSnapshot(true, true, true, true, true, (25f, 0f, 3f), 25.2f, 1)
        };
        var visit = new Chapter3ArthurVisitPump(() => 0f);
        Assert.True(visit.BeginCome());

        var lines = Chapter3ArthurHarness.TryStatus(world, visit);

        Assert.Contains("arthur: coming", lines);
        Assert.Contains("read Ready", lines);
        Assert.Contains("spawned yes", lines);
        Assert.Contains("present yes, visible yes, moving yes, conscious yes", lines);
        Assert.Contains("position (25.0, 0.0, 3.0), distance to player 25.2 m", lines);
        Assert.Contains($"registered NPCs carrying {Chapter3ArthurIds.NpcId}: 1", lines);
        Assert.DoesNotContain(lines, line => line.Contains("appearance donor", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_flags_a_registered_count_other_than_one()
    {
        var world = new FakeWorld { Next = new Chapter3ArthurSnapshot(true, true, false, false, true, (0f, -10000f, 0f), float.NaN, 2) };
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var lines = Chapter3ArthurHarness.TryStatus(world, visit);

        Assert.Contains($"registered NPCs carrying {Chapter3ArthurIds.NpcId}: 2 (expected exactly 1)", lines);
    }

    [Fact]
    public void Status_with_an_absent_contact_says_so()
    {
        var world = new FakeWorld { Next = Chapter3ArthurSnapshot.Absent() };
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var lines = Chapter3ArthurHarness.TryStatus(world, visit);

        Assert.Contains("arthur: parked", lines);
        Assert.Contains("present no; the load-constructed Arthur has not resolved yet.", lines);
    }

    [Fact]
    public void Status_without_wiring_says_so()
    {
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        Assert.Equal(
            new[] { "Chapter 3 QA arthur status: Arthur is not wired up." },
            Chapter3ArthurHarness.TryStatus(null, visit));
    }

    [Fact]
    public void Commands_surface_the_unspawned_refusal_and_change_no_state()
    {
        const string refusal = "Arthur isn't spawned yet.";
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var comeWorld = new FakeWorld { ComeStatus = Chapter3ArthurMutationStatus.Rejected, Refusal = refusal };
        var come = Chapter3ArthurHarness.TryCome(comeWorld, visit);
        Assert.False(come.Ok);
        Assert.Contains("rejected", come.Message);
        Assert.Contains(refusal, come.Message);
        Assert.Equal(Chapter3ArthurVisitState.Parked, visit.State);

        Assert.True(visit.BeginCome());
        var sayWorld = new FakeWorld { SayStatus = Chapter3ArthurMutationStatus.Rejected, Refusal = refusal };
        var say = Chapter3ArthurHarness.TrySay(sayWorld, visit, "hi");
        Assert.False(say.Ok);
        Assert.Contains(refusal, say.Message);

        var leaveWorld = new FakeWorld { LeaveStatus = Chapter3ArthurMutationStatus.Rejected, Refusal = refusal };
        var leave = Chapter3ArthurHarness.TryLeave(leaveWorld, visit);
        Assert.False(leave.Ok);
        Assert.Contains(refusal, leave.Message);
        Assert.Equal(Chapter3ArthurVisitState.Coming, visit.State);
    }

    [Fact]
    public void Status_reports_spawned_no_while_pending()
    {
        var world = new FakeWorld { Next = new Chapter3ArthurSnapshot(true, false, true, false, true, (0f, -10000f, 0f), float.NaN, 1) };
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var lines = Chapter3ArthurHarness.TryStatus(world, visit);

        Assert.Contains("spawned no", lines);
    }

    [Fact]
    public void Hq_sets_the_override_and_reports_it()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var on = Chapter3ArthurHarness.TryHq(world, visit, "on");
        Assert.True(on.Ok);
        Assert.Equal(Chapter3ArthurHqForce.On, visit.HqForce);
        Assert.Contains("hq", on.Message);

        var off = Chapter3ArthurHarness.TryHq(world, visit, "OFF");
        Assert.True(off.Ok);
        Assert.Equal(Chapter3ArthurHqForce.Off, visit.HqForce);

        var auto = Chapter3ArthurHarness.TryHq(world, visit, "auto");
        Assert.True(auto.Ok);
        Assert.Equal(Chapter3ArthurHqForce.Auto, visit.HqForce);
        Assert.Empty(world.Mutations);
    }

    [Fact]
    public void Hq_refuses_a_bad_mode_without_authority_or_wiring()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        Assert.Contains("Usage", Chapter3ArthurHarness.TryHq(world, visit, "maybe").Message);
        Assert.Contains("Usage", Chapter3ArthurHarness.TryHq(world, visit, null).Message);
        Assert.Contains("host only", Chapter3ArthurHarness.TryHq(new FakeWorld { Authority = false }, visit, "on").Message);
        Assert.Contains("not wired up", Chapter3ArthurHarness.TryHq(null, visit, "on").Message);
        Assert.Contains("not wired up", Chapter3ArthurHarness.TryHq(world, null, "on").Message);
        Assert.Equal(Chapter3ArthurHqForce.Auto, visit.HqForce);
    }

    [Fact]
    public void Status_reports_location_parked_visit_or_hq()
    {
        var world = new FakeWorld
        {
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, -10000f, 0f), float.NaN, 1)
        };
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        Assert.Contains("location: parked", Chapter3ArthurHarness.TryStatus(world, visit));

        Assert.True(visit.BeginCome());
        Assert.Contains("location: visit", Chapter3ArthurHarness.TryStatus(world, visit));

        visit.HqForce = Chapter3ArthurHqForce.On;
        var forced = Chapter3ArthurHarness.TryStatus(world, visit);
        Assert.Contains("location: visit", forced);
        Assert.Contains("hq override: on", forced);
    }

    [Fact]
    public void Status_reports_hq_at_the_spot()
    {
        var world = new FakeWorld
        {
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (1002.5f, 0f, 1004.2f), 5f, 1)
        };
        var visit = new Chapter3ArthurVisitPump(() => 0f)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        visit.BeginAfterLoad();
        Assert.NotNull(visit.Pump(world));
        Assert.NotNull(visit.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.AtHq, visit.State);

        var lines = Chapter3ArthurHarness.TryStatus(world, visit);

        Assert.Contains("arthur: at the HQ", lines);
        Assert.Contains("location: hq", lines);
    }

    [Fact]
    public void Come_runs_from_hq_say_works_there_and_leave_refuses()
    {
        var world = new FakeWorld
        {
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (1002.5f, 0f, 1004.2f), 5f, 1)
        };
        var visit = new Chapter3ArthurVisitPump(() => 0f)
        {
            HqPresence = new FakePresence { Chapter = true, Inside = true }
        };
        visit.BeginAfterLoad();
        Assert.NotNull(visit.Pump(world));
        Assert.NotNull(visit.Pump(world));

        var say = Chapter3ArthurHarness.TrySay(world, visit, "Evening.");
        Assert.True(say.Ok);

        var leave = Chapter3ArthurHarness.TryLeave(world, visit);
        Assert.False(leave.Ok);

        var come = Chapter3ArthurHarness.TryCome(world, visit);
        Assert.True(come.Ok);
        Assert.Equal(Chapter3ArthurVisitState.Coming, visit.State);
        Assert.Contains("come", world.Mutations);
    }

    [Fact]
    public void HqHere_captures_replaces_and_logs_paste_ready_values()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var result = Chapter3ArthurHarness.TryHqHere(world, visit);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "capture", "place" }, world.Mutations);
        Assert.Contains("Arthur HQ spot: local (-2.90, 0.00, 3.70) yaw 180", result.Message);
    }

    [Fact]
    public void HqHere_skips_the_place_mid_visit()
    {
        var world = new FakeWorld();
        var visit = new Chapter3ArthurVisitPump(() => 0f);
        Assert.True(visit.BeginCome());

        var result = Chapter3ArthurHarness.TryHqHere(world, visit);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "capture" }, world.Mutations);
        Assert.Contains("mid-visit; place skipped", result.Message);
    }

    [Fact]
    public void HqHere_refuses_outside_the_hq_without_authority_or_wiring()
    {
        var world = new FakeWorld { CaptureStatus = Chapter3ArthurMutationStatus.Rejected };
        var visit = new Chapter3ArthurVisitPump(() => 0f);

        var refused = Chapter3ArthurHarness.TryHqHere(world, visit);
        Assert.False(refused.Ok);
        Assert.Contains("not inside the HQ", refused.Message);
        Assert.DoesNotContain("place", world.Mutations);

        Assert.Contains("host only", Chapter3ArthurHarness.TryHqHere(new FakeWorld { Authority = false }, visit).Message);
        Assert.Contains("not wired up", Chapter3ArthurHarness.TryHqHere(null, visit).Message);
        Assert.Contains("not wired up", Chapter3ArthurHarness.TryHqHere(world, null).Message);
    }

    private sealed class FakePresence : IChapter3HqPresence
    {
        public bool Chapter;
        public bool Inside;
        public bool ChapterActive => Chapter;
        public bool PlayerInsideHq => Inside;
    }

    private sealed class FakeWorld : IChapter3ArthurWorld
    {
        public bool Authority = true;
        public bool HasAuthority => Authority;
        public bool PollArthurImpact() => false;
        public Chapter3ArthurSnapshot Next = Chapter3ArthurSnapshot.Absent();
        public Chapter3ArthurMutationStatus ComeStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus SayStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus LeaveStatus = Chapter3ArthurMutationStatus.Succeeded;
        public string Refusal = "coming";
        public List<string> Reads { get; } = new();
        public List<string> Mutations { get; } = new();

        public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
        {
            Reads.Add("read");
            snapshot = Next;
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
            reason = ComeStatus == Chapter3ArthurMutationStatus.Succeeded ? "coming" : Refusal;
            return ComeStatus;
        }

        public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason)
        {
            Mutations.Add($"say:{text}");
            reason = SayStatus == Chapter3ArthurMutationStatus.Succeeded ? "said" : Refusal;
            return SayStatus;
        }

        public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason)
        {
            Mutations.Add("leave");
            reason = LeaveStatus == Chapter3ArthurMutationStatus.Succeeded ? "leaving" : Refusal;
            return LeaveStatus;
        }

        public Chapter3ArthurMutationStatus TryFacePlayer(out string reason)
        {
            Mutations.Add("face");
            reason = "faced";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus CaptureStatus = Chapter3ArthurMutationStatus.Succeeded;

        public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)
        {
            Mutations.Add("capture");
            local = (-2.9f, 0f, 3.7f);
            yawDegrees = 180f;
            reason = CaptureStatus == Chapter3ArthurMutationStatus.Succeeded ? "captured" : "the player is not inside the HQ.";
            return CaptureStatus;
        }

        public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
        {
            Mutations.Add("facehq");
            reason = "faced directly";
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
