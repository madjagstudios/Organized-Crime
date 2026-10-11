namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-132 fix: the engine seam Arthur's HQ placement hangs off. The
/// pocket room has no navmesh, so nothing here may path, warp, or
/// sample one: activate, agent off, physics held, direct transform
/// set, visible, read back. Implemented by
/// <c>S1ApiChapter3ArthurRuntime</c> (game framework and engine only,
/// never linked into tests); faked in tests. Every member is total:
/// a status plus a reason, never an exception out.
/// </summary>
public interface IChapter3ArthurHqBody
{
    Chapter3ArthurMutationStatus TryActivateBody(out string reason);
    Chapter3ArthurMutationStatus TrySetAgentEnabled(bool enabled, out string reason);
    Chapter3ArthurMutationStatus TrySetPhysicsHeld(bool held, out string reason);
    Chapter3ArthurMutationStatus TrySetTransform((float X, float Y, float Z) position, float yawDegrees, out string reason);
    Chapter3ArthurMutationStatus TrySetVisible(bool visible, out string reason);
    Chapter3ArthurMutationStatus TryFacePlayerDirect(out string reason);
    Chapter3ArthurMutationStatus TryReadPlacement(out (float X, float Y, float Z) position, out bool agentEnabled, out string reason);
}

/// <summary>
/// OC-132 fix: the HQ placement order, in test-linkable code. The
/// first attempt warped through <c>Movement</c> while the body was
/// still inactive from the park, then re-enabled a NavMeshAgent in a
/// room with no navmesh, and logged the requested spot instead of
/// where he is -- so the owner walked in and found no Arthur. This
/// activates first, disables the agent and holds physics, sets the
/// transform directly, forces visible, and only reports success when
/// a read-back puts him within <see cref="PlaceToleranceMetres"/>
/// of the spot. Stateless: every call runs the full order, so the
/// maintain pass can re-place after the save-time interior rebuild
/// by calling <see cref="TryPlace"/> again. <see cref="TryRestoreForWalk"/>
/// reverses the hold before any park, come, leave, or re-issue, so a
/// visit always uses the normal agent path again.
/// </summary>
public static class Chapter3ArthurHqPlacer
{
    /// <summary>
    /// A placement counts as succeeded only within this distance of
    /// the spot, measured in full 3D: floating or fallen reads fail.
    /// </summary>
    public const float PlaceToleranceMetres = 0.5f;

    public static Chapter3ArthurMutationStatus TryPlace(
        IChapter3ArthurHqBody? body,
        (float X, float Y, float Z) spot,
        float yawDegrees,
        out string reason)
    {
        reason = "the HQ place did not run.";
        if (body is null)
        {
            reason = "the HQ body was not wired up.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }

        var status = Guarded(body.TryActivateBody, out var stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"activate {status}: {stepReason}";
            return status;
        }
        status = Guarded((out string r) => body.TrySetAgentEnabled(false, out r), out stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"agent off {status}: {stepReason}";
            return status;
        }
        status = Guarded((out string r) => body.TrySetPhysicsHeld(true, out r), out stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"physics hold {status}: {stepReason}";
            return status;
        }
        status = Guarded((out string r) => body.TrySetTransform(spot, yawDegrees, out r), out stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"transform {status}: {stepReason}";
            return status;
        }
        status = Guarded((out string r) => body.TrySetVisible(true, out r), out stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"visible {status}: {stepReason}";
            return status;
        }
        status = GuardedRead(body, out var actual, out var agentEnabled, out var readReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"read-back {status}: {readReason}";
            return status;
        }

        var dx = actual.X - spot.X;
        var dy = actual.Y - spot.Y;
        var dz = actual.Z - spot.Z;
        var distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        reason = $"HQ place: requested {Format(spot)}, actual {Format(actual)}, agent {(agentEnabled ? "on" : "off")}; {readReason}";
        return distance <= PlaceToleranceMetres ? Chapter3ArthurMutationStatus.Succeeded : Chapter3ArthurMutationStatus.Ambiguous;
    }

    /// <summary>
    /// Re-enables the agent and restores physics, in that order. The
    /// runtime calls this before every park, come, leave, and
    /// re-issue, so leaving the HQ always hands a normal walking NPC
    /// back to the agent path. Idempotent: safe when he was never held.
    /// </summary>
    public static Chapter3ArthurMutationStatus TryRestoreForWalk(IChapter3ArthurHqBody? body, out string reason)
    {
        reason = "the movement restore did not run.";
        if (body is null)
        {
            reason = "the HQ body was not wired up.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }

        var status = Guarded((out string r) => body.TrySetAgentEnabled(true, out r), out var stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"agent on {status}: {stepReason}";
            return status;
        }
        status = Guarded((out string r) => body.TrySetPhysicsHeld(false, out r), out stepReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = $"physics restore {status}: {stepReason}";
            return status;
        }

        reason = "movement restored for the walk: agent on, physics restored.";
        return Chapter3ArthurMutationStatus.Succeeded;
    }

    /// <summary>
    /// Turns him toward the player with the transform only: the HQ
    /// has no navmesh, so the agent-backed face is never used there.
    /// </summary>
    public static Chapter3ArthurMutationStatus TryFacePlayer(IChapter3ArthurHqBody? body, out string reason)
    {
        reason = "the HQ face did not run.";
        if (body is null)
        {
            reason = "the HQ body was not wired up.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }
        return Guarded(body.TryFacePlayerDirect, out reason);
    }

    private static string Format((float X, float Y, float Z) point) =>
        $"({point.X:0.00}, {point.Y:0.00}, {point.Z:0.00})";

    private delegate Chapter3ArthurMutationStatus Step(out string reason);

    private static Chapter3ArthurMutationStatus Guarded(Step step, out string reason)
    {
        reason = "the HQ body step did not run.";
        try
        {
            return step(out reason);
        }
        catch (Exception ex)
        {
            reason = $"threw {ex.GetType().Name}: {ex.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    private static Chapter3ArthurMutationStatus GuardedRead(
        IChapter3ArthurHqBody body,
        out (float X, float Y, float Z) position,
        out bool agentEnabled,
        out string reason)
    {
        position = (0f, 0f, 0f);
        agentEnabled = true;
        reason = "the HQ body step did not run.";
        try
        {
            return body.TryReadPlacement(out position, out agentEnabled, out reason);
        }
        catch (Exception ex)
        {
            reason = $"threw {ex.GetType().Name}: {ex.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }
}
