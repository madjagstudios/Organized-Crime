using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-134: the game-touching surface behind Arthur's visit-walk
/// boost. Total like every other world boundary: bools and out
/// values, never an exception. The S1API movement wrapper is
/// deliberately not used here: it hard-codes the normalized speed
/// type, while the boost needs absolute metres per second.
/// </summary>
public interface IChapter3ArthurSpeedControl
{
    bool TryReadSpeeds(out float moveSpeed, out float agentSpeed);
    bool TryAddBoost(float boostedSpeed, out float agentSpeedAfter);
    bool TryRemoveBoost();
}

/// <summary>
/// OC-134: the visit-walk speed coordinator, in the HQ placer's
/// shape: pure order and math over an injected game surface, so the
/// fakes pin the sequence. Every walk issue removes any stacked
/// control first, reads the live baseline, and adds one absolute
/// control at <see cref="Chapter3ArthurTuning.VisitWalkSpeedFactor"/>
/// times that baseline; every walk end removes it. Nothing is
/// touched until FishNet reports spawned: the caller passes the
/// spawn state it just verified.
/// </summary>
public static class Chapter3ArthurWalkSpeed
{
    /// <summary>
    /// Boosts for a walk and returns the proof fragment for the
    /// reason, or a short note (possibly empty) when the walk stays
    /// native. Removes first so a re-issue never stacks or ratchets:
    /// the baseline is always read unboosted.
    /// </summary>
    public static string BeginWalk(IChapter3ArthurSpeedControl? control, bool isSpawned)
    {
        if (!isSpawned || control is null)
            return string.Empty;
        control.TryRemoveBoost();
        if (!control.TryReadSpeeds(out var baseline, out var before) || !IsUsableBaseline(baseline))
            return "walk speed left native: no usable baseline.";
        var boosted = baseline * Chapter3ArthurTuning.VisitWalkSpeedFactor;
        if (!control.TryAddBoost(boosted, out var after))
            return "walk speed left native: the boost refused.";
        return $"Arthur walk speed: {before:0.00} -> {after:0.00} m/s (x{Chapter3ArthurTuning.VisitWalkSpeedFactor:0.00}).";
    }

    /// <summary>
    /// Drops the boost, back to native. Safe when absent and
    /// silent: the walk-end reasons say enough already.
    /// </summary>
    public static void EndWalk(IChapter3ArthurSpeedControl? control, bool isSpawned)
    {
        if (!isSpawned || control is null)
            return;
        control.TryRemoveBoost();
    }

    /// <summary>
    /// The come-arrival proof: the walk's average speed, distance
    /// over elapsed time, as the runtime tracked it.
    /// </summary>
    public static string DescribeArrival(float distanceMetres, float elapsedSeconds) =>
        elapsedSeconds > 0f
            ? $"come walk averaged {distanceMetres / elapsedSeconds:0.00} m/s over {distanceMetres:0.0} m in {elapsedSeconds:0.0} s."
            : $"come walk covered {distanceMetres:0.0} m in no measurable time.";

    private static bool IsUsableBaseline(float baseline) => float.IsFinite(baseline) && baseline > 0f;
}
