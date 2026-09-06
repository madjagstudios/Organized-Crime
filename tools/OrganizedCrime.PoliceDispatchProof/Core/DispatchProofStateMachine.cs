namespace OrganizedCrime.PoliceDispatchProof;

public sealed class DispatchProofStateMachine
{
    private readonly HashSet<string> _transitionKeys = new(StringComparer.Ordinal);

    public DispatchProofPhase Phase { get; private set; } = DispatchProofPhase.Detached;

    public string? LastRejectionReason { get; private set; }

    public bool TryTransition(DispatchProofPhase expected, DispatchProofPhase next, string key)
    {
        if (Phase != expected)
            return Reject($"expected phase {expected}, actual phase {Phase}");
        if (string.IsNullOrWhiteSpace(key))
            return Reject("transition key is required");
        if (!_transitionKeys.Add(key))
            return Reject($"duplicate transition key: {key}");
        if (!IsLegal(Phase, next))
        {
            _transitionKeys.Remove(key);
            return Reject($"illegal transition {Phase} -> {next}");
        }

        Phase = next;
        LastRejectionReason = null;
        return true;
    }

    private bool Reject(string reason)
    {
        LastRejectionReason = reason;
        return false;
    }

    private static bool IsLegal(DispatchProofPhase current, DispatchProofPhase next) =>
        current switch
        {
            DispatchProofPhase.Detached => next is DispatchProofPhase.AwaitingLoadAuthorityIdentity or DispatchProofPhase.Stop,
            DispatchProofPhase.AwaitingLoadAuthorityIdentity => next is DispatchProofPhase.BaselineReady or DispatchProofPhase.Inconclusive or DispatchProofPhase.Stop,
            DispatchProofPhase.BaselineReady => next is DispatchProofPhase.Response1Armed or DispatchProofPhase.TeardownPending or DispatchProofPhase.Stop,
            DispatchProofPhase.Response1Armed => next is DispatchProofPhase.Response1Invoked or DispatchProofPhase.TeardownPending or DispatchProofPhase.Stop,
            DispatchProofPhase.Response1Invoked => next is DispatchProofPhase.ObservingRecovery1 or DispatchProofPhase.Inconclusive or DispatchProofPhase.Stop,
            DispatchProofPhase.ObservingRecovery1 => next is DispatchProofPhase.Response2Eligible or DispatchProofPhase.TeardownPending or DispatchProofPhase.Inconclusive or DispatchProofPhase.Stop,
            DispatchProofPhase.Response2Eligible => next is DispatchProofPhase.Response2Invoked or DispatchProofPhase.TeardownPending or DispatchProofPhase.Stop,
            DispatchProofPhase.Response2Invoked => next is DispatchProofPhase.ObservingRecovery2 or DispatchProofPhase.Inconclusive or DispatchProofPhase.Stop,
            DispatchProofPhase.ObservingRecovery2 => next is DispatchProofPhase.TeardownPending or DispatchProofPhase.Inconclusive or DispatchProofPhase.Stop,
            DispatchProofPhase.TeardownPending => next is DispatchProofPhase.Complete or DispatchProofPhase.Inconclusive or DispatchProofPhase.Stop,
            _ => false
        };
}
