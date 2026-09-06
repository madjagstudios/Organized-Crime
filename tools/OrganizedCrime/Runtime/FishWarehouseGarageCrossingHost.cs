using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;
using UnityEngine.AI;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-7 garage off-mesh-link crossing assist. Schedule I only auto-completes an
/// off-mesh link for a Ladder, so an employee running a dock→interior route
/// dead-stops at Organized Crime's component-less garage link. This host — host
/// authority only, scoped to Organized Crime's own property employees and its
/// own garage link — proactively enables the agent's native auto-traverse (the
/// cheap first layer) and, if an agent still stalls on the link, takes over and
/// glides it across at walk speed (the robust fallback), then completes the
/// native link and restores the agent. It never patches global movement and
/// never touches the frozen navigation surfaces/links themselves; it only assists
/// the agent across an existing link. All decisions live in the pure
/// <see cref="FishWarehouseGarageCrossingDefinition"/>; this type is the thin
/// Unity boundary.
/// </summary>
public sealed class FishWarehouseGarageCrossingHost
{
    private const float GarageLinkScopeRadius = 8f;
    // Glide a little past the far link endpoint so the agent lands on solid
    // exterior navmesh rather than exactly on the link boundary (which would let
    // it immediately re-enter the link).
    private const float GlideOvershoot = 0.9f;
    // After a completed crossing, suppress re-detection briefly so the assist
    // cannot thrash while native navigation re-paths the agent from the far side.
    private const int GlideCooldownFrames = 90;

    private readonly Dictionary<int, FishWarehouseGarageCrossingAgentState> _states = new();
    private readonly Dictionary<int, SuspendedAgent> _suspended = new();
    private readonly Dictionary<int, Vector3> _glideTargets = new();
    private readonly Dictionary<int, int> _cooldownUntilFrame = new();
    private bool _active;
    private Vector3 _garageWorldCenter;

    public bool IsActive => _active;

    /// <summary>
    /// Enables native off-mesh auto-traversal on the property's employees and
    /// arms the assist. Idempotent. <paramref name="garageWorldCenter"/> scopes
    /// the assist to Organized Crime's garage link.
    /// </summary>
    public void Activate(Vector3 garageWorldCenter)
    {
        _garageWorldCenter = garageWorldCenter;
        _active = true;

        // Deliberately does NOT mutate the agent (e.g. autoTraverseOffMeshLink):
        // the game drives NPC movement manually so that flag is inert, and leaving
        // the agent untouched keeps the assist purely reactive — it acts only when
        // an employee is genuinely stalled on Organized Crime's garage link.
    }

    /// <summary>Per-tick assist. Safe to call every frame while owned; host authority required.</summary>
    public void Tick(Property property, float deltaTime, bool hostAuthority)
    {
        if (!_active || !hostAuthority)
            return;

        try
        {
            foreach (NavMeshAgent agent in EnumerateAgents(property))
                TickAgent(agent, deltaTime);
        }
        catch
        {
            // Never let the assist throw into the owned-features loop.
        }
    }

    /// <summary>Restores every agent this host suspended and disarms. Idempotent and reversible.</summary>
    public void Deactivate()
    {
        foreach (SuspendedAgent suspended in _suspended.Values.ToArray())
            RestoreAgent(suspended);
        _suspended.Clear();
        _states.Clear();
        _glideTargets.Clear();
        _cooldownUntilFrame.Clear();
        _active = false;
    }

    private void TickAgent(NavMeshAgent agent, float deltaTime)
    {
        int id = agent.GetInstanceID();
        if (!_states.TryGetValue(id, out FishWarehouseGarageCrossingAgentState? state))
        {
            state = new FishWarehouseGarageCrossingAgentState();
            _states[id] = state;
        }

        bool onGarageLink = TryGetGarageLink(agent, out Vector3 start, out Vector3 end);
        Vector3 position = agent.transform.position;

        // Suppress re-detection during the post-crossing cooldown so the assist
        // does not thrash while native navigation re-paths the agent.
        if (onGarageLink && state.Phase == FishWarehouseGarageCrossingPhase.Idle &&
            _cooldownUntilFrame.TryGetValue(id, out int until) && Time.frameCount < until)
        {
            onGarageLink = false;
        }

        // The glide target is fixed when the glide begins and reused for its whole
        // duration — recomputing the "far" endpoint each tick would flip it once
        // the agent passes the link midpoint and send it back.
        Vector3 exit;
        if (state.Phase == FishWarehouseGarageCrossingPhase.Gliding &&
            _glideTargets.TryGetValue(id, out Vector3 storedTarget))
        {
            exit = storedTarget;
        }
        else if (onGarageLink)
        {
            bool startIsFar = Vector3.Distance(position, start) >= Vector3.Distance(position, end);
            Vector3 far = startIsFar ? start : end;
            Vector3 near = startIsFar ? end : start;
            Vector3 dir = far - near;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
            exit = far + (dir * GlideOvershoot);
        }
        else
        {
            exit = position;
        }

        var sample = new FishWarehouseGarageCrossingSample(
            onGarageLink,
            SafeSpeed(agent),
            ToModel(position),
            ToModel(exit));

        FishWarehouseGarageCrossingDecision decision =
            FishWarehouseGarageCrossingDefinition.Decide(state, sample, deltaTime, hostAuthority: true);

        switch (decision.Kind)
        {
            case FishWarehouseGarageCrossingActionKind.BeginGlide:
                _glideTargets[id] = exit;
                BeginGlide(agent, id);
                break;
            case FishWarehouseGarageCrossingActionKind.StepGlide:
                StepGlide(agent, decision.GlideTarget);
                break;
            case FishWarehouseGarageCrossingActionKind.CompleteGlide:
                CompleteGlide(agent, id, decision.GlideTarget);
                break;
        }
    }

    private void BeginGlide(NavMeshAgent agent, int id)
    {
        try
        {
            if (!_suspended.ContainsKey(id))
            {
                // Capture the destination so we can re-issue it after crossing —
                // without a fresh path the agent keeps a stale path that walks it
                // straight back onto the link, looping forever.
                Vector3 destination = SafeDestination(agent, agent.transform.position);
                _suspended[id] = new SuspendedAgent(
                    agent, agent.updatePosition, agent.updateRotation, agent.isStopped, destination);
                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            // Do NOT complete the link here: keep the agent on the link and glide
            // its transform across at walk speed over the next ticks, so the
            // crossing reads as walking, then complete + re-path at the far side.
        }
        catch
        {
            RestoreById(id);
        }
    }

    private void StepGlide(NavMeshAgent agent, FishWarehouseNativeNavigationVector target)
    {
        try
        {
            agent.transform.position = ToUnity(target);
        }
        catch
        {
            // A failed manual step is recovered by CompleteGlide/Deactivate restore.
        }
    }

    private void CompleteGlide(NavMeshAgent agent, int id, FishWarehouseNativeNavigationVector exit)
    {
        Vector3 destination = _suspended.TryGetValue(id, out SuspendedAgent s) ? s.Destination : agent.transform.position;
        try
        {
            // Seat the agent on the far navmesh via the native link completion, then
            // restore its own movement and re-issue the destination so it paths
            // onward from the far side rather than re-entering the link.
            try { agent.CompleteOffMeshLink(); } catch { /* may already be off the link */ }
            RestoreById(id);
            try { agent.SetDestination(destination); } catch { /* behaviour will re-issue */ }

            _glideTargets.Remove(id);
            _cooldownUntilFrame[id] = Time.frameCount + GlideCooldownFrames;
        }
        catch
        {
            RestoreById(id);
        }
    }

    private bool TryGetGarageLink(NavMeshAgent agent, out Vector3 start, out Vector3 end)
    {
        start = Vector3.zero;
        end = Vector3.zero;
        try
        {
            if (!agent.isOnOffMeshLink)
                return false;

            OffMeshLinkData data = agent.currentOffMeshLinkData;
            if (!data.valid)
                return false;

            start = data.startPos;
            end = data.endPos;
            Vector3 midpoint = (start + end) * 0.5f;
            return Vector3.Distance(midpoint, _garageWorldCenter) <= GarageLinkScopeRadius;
        }
        catch
        {
            return false;
        }
    }

    private void RestoreById(int id)
    {
        if (_suspended.TryGetValue(id, out SuspendedAgent suspended))
        {
            RestoreAgent(suspended);
            _suspended.Remove(id);
        }

        _glideTargets.Remove(id);
    }

    private static void RestoreAgent(SuspendedAgent suspended)
    {
        try
        {
            NavMeshAgent agent = suspended.Agent;
            if (agent is null || agent == null)
                return;

            agent.updatePosition = suspended.UpdatePosition;
            agent.updateRotation = suspended.UpdateRotation;
            agent.isStopped = suspended.IsStopped;
        }
        catch
        {
            // Best-effort restore during teardown.
        }
    }

    private static IEnumerable<NavMeshAgent> EnumerateAgents(Property property)
    {
        if (property is null || property == null || property.Employees is null)
            yield break;

        foreach (Employee employee in property.Employees)
        {
            NavMeshAgent? agent = null;
            try
            {
                if (employee is not null && employee != null &&
                    employee.Movement is not null && employee.Movement != null &&
                    employee.Movement.Agent is not null && employee.Movement.Agent != null)
                {
                    agent = employee.Movement.Agent;
                }
            }
            catch
            {
                agent = null;
            }

            if (agent is not null)
                yield return agent;
        }
    }

    private static float SafeSpeed(NavMeshAgent agent)
    {
        try
        {
            return agent.velocity.magnitude;
        }
        catch
        {
            return 0f;
        }
    }

    private static FishWarehouseNativeNavigationVector ToModel(Vector3 value) => new(value.x, value.y, value.z);

    private static Vector3 ToUnity(FishWarehouseNativeNavigationVector value) => new(value.X, value.Y, value.Z);

    private static Vector3 SafeDestination(NavMeshAgent agent, Vector3 fallback)
    {
        try
        {
            return agent.hasPath || agent.pathPending ? agent.destination : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private readonly struct SuspendedAgent
    {
        public SuspendedAgent(NavMeshAgent agent, bool updatePosition, bool updateRotation, bool isStopped, Vector3 destination)
        {
            Agent = agent;
            UpdatePosition = updatePosition;
            UpdateRotation = updateRotation;
            IsStopped = isStopped;
            Destination = destination;
        }

        public NavMeshAgent Agent { get; }
        public bool UpdatePosition { get; }
        public bool UpdateRotation { get; }
        public bool IsStopped { get; }
        public Vector3 Destination { get; }
    }
}
