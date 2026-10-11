namespace OrganizedCrime.Model;

/// <summary>
/// OC-134 tuning: Arthur's visit walks run at this multiple of his
/// native walk speed, applied as an absolute metres-per-second speed
/// control for the walk and removed when it ends. Retune by number.
/// </summary>
public static class Chapter3ArthurTuning
{
    /// <summary>
    /// Visit walks (come, leave, re-issue) run at baseline times
    /// this. The owner run timed the native walk at about 1.8 m/s,
    /// so 1.35 lands him near 2.43 m/s.
    /// </summary>
    public const float VisitWalkSpeedFactor = 1.35f;

    /// <summary>
    /// The stable speed-control id. The game lowercases ids and
    /// updates a re-added id in place, so repeats never stack.
    /// </summary>
    public const string VisitWalkSpeedControlId = "oc-arthur-visit";

    /// <summary>
    /// Above the default control (0) and the patrol/sentry walks
    /// (1), level with fleeing (2), and far below the ragdoll and
    /// seated stops (100), which must always win.
    /// </summary>
    public const int VisitWalkSpeedControlPriority = 2;
}
