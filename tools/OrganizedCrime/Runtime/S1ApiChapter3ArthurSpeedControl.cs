using OrganizedCrime.Model;
using UnityEngine;
using UnityEngine.AI;
using NativeNpc = Il2CppScheduleOne.NPCs.NPC;
using NativeSpeedControl = Il2CppScheduleOne.NPCs.SpeedControl;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-134: the native calls behind <see cref="IChapter3ArthurSpeedControl"/>.
/// The seam is the game's own: <c>NPCMovement.SpeedController</c>, the
/// same stack the flee, combat, patrol, ragdoll-standup, and seated
/// controls all use. The boost goes on as one absolute (metres per
/// second) control at <see cref="Chapter3ArthurTuning.VisitWalkSpeedControlPriority"/>,
/// so it beats the default walk and defers to every stop. Each add or
/// remove recalculates synchronously and the movement's change
/// handler writes the agent speed inline, so the after-read in
/// <see cref="TryAddBoost"/> already shows the boosted speed. Total:
/// false plus no throw, never an exception out.
/// </summary>
public sealed class S1ApiChapter3ArthurSpeedControl : IChapter3ArthurSpeedControl
{
    private readonly Func<GameObject?> _resolveBody;
    private readonly Action<string> _warn;

    public S1ApiChapter3ArthurSpeedControl(Func<GameObject?> resolveBody, Action<string> warn)
    {
        _resolveBody = resolveBody ?? throw new ArgumentNullException(nameof(resolveBody));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public bool TryReadSpeeds(out float moveSpeed, out float agentSpeed)
    {
        moveSpeed = float.NaN;
        agentSpeed = float.NaN;
        try
        {
            if (!TryResolve(out var controller, out var agent))
                return false;
            moveSpeed = controller!.MoveSpeed;
            agentSpeed = agent!.speed;
            return true;
        }
        catch (Exception exception)
        {
            _warn($"OC-134 Arthur walk speed read failed ({exception.GetType().Name}).");
            return false;
        }
    }

    public bool TryAddBoost(float boostedSpeed, out float agentSpeedAfter)
    {
        agentSpeedAfter = float.NaN;
        if (!float.IsFinite(boostedSpeed) || boostedSpeed <= 0f)
            return false;
        try
        {
            if (!TryResolve(out var controller, out var agent))
                return false;
            controller!.AddSpeedControl(
                Chapter3ArthurTuning.VisitWalkSpeedControlId,
                boostedSpeed,
                Chapter3ArthurTuning.VisitWalkSpeedControlPriority,
                NativeSpeedControl.EType.Absolute);
            agentSpeedAfter = agent!.speed;
            return true;
        }
        catch (Exception exception)
        {
            _warn($"OC-134 Arthur walk speed boost failed ({exception.GetType().Name}).");
            return false;
        }
    }

    public bool TryRemoveBoost()
    {
        try
        {
            if (!TryResolve(out var controller, out _))
                return false;
            controller!.RemoveSpeedControl(Chapter3ArthurTuning.VisitWalkSpeedControlId);
            return true;
        }
        catch (Exception exception)
        {
            _warn($"OC-134 Arthur walk speed removal failed ({exception.GetType().Name}).");
            return false;
        }
    }

    private bool TryResolve(out Il2CppScheduleOne.NPCs.NPCSpeedController? controller, out NavMeshAgent? agent)
    {
        controller = null;
        agent = null;
        try
        {
            var body = _resolveBody();
            if (body == null)
                return false;
            var movement = body.GetComponent<NativeNpc>()?.Movement;
            if (movement == null)
                return false;
            controller = movement.SpeedController;
            agent = movement._agent;
            return controller != null && agent != null;
        }
        catch
        {
            controller = null;
            agent = null;
            return false;
        }
    }
}
