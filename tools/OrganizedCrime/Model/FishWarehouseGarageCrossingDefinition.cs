namespace OrganizedCrime.Model;

public enum FishWarehouseGarageCrossingPhase
{
    Idle,
    Gliding
}

public enum FishWarehouseGarageCrossingActionKind
{
    /// <summary>Do nothing this tick.</summary>
    None,
    /// <summary>Begin a bounded glide across the link (suspend the agent's own movement).</summary>
    BeginGlide,
    /// <summary>Move the agent transform to <see cref="FishWarehouseGarageCrossingDecision.GlideTarget"/> this tick.</summary>
    StepGlide,
    /// <summary>Finish: reseat the agent past the link exit, complete the off-mesh link, restore the agent.</summary>
    CompleteGlide
}

/// <summary>
/// One per-tick observation of an Organized Crime employee agent, pre-scoped by
/// the runtime adapter to Organized Crime's garage off-mesh link. <see cref="IsOnGarageLink"/>
/// is true only when the agent is genuinely on OC's own garage link (not any
/// other link). <see cref="LinkExit"/> is the far link endpoint the agent is
/// heading toward, in property-world coordinates.
/// </summary>
public readonly record struct FishWarehouseGarageCrossingSample(
    bool IsOnGarageLink,
    float Speed,
    FishWarehouseNativeNavigationVector Position,
    FishWarehouseNativeNavigationVector LinkExit);

public sealed record FishWarehouseGarageCrossingDecision(
    FishWarehouseGarageCrossingActionKind Kind,
    FishWarehouseNativeNavigationVector GlideTarget = default)
{
    public static readonly FishWarehouseGarageCrossingDecision None =
        new(FishWarehouseGarageCrossingActionKind.None);
}

/// <summary>Mutable per-agent crossing state held by the runtime host.</summary>
public sealed class FishWarehouseGarageCrossingAgentState
{
    public FishWarehouseGarageCrossingPhase Phase { get; set; } = FishWarehouseGarageCrossingPhase.Idle;
    public int StalledTicks { get; set; }

    public void Reset()
    {
        Phase = FishWarehouseGarageCrossingPhase.Idle;
        StalledTicks = 0;
    }
}

/// <summary>
/// Pure decision model for OC-7's garage off-mesh-link crossing assist. Schedule I
/// only auto-completes an off-mesh link for a Ladder, so an employee walking a
/// route out to the loading dock dead-stops at Organized Crime's component-less
/// garage link. This model decides, per agent per tick, when to take over and
/// glide the agent across the link at walk speed — bounded, and only while the
/// agent is genuinely stalled on OC's own link. It never runs without host
/// authority. The runtime host applies the returned actions and additionally
/// sets each agent's native auto-traverse flag proactively (the cheap first layer);
/// this model is the robust glide fallback and is kept free of Unity types.
/// </summary>
public static class FishWarehouseGarageCrossingDefinition
{
    /// <summary>Below this speed (m/s) an on-link agent counts as stalled.</summary>
    public const float StallSpeedThreshold = 0.15f;

    /// <summary>Consecutive stalled ticks on the link before the assist takes over (~0.5s at 60 FPS).</summary>
    public const int StallDwellTicks = 30;

    /// <summary>Glide speed (m/s) — matches a normal employee walk so it reads as walking, not a teleport.</summary>
    public const float GlideSpeed = 1.45f;

    /// <summary>Distance (m) from the link exit at which the glide completes.</summary>
    public const float GlideArriveEpsilon = 0.2f;

    public static FishWarehouseGarageCrossingDecision Decide(
        FishWarehouseGarageCrossingAgentState state,
        FishWarehouseGarageCrossingSample sample,
        float deltaTime,
        bool hostAuthority)
    {
        if (state is null || !hostAuthority || deltaTime <= 0f)
            return FishWarehouseGarageCrossingDecision.None;

        if (state.Phase == FishWarehouseGarageCrossingPhase.Gliding)
            return StepGlideToward(state, sample, deltaTime);

        // Idle: watch for a genuine stall on OC's garage link.
        if (!sample.IsOnGarageLink)
        {
            state.StalledTicks = 0;
            return FishWarehouseGarageCrossingDecision.None;
        }

        state.StalledTicks = sample.Speed < StallSpeedThreshold ? state.StalledTicks + 1 : 0;
        if (state.StalledTicks < StallDwellTicks)
            return FishWarehouseGarageCrossingDecision.None;

        state.Phase = FishWarehouseGarageCrossingPhase.Gliding;
        state.StalledTicks = 0;
        return new FishWarehouseGarageCrossingDecision(FishWarehouseGarageCrossingActionKind.BeginGlide);
    }

    private static FishWarehouseGarageCrossingDecision StepGlideToward(
        FishWarehouseGarageCrossingAgentState state,
        FishWarehouseGarageCrossingSample sample,
        float deltaTime)
    {
        // The host attempts the cheap native CompleteOffMeshLink when the glide
        // begins; if that already carried the agent off the link, finish and
        // restore the agent immediately instead of manually stepping.
        if (!sample.IsOnGarageLink)
        {
            state.Reset();
            return new FishWarehouseGarageCrossingDecision(
                FishWarehouseGarageCrossingActionKind.CompleteGlide,
                sample.Position);
        }

        FishWarehouseNativeNavigationVector next = MoveTowards(
            sample.Position,
            sample.LinkExit,
            GlideSpeed * deltaTime);

        if (Distance(next, sample.LinkExit) <= GlideArriveEpsilon)
        {
            state.Reset();
            return new FishWarehouseGarageCrossingDecision(
                FishWarehouseGarageCrossingActionKind.CompleteGlide,
                sample.LinkExit);
        }

        return new FishWarehouseGarageCrossingDecision(
            FishWarehouseGarageCrossingActionKind.StepGlide,
            next);
    }

    public static float Distance(
        FishWarehouseNativeNavigationVector a,
        FishWarehouseNativeNavigationVector b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    public static FishWarehouseNativeNavigationVector MoveTowards(
        FishWarehouseNativeNavigationVector current,
        FishWarehouseNativeNavigationVector target,
        float maxDistanceDelta)
    {
        float dx = target.X - current.X;
        float dy = target.Y - current.Y;
        float dz = target.Z - current.Z;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (distance <= maxDistanceDelta || distance <= 1e-6f)
            return target;

        float scale = maxDistanceDelta / distance;
        return new FishWarehouseNativeNavigationVector(
            current.X + (dx * scale),
            current.Y + (dy * scale),
            current.Z + (dz * scale));
    }

    /// <summary>The far link endpoint (the exit) relative to the agent's current position.</summary>
    public static FishWarehouseNativeNavigationVector SelectLinkExit(
        FishWarehouseNativeNavigationVector position,
        FishWarehouseNativeNavigationVector linkStart,
        FishWarehouseNativeNavigationVector linkEnd) =>
        Distance(position, linkStart) >= Distance(position, linkEnd) ? linkStart : linkEnd;
}
