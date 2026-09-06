using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseGarageCrossingDefinitionTests
{
    private static readonly FishWarehouseNativeNavigationVector Interior = new(0f, 0f, 0f);
    private static readonly FishWarehouseNativeNavigationVector Exterior = new(0f, 0f, 3f);

    private static FishWarehouseGarageCrossingSample OnLinkStalled(
        FishWarehouseNativeNavigationVector position) =>
        new(IsOnGarageLink: true, Speed: 0f, Position: position, LinkExit: Exterior);

    [Fact]
    public void Does_nothing_without_host_authority()
    {
        var state = new FishWarehouseGarageCrossingAgentState();
        for (int i = 0; i < FishWarehouseGarageCrossingDefinition.StallDwellTicks + 5; i++)
        {
            FishWarehouseGarageCrossingDecision decision =
                FishWarehouseGarageCrossingDefinition.Decide(state, OnLinkStalled(Interior), 0.016f, hostAuthority: false);
            Assert.Equal(FishWarehouseGarageCrossingActionKind.None, decision.Kind);
        }

        Assert.Equal(FishWarehouseGarageCrossingPhase.Idle, state.Phase);
    }

    [Fact]
    public void Takes_over_only_after_the_stall_dwell_elapses_on_the_garage_link()
    {
        var state = new FishWarehouseGarageCrossingAgentState();

        for (int i = 0; i < FishWarehouseGarageCrossingDefinition.StallDwellTicks - 1; i++)
        {
            FishWarehouseGarageCrossingDecision decision =
                FishWarehouseGarageCrossingDefinition.Decide(state, OnLinkStalled(Interior), 0.016f, hostAuthority: true);
            Assert.Equal(FishWarehouseGarageCrossingActionKind.None, decision.Kind);
        }

        FishWarehouseGarageCrossingDecision takeover =
            FishWarehouseGarageCrossingDefinition.Decide(state, OnLinkStalled(Interior), 0.016f, hostAuthority: true);
        Assert.Equal(FishWarehouseGarageCrossingActionKind.BeginGlide, takeover.Kind);
        Assert.Equal(FishWarehouseGarageCrossingPhase.Gliding, state.Phase);
    }

    [Fact]
    public void A_moving_agent_on_the_link_never_triggers_the_assist()
    {
        var state = new FishWarehouseGarageCrossingAgentState();
        var moving = new FishWarehouseGarageCrossingSample(
            IsOnGarageLink: true, Speed: 1.4f, Position: Interior, LinkExit: Exterior);

        for (int i = 0; i < FishWarehouseGarageCrossingDefinition.StallDwellTicks * 3; i++)
        {
            FishWarehouseGarageCrossingDecision decision =
                FishWarehouseGarageCrossingDefinition.Decide(state, moving, 0.016f, hostAuthority: true);
            Assert.Equal(FishWarehouseGarageCrossingActionKind.None, decision.Kind);
        }
    }

    [Fact]
    public void Leaving_the_link_resets_the_stall_counter()
    {
        var state = new FishWarehouseGarageCrossingAgentState();
        for (int i = 0; i < FishWarehouseGarageCrossingDefinition.StallDwellTicks - 1; i++)
            FishWarehouseGarageCrossingDefinition.Decide(state, OnLinkStalled(Interior), 0.016f, hostAuthority: true);

        var offLink = new FishWarehouseGarageCrossingSample(false, 0f, Interior, Exterior);
        FishWarehouseGarageCrossingDecision decision =
            FishWarehouseGarageCrossingDefinition.Decide(state, offLink, 0.016f, hostAuthority: true);

        Assert.Equal(FishWarehouseGarageCrossingActionKind.None, decision.Kind);
        Assert.Equal(0, state.StalledTicks);
        Assert.Equal(FishWarehouseGarageCrossingPhase.Idle, state.Phase);
    }

    [Fact]
    public void Glide_steps_toward_the_exit_then_completes()
    {
        var state = new FishWarehouseGarageCrossingAgentState { Phase = FishWarehouseGarageCrossingPhase.Gliding };
        FishWarehouseNativeNavigationVector position = Interior;

        FishWarehouseGarageCrossingActionKind last = FishWarehouseGarageCrossingActionKind.None;
        for (int i = 0; i < 1000 && last != FishWarehouseGarageCrossingActionKind.CompleteGlide; i++)
        {
            var sample = new FishWarehouseGarageCrossingSample(true, 0f, position, Exterior);
            FishWarehouseGarageCrossingDecision decision =
                FishWarehouseGarageCrossingDefinition.Decide(state, sample, 0.016f, hostAuthority: true);
            last = decision.Kind;
            if (decision.Kind == FishWarehouseGarageCrossingActionKind.StepGlide)
            {
                // The step target advances toward the exit and never overshoots.
                Assert.True(FishWarehouseGarageCrossingDefinition.Distance(decision.GlideTarget, Exterior) <
                    FishWarehouseGarageCrossingDefinition.Distance(position, Exterior) + 1e-4f);
                position = decision.GlideTarget;
            }
        }

        Assert.Equal(FishWarehouseGarageCrossingActionKind.CompleteGlide, last);
        Assert.Equal(FishWarehouseGarageCrossingPhase.Idle, state.Phase);
    }

    [Fact]
    public void Glide_is_idempotent_and_does_not_re_begin_while_in_progress()
    {
        var state = new FishWarehouseGarageCrossingAgentState { Phase = FishWarehouseGarageCrossingPhase.Gliding };
        var sample = new FishWarehouseGarageCrossingSample(true, 0f, Interior, Exterior);

        FishWarehouseGarageCrossingDecision decision =
            FishWarehouseGarageCrossingDefinition.Decide(state, sample, 0.016f, hostAuthority: true);

        Assert.Equal(FishWarehouseGarageCrossingActionKind.StepGlide, decision.Kind);
    }

    [Fact]
    public void Glide_completes_immediately_if_the_agent_already_left_the_link()
    {
        // Simulates the host's cheap CompleteOffMeshLink already carrying the agent across.
        var state = new FishWarehouseGarageCrossingAgentState { Phase = FishWarehouseGarageCrossingPhase.Gliding };
        var offLink = new FishWarehouseGarageCrossingSample(
            IsOnGarageLink: false, Speed: 0f, Position: Exterior, LinkExit: Exterior);

        FishWarehouseGarageCrossingDecision decision =
            FishWarehouseGarageCrossingDefinition.Decide(state, offLink, 0.016f, hostAuthority: true);

        Assert.Equal(FishWarehouseGarageCrossingActionKind.CompleteGlide, decision.Kind);
        Assert.Equal(FishWarehouseGarageCrossingPhase.Idle, state.Phase);
    }

    [Fact]
    public void MoveTowards_clamps_to_the_target_and_never_overshoots()
    {
        FishWarehouseNativeNavigationVector next = FishWarehouseGarageCrossingDefinition.MoveTowards(
            Interior, Exterior, maxDistanceDelta: 10f);
        Assert.Equal(Exterior, next);

        FishWarehouseNativeNavigationVector step = FishWarehouseGarageCrossingDefinition.MoveTowards(
            Interior, Exterior, maxDistanceDelta: 1f);
        Assert.Equal(1f, FishWarehouseGarageCrossingDefinition.Distance(Interior, step), 4);
    }

    [Fact]
    public void Selects_the_far_link_endpoint_as_the_exit()
    {
        // Agent near the interior endpoint → exit is the exterior endpoint.
        Assert.Equal(
            Exterior,
            FishWarehouseGarageCrossingDefinition.SelectLinkExit(new(0f, 0f, 0.3f), Interior, Exterior));
        // Agent near the exterior endpoint → exit is the interior endpoint (return trip).
        Assert.Equal(
            Interior,
            FishWarehouseGarageCrossingDefinition.SelectLinkExit(new(0f, 0f, 2.7f), Interior, Exterior));
    }
}
