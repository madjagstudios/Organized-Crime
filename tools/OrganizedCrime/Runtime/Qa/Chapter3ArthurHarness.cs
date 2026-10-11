namespace OrganizedCrime.Runtime;

/// <summary>
/// Owner QA only (OwnerQaKeys): entry points for Arthur's proof visit, behind <c>oc_ch3
/// arthur come | say | leave | status</c>, plus the <c>oc_ch3 arthur hq</c> HQ-presence
/// override. No engine or mod-framework reference here, so it stays test linkable; the world
/// boundary carries every game touch. Mutations refuse without host authority and outside
/// their visit state; <c>status</c> only ever reads.
/// </summary>
public static class Chapter3ArthurHarness
{
    public static Chapter3QaResult TryCome(IChapter3ArthurWorld? world, Chapter3ArthurVisitPump? visit)
    {
        if (world is null || visit is null)
            return QaRefused("Chapter 3 QA arthur come refused: Arthur is not wired up.");
        if (!world.HasAuthority)
            return QaRefused("Chapter 3 QA arthur come refused: host only.");
        // A visit overrides the HQ presence: come runs from parked or
        // from the HQ spot, never stacked mid-visit.
        if (visit.State != Chapter3ArthurVisitState.Parked && visit.State != Chapter3ArthurVisitState.AtHq)
            return QaRefused($"Chapter 3 QA arthur come refused: Arthur is {DescribeState(visit.State)}, not parked.");

        var status = Guarded(world.TryComeArthur, out var outcomeReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
            return new Chapter3QaResult(false, $"Chapter 3 QA arthur come {DescribeStatus(status)}: {outcomeReason}");
        visit.BeginCome();
        return new Chapter3QaResult(true, $"Chapter 3 QA arthur come: {outcomeReason}");
    }

    public static Chapter3QaResult TrySay(IChapter3ArthurWorld? world, Chapter3ArthurVisitPump? visit, string? text)
    {
        if (world is null || visit is null)
            return QaRefused("Chapter 3 QA arthur say refused: Arthur is not wired up.");
        if (string.IsNullOrWhiteSpace(text))
            return QaRefused("Usage: oc_ch3 arthur say <text>");
        if (!world.HasAuthority)
            return QaRefused("Chapter 3 QA arthur say refused: host only.");
        if (visit.State == Chapter3ArthurVisitState.Parked)
            return QaRefused("Chapter 3 QA arthur say refused: Arthur is parked; come first.");

        var status = Guarded((out string sayReason) => world.TrySayArthur(text, out sayReason), out var outcomeReason);
        return status == Chapter3ArthurMutationStatus.Succeeded
            ? new Chapter3QaResult(true, $"Chapter 3 QA arthur say: {outcomeReason}")
            : new Chapter3QaResult(false, $"Chapter 3 QA arthur say {DescribeStatus(status)}: {outcomeReason}");
    }

    public static Chapter3QaResult TryLeave(IChapter3ArthurWorld? world, Chapter3ArthurVisitPump? visit)
    {
        if (world is null || visit is null)
            return QaRefused("Chapter 3 QA arthur leave refused: Arthur is not wired up.");
        if (!world.HasAuthority)
            return QaRefused("Chapter 3 QA arthur leave refused: host only.");
        if (visit.State != Chapter3ArthurVisitState.Coming && visit.State != Chapter3ArthurVisitState.Arrived)
            return QaRefused($"Chapter 3 QA arthur leave refused: Arthur is {DescribeState(visit.State)}.");

        var status = Guarded(world.TryLeaveArthur, out var outcomeReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
            return new Chapter3QaResult(false, $"Chapter 3 QA arthur leave {DescribeStatus(status)}: {outcomeReason}");
        visit.BeginLeave();
        return new Chapter3QaResult(true, $"Chapter 3 QA arthur leave: {outcomeReason}");
    }

    public static IReadOnlyList<string> TryStatus(IChapter3ArthurWorld? world, Chapter3ArthurVisitPump? visit)
    {
        if (world is null || visit is null)
            return new[] { "Chapter 3 QA arthur status: Arthur is not wired up." };

        var lines = new List<string> { $"arthur: {DescribeState(visit.State)}", $"location: {DescribeLocation(visit.State)}" };
        if (visit.HqForce != Chapter3ArthurHqForce.Auto)
            lines.Add($"hq override: {visit.HqForce.ToString().ToLowerInvariant()}");
        Chapter3ArthurReadStatus readStatus;
        Chapter3ArthurSnapshot? snapshot;
        try
        {
            readStatus = world.TryReadArthur(out snapshot);
        }
        catch (Exception ex)
        {
            lines.Add($"read threw {ex.GetType().Name}: {ex.Message}");
            return lines;
        }

        lines.Add($"read {readStatus}");
        if (readStatus != Chapter3ArthurReadStatus.Ready || snapshot is null)
            return lines;
        if (!snapshot.Present)
        {
            lines.Add("present no; the load-constructed Arthur has not resolved yet.");
            return lines;
        }

        lines.Add($"spawned {YesNo(snapshot.Spawned)}");
        lines.Add($"present yes, visible {YesNo(snapshot.Visible)}, moving {YesNo(snapshot.Moving)}, conscious {YesNo(snapshot.Conscious)}");
        lines.Add($"position ({snapshot.Position.X:0.0}, {snapshot.Position.Y:0.0}, {snapshot.Position.Z:0.0}), " +
            $"distance to player {(float.IsNaN(snapshot.DistanceToPlayer) ? "unknown" : $"{snapshot.DistanceToPlayer:0.0} m")}");
        lines.Add($"registered NPCs carrying {Chapter3ArthurIds.NpcId}: {snapshot.RegisteredCount}" +
            (snapshot.RegisteredCount == 1 ? string.Empty : " (expected exactly 1)"));
        return lines;
    }

    /// <summary>
    /// Forces Arthur's HQ presence on or off for tuning, or back to auto (the shipped
    /// behaviour). Only ever flips the pump's override; the pump still places, parks, and
    /// gates the spawn itself.
    /// </summary>
    public static Chapter3QaResult TryHq(IChapter3ArthurWorld? world, Chapter3ArthurVisitPump? visit, string? mode)
    {
        if (world is null || visit is null)
            return QaRefused("Chapter 3 QA arthur hq refused: Arthur is not wired up.");
        if (!world.HasAuthority)
            return QaRefused("Chapter 3 QA arthur hq refused: host only.");
        var force = (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "on" => Chapter3ArthurHqForce.On,
            "off" => Chapter3ArthurHqForce.Off,
            "auto" => Chapter3ArthurHqForce.Auto,
            _ => (Chapter3ArthurHqForce?)null
        };
        if (force is null)
            return QaRefused("Usage: oc_ch3 arthur hq <on | off | auto>");

        var previous = visit.HqForce;
        visit.HqForce = force.Value;
        return new Chapter3QaResult(true,
            $"Chapter 3 QA arthur hq: HQ presence override {force.Value.ToString().ToLowerInvariant()} (was {previous.ToString().ToLowerInvariant()}).");
    }

    /// <summary>
    /// Captures the player's current position and yaw as Arthur's HQ spot for the session and
    /// re-places him (unless a visit is mid-flight, when the override waits for the visit to
    /// end). Refused outside the HQ. The message carries the local values ready to paste.
    /// </summary>
    public static Chapter3QaResult TryHqHere(IChapter3ArthurWorld? world, Chapter3ArthurVisitPump? visit)
    {
        if (world is null || visit is null)
            return QaRefused("Chapter 3 QA arthur hq here refused: Arthur is not wired up.");
        if (!world.HasAuthority)
            return QaRefused("Chapter 3 QA arthur hq here refused: host only.");
        var capture = world.TryCaptureHqSpot(out var local, out var yaw, out var captureReason);
        if (capture != Chapter3ArthurMutationStatus.Succeeded)
            return QaRefused($"Chapter 3 QA arthur hq here refused: {captureReason}");
        if (visit.State is Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived or Chapter3ArthurVisitState.Leaving)
            return new Chapter3QaResult(true,
                $"Chapter 3 QA arthur hq here: Arthur HQ spot: local ({local.X:0.00}, {local.Y:0.00}, {local.Z:0.00}) yaw {yaw:0} (mid-visit; place skipped).");
        var place = world.TryPlaceAtHq(out var placeReason);
        return new Chapter3QaResult(true,
            $"Chapter 3 QA arthur hq here: Arthur HQ spot: local ({local.X:0.00}, {local.Y:0.00}, {local.Z:0.00}) yaw {yaw:0} (place {DescribeStatus(place)}: {placeReason}).");
    }

    private static Chapter3QaResult QaRefused(string message) => new(false, message);

    private static string DescribeState(Chapter3ArthurVisitState state) => state switch
    {
        Chapter3ArthurVisitState.Parked => "parked",
        Chapter3ArthurVisitState.Coming => "coming",
        Chapter3ArthurVisitState.Arrived => "arrived",
        Chapter3ArthurVisitState.Leaving => "leaving",
        Chapter3ArthurVisitState.AtHq => "at the HQ",
        _ => "unknown"
    };

    private static string DescribeLocation(Chapter3ArthurVisitState state) => state switch
    {
        Chapter3ArthurVisitState.AtHq => "hq",
        Chapter3ArthurVisitState.Coming => "visit",
        Chapter3ArthurVisitState.Arrived => "visit",
        Chapter3ArthurVisitState.Leaving => "visit",
        _ => "parked"
    };

    private static string DescribeStatus(Chapter3ArthurMutationStatus status) => status switch
    {
        Chapter3ArthurMutationStatus.Succeeded => "succeeded",
        Chapter3ArthurMutationStatus.Rejected => "rejected",
        Chapter3ArthurMutationStatus.Unavailable => "unavailable",
        Chapter3ArthurMutationStatus.Ambiguous => "ambiguous",
        _ => "unknown"
    };

    private static string YesNo(bool value) => value ? "yes" : "no";

    private delegate Chapter3ArthurMutationStatus Mutation(out string reason);

    private static Chapter3ArthurMutationStatus Guarded(Mutation mutation, out string reason)
    {
        reason = "the Arthur mutation did not run.";
        try
        {
            return mutation(out reason);
        }
        catch (Exception ex)
        {
            reason = $"threw {ex.GetType().Name}: {ex.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }
}
