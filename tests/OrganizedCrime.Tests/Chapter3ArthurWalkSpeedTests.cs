using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurWalkSpeedTests
{
    [Fact]
    public void BeginWalk_removes_first_then_reads_then_adds_at_135x()
    {
        var control = new FakeControl();

        var note = Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: true);

        Assert.Equal(new[] { "remove", "read", "add:2.43" }, control.Calls);
        Assert.Equal("Arthur walk speed: 1.80 -> 2.43 m/s (x1.35).", note);
    }

    [Fact]
    public void BeginWalk_twice_never_stacks_or_ratchets()
    {
        var control = new FakeControl();

        Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: true);
        var note = Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: true);

        Assert.Equal(
            new[] { "remove", "read", "add:2.43", "remove", "read", "add:2.43" },
            control.Calls);
        Assert.Equal("Arthur walk speed: 1.80 -> 2.43 m/s (x1.35).", note);
        Assert.Equal("oc-arthur-visit", Chapter3ArthurTuning.VisitWalkSpeedControlId);
    }

    [Fact]
    public void Unspawned_walks_touch_nothing()
    {
        var control = new FakeControl();

        Assert.Equal(string.Empty, Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: false));
        Chapter3ArthurWalkSpeed.EndWalk(control, isSpawned: false);
        Assert.Equal(string.Empty, Chapter3ArthurWalkSpeed.BeginWalk(null, isSpawned: true));
        Chapter3ArthurWalkSpeed.EndWalk(null, isSpawned: true);

        Assert.Empty(control.Calls);
    }

    [Fact]
    public void EndWalk_removes_and_only_removes()
    {
        var control = new FakeControl();

        Chapter3ArthurWalkSpeed.EndWalk(control, isSpawned: true);

        Assert.Equal(new[] { "remove" }, control.Calls);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Bad_baseline_skips_the_add(float baseline)
    {
        var control = new FakeControl { MoveSpeed = baseline };

        var note = Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: true);

        Assert.Equal(new[] { "remove", "read" }, control.Calls);
        Assert.Contains("left native", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_baseline_skips_the_add()
    {
        var control = new FakeControl { ReadOk = false };

        var note = Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: true);

        Assert.Equal(new[] { "remove", "read" }, control.Calls);
        Assert.Contains("left native", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Refused_add_reports_and_keeps_native()
    {
        var control = new FakeControl { AddOk = false };

        var note = Chapter3ArthurWalkSpeed.BeginWalk(control, isSpawned: true);

        Assert.Equal(new[] { "remove", "read", "add:2.43" }, control.Calls);
        Assert.Contains("boost refused", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Tuning_pins_the_factor_id_and_priority()
    {
        Assert.Equal(1.35f, Chapter3ArthurTuning.VisitWalkSpeedFactor);
        Assert.Equal("oc-arthur-visit", Chapter3ArthurTuning.VisitWalkSpeedControlId);
        Assert.Equal(2, Chapter3ArthurTuning.VisitWalkSpeedControlPriority);
    }

    [Fact]
    public void Arrival_reports_distance_over_time()
    {
        Assert.Equal(
            "come walk averaged 1.85 m/s over 25.0 m in 13.5 s.",
            Chapter3ArthurWalkSpeed.DescribeArrival(25f, 13.5f));
        Assert.Equal(
            "come walk covered 25.0 m in no measurable time.",
            Chapter3ArthurWalkSpeed.DescribeArrival(25f, 0f));
    }

    private sealed class FakeControl : IChapter3ArthurSpeedControl
    {
        public List<string> Calls { get; } = new();
        public float MoveSpeed = 1.8f;
        public float AgentSpeed = 1.8f;
        public float AgentSpeedAfter = 2.43f;
        public bool ReadOk = true;
        public bool AddOk = true;

        public bool TryReadSpeeds(out float moveSpeed, out float agentSpeed)
        {
            Calls.Add("read");
            moveSpeed = MoveSpeed;
            agentSpeed = AgentSpeed;
            return ReadOk;
        }

        public bool TryAddBoost(float boostedSpeed, out float agentSpeedAfter)
        {
            Calls.Add($"add:{boostedSpeed:0.00}");
            agentSpeedAfter = AgentSpeedAfter;
            return AddOk;
        }

        public bool TryRemoveBoost()
        {
            Calls.Add("remove");
            return true;
        }
    }
}
