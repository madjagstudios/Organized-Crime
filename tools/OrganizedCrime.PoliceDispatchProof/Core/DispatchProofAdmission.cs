namespace OrganizedCrime.PoliceDispatchProof;

public sealed class DispatchProofAdmission
{
    private readonly DispatchProofStateMachine _stateMachine;
    private readonly HashSet<string> _triggerKeys = new(StringComparer.Ordinal);
    private readonly HashSet<DispatchProofTrigger> _triggers = new();

    public DispatchProofAdmission(DispatchProofStateMachine stateMachine) =>
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

    public int AcceptedCallCount { get; private set; }

    public DispatchProofAdmissionResult TryAdmit(
        DispatchProofTrigger trigger,
        string triggerKey,
        DispatchProofContext context)
    {
        if (AcceptedCallCount >= 2)
            return Reject("maximum of two Dispatch calls has already been admitted");
        if (string.IsNullOrWhiteSpace(triggerKey) || !_triggerKeys.Add(triggerKey))
            return Reject("duplicate or missing owner trigger key");
        if (!_triggers.Add(trigger))
        {
            _triggerKeys.Remove(triggerKey);
            return Reject($"duplicate response trigger: {trigger}");
        }
        if (!IsContextSafe(context, out var contextReason))
        {
            _triggers.Remove(trigger);
            _triggerKeys.Remove(triggerKey);
            return Reject(contextReason);
        }

        var expectedPhase = trigger == DispatchProofTrigger.Response1
            ? DispatchProofPhase.Response1Armed
            : DispatchProofPhase.Response2Eligible;
        var invokedPhase = trigger == DispatchProofTrigger.Response1
            ? DispatchProofPhase.Response1Invoked
            : DispatchProofPhase.Response2Invoked;
        if (!_stateMachine.TryTransition(expectedPhase, invokedPhase, $"admit:{triggerKey}"))
        {
            _triggers.Remove(trigger);
            _triggerKeys.Remove(triggerKey);
            return Reject(_stateMachine.LastRejectionReason ?? "wrong proof phase");
        }

        AcceptedCallCount++;
        return new(true, "admitted exact owner-triggered native Dispatch shape", AcceptedCallCount);
    }

    private static bool IsContextSafe(DispatchProofContext context, out string reason)
    {
        if (!context.IsAuthoritativeHost)
        {
            reason = "authority is not the authoritative host";
            return false;
        }
        if (!context.IsSinglePlayer)
        {
            reason = "proof is not running in the required single-player scope";
            return false;
        }
        if (string.IsNullOrWhiteSpace(context.CanonicalTargetIdentity))
        {
            reason = "canonical target identity is unavailable";
            return false;
        }
        if (!string.Equals(context.CanonicalTargetIdentity, context.ActualTargetIdentity, StringComparison.Ordinal))
        {
            reason = "canonical target identity does not match the dispatch target";
            return false;
        }
        if (string.IsNullOrWhiteSpace(context.BuildPinProvenance))
        {
            reason = "build pin provenance is missing; verify the pinned build independently before the live run";
            return false;
        }
        if (string.IsNullOrWhiteSpace(context.DispatchSignatureProvenance))
        {
            reason = "Dispatch signature provenance is missing; verify the callable surface independently before the live run";
            return false;
        }
        if (context.MorePatrolsPresent)
        {
            reason = "MorePatrols is present; proof must stop fail-closed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private DispatchProofAdmissionResult Reject(string reason) =>
        new(false, reason, AcceptedCallCount);
}
