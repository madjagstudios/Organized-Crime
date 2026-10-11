using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurHqPlacerTests
{
    [Fact]
    public void Place_runs_activate_agent_off_physics_hold_transform_visible_readback_in_order()
    {
        var body = new FakeBody { ReadPosition = (1f, 2f, 3f), ReadAgentEnabled = false };

        var status = Chapter3ArthurHqPlacer.TryPlace(body, (1f, 2f, 3f), 180f, out var reason);

        Assert.Equal(Chapter3ArthurMutationStatus.Succeeded, status);
        Assert.Equal(
            new[] { "activate", "agent-off", "physics-held", "transform", "visible", "read" },
            body.Calls);
        Assert.Equal((1f, 2f, 3f), body.TransformPosition);
        Assert.Equal(180f, body.TransformYaw);
        Assert.Contains("HQ place: requested (1.00, 2.00, 3.00), actual (1.00, 2.00, 3.00), agent off", reason);
        Assert.Contains("; placement read", reason);
    }

    [Fact]
    public void Place_reports_ambiguous_when_the_readback_is_far()
    {
        var body = new FakeBody { ReadPosition = (10f, 0f, 0f), ReadAgentEnabled = false };

        var status = Chapter3ArthurHqPlacer.TryPlace(body, (0f, 0f, 0f), 180f, out var reason);

        Assert.Equal(Chapter3ArthurMutationStatus.Ambiguous, status);
        Assert.Contains("HQ place: requested (0.00, 0.00, 0.00), actual (10.00, 0.00, 0.00)", reason);
    }

    [Fact]
    public void Place_tolerance_is_half_a_metre_3d()
    {
        var close = new FakeBody { ReadPosition = (0.3f, 0.4f, 0f) };
        Assert.Equal(
            Chapter3ArthurMutationStatus.Succeeded,
            Chapter3ArthurHqPlacer.TryPlace(close, (0f, 0f, 0f), 180f, out _));

        var far = new FakeBody { ReadPosition = (0.31f, 0.4f, 0f) };
        Assert.Equal(
            Chapter3ArthurMutationStatus.Ambiguous,
            Chapter3ArthurHqPlacer.TryPlace(far, (0f, 0f, 0f), 180f, out _));
    }

    [Fact]
    public void Place_stops_at_the_first_failed_step()
    {
        var body = new FakeBody { AgentStatus = Chapter3ArthurMutationStatus.Unavailable };

        var status = Chapter3ArthurHqPlacer.TryPlace(body, (0f, 0f, 0f), 180f, out var reason);

        Assert.Equal(Chapter3ArthurMutationStatus.Unavailable, status);
        Assert.Equal(new[] { "activate", "agent-off" }, body.Calls);
        Assert.Contains("agent off", reason);
    }

    [Fact]
    public void Place_requires_a_body_and_never_throws()
    {
        Assert.Equal(
            Chapter3ArthurMutationStatus.Unavailable,
            Chapter3ArthurHqPlacer.TryPlace(null, (0f, 0f, 0f), 180f, out _));

        var throwing = new FakeBody { ThrowOn = "transform" };
        var status = Chapter3ArthurHqPlacer.TryPlace(throwing, (0f, 0f, 0f), 180f, out var reason);

        Assert.Equal(Chapter3ArthurMutationStatus.Ambiguous, status);
        Assert.Contains("threw", reason);
    }

    [Fact]
    public void Restore_enables_the_agent_then_restores_physics()
    {
        var body = new FakeBody();

        var status = Chapter3ArthurHqPlacer.TryRestoreForWalk(body, out var reason);

        Assert.Equal(Chapter3ArthurMutationStatus.Succeeded, status);
        Assert.Equal(new[] { "agent-on", "physics-restored" }, body.Calls);
        Assert.Contains("agent on", reason);
    }

    [Fact]
    public void Restore_requires_a_body_and_stops_at_failure()
    {
        Assert.Equal(
            Chapter3ArthurMutationStatus.Unavailable,
            Chapter3ArthurHqPlacer.TryRestoreForWalk(null, out _));

        var body = new FakeBody { AgentStatus = Chapter3ArthurMutationStatus.Ambiguous };
        Assert.Equal(
            Chapter3ArthurMutationStatus.Ambiguous,
            Chapter3ArthurHqPlacer.TryRestoreForWalk(body, out _));
        Assert.Equal(new[] { "agent-on" }, body.Calls);
    }

    [Fact]
    public void Face_delegates_to_the_direct_face()
    {
        var body = new FakeBody();

        var status = Chapter3ArthurHqPlacer.TryFacePlayer(body, out _);

        Assert.Equal(Chapter3ArthurMutationStatus.Succeeded, status);
        Assert.Equal(new[] { "face-direct" }, body.Calls);
        Assert.Equal(
            Chapter3ArthurMutationStatus.Unavailable,
            Chapter3ArthurHqPlacer.TryFacePlayer(null, out _));
    }

    private sealed class FakeBody : IChapter3ArthurHqBody
    {
        public List<string> Calls { get; } = new();
        public Chapter3ArthurMutationStatus ActivateStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus AgentStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus PhysicsStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus TransformStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus VisibleStatus = Chapter3ArthurMutationStatus.Succeeded;
        public Chapter3ArthurMutationStatus ReadStatus = Chapter3ArthurMutationStatus.Succeeded;
        public string? ThrowOn;
        public (float X, float Y, float Z) ReadPosition;
        public bool ReadAgentEnabled;
        public (float X, float Y, float Z) TransformPosition;
        public float TransformYaw;

        public Chapter3ArthurMutationStatus TryActivateBody(out string reason)
        {
            Calls.Add("activate");
            reason = "activated";
            return ThrowOr("activate", ActivateStatus);
        }

        public Chapter3ArthurMutationStatus TrySetAgentEnabled(bool enabled, out string reason)
        {
            Calls.Add(enabled ? "agent-on" : "agent-off");
            reason = enabled ? "agent enabled" : "agent disabled";
            return ThrowOr(enabled ? "agent-on" : "agent-off", AgentStatus);
        }

        public Chapter3ArthurMutationStatus TrySetPhysicsHeld(bool held, out string reason)
        {
            Calls.Add(held ? "physics-held" : "physics-restored");
            reason = held ? "held" : "restored";
            return ThrowOr(held ? "physics-held" : "physics-restored", PhysicsStatus);
        }

        public Chapter3ArthurMutationStatus TrySetTransform((float X, float Y, float Z) position, float yawDegrees, out string reason)
        {
            Calls.Add("transform");
            TransformPosition = position;
            TransformYaw = yawDegrees;
            reason = "transform set";
            return ThrowOr("transform", TransformStatus);
        }

        public Chapter3ArthurMutationStatus TrySetVisible(bool visible, out string reason)
        {
            Calls.Add("visible");
            reason = "visible set";
            return ThrowOr("visible", VisibleStatus);
        }

        public Chapter3ArthurMutationStatus TryFacePlayerDirect(out string reason)
        {
            Calls.Add("face-direct");
            reason = "faced directly";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryReadPlacement(out (float X, float Y, float Z) position, out bool agentEnabled, out string reason)
        {
            Calls.Add("read");
            position = ReadPosition;
            agentEnabled = ReadAgentEnabled;
            reason = "placement read";
            return ThrowOr("read", ReadStatus);
        }

        private Chapter3ArthurMutationStatus ThrowOr(string step, Chapter3ArthurMutationStatus status)
        {
            if (ThrowOn == step) throw new InvalidOperationException($"fake {step} threw");
            return status;
        }
    }
}
