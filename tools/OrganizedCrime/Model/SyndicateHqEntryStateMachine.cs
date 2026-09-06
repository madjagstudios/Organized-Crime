namespace OrganizedCrime.Model;

public enum SyndicateHqEntryState
{
    Detached,
    AwaitingDoor,
    LockedPrompt,
    EnterPrompt,
    Entering,
    Inside,
    Exiting,
    Faulted,
    Disposed
}

public sealed class SyndicateHqEntryStateMachine
{
    public SyndicateHqEntryState State { get; private set; } = SyndicateHqEntryState.Detached;

    public bool MarkReady()
    {
        if (State == SyndicateHqEntryState.Disposed) return false;
        if (State is SyndicateHqEntryState.Detached or SyndicateHqEntryState.Faulted) State = SyndicateHqEntryState.AwaitingDoor;
        return true;
    }

    public bool ObserveDoor(bool unlocked)
    {
        if (State is SyndicateHqEntryState.Disposed or SyndicateHqEntryState.Entering or SyndicateHqEntryState.Inside or SyndicateHqEntryState.Exiting) return false;
        State = unlocked ? SyndicateHqEntryState.EnterPrompt : SyndicateHqEntryState.LockedPrompt;
        return true;
    }

    public bool BeginEntry(bool returnStateValid)
    {
        if (State != SyndicateHqEntryState.EnterPrompt || !returnStateValid) return false;
        State = SyndicateHqEntryState.Entering;
        return true;
    }

    public bool CompleteEntry(bool succeeded)
    {
        if (State != SyndicateHqEntryState.Entering) return false;
        State = succeeded ? SyndicateHqEntryState.Inside : SyndicateHqEntryState.Faulted;
        return succeeded;
    }

    public bool BeginExit()
    {
        if (State != SyndicateHqEntryState.Inside) return false;
        State = SyndicateHqEntryState.Exiting;
        return true;
    }

    public bool BeginRecoveryExit()
    {
        if (State != SyndicateHqEntryState.Faulted) return false;
        State = SyndicateHqEntryState.Exiting;
        return true;
    }

    public bool CompleteExit(bool succeeded)
    {
        if (State != SyndicateHqEntryState.Exiting) return false;
        State = succeeded ? SyndicateHqEntryState.AwaitingDoor : SyndicateHqEntryState.Faulted;
        return succeeded;
    }

    public void Reset()
    {
        if (State != SyndicateHqEntryState.Disposed) State = SyndicateHqEntryState.AwaitingDoor;
    }

    public void Fault()
    {
        if (State != SyndicateHqEntryState.Disposed) State = SyndicateHqEntryState.Faulted;
    }

    public void Dispose() => State = SyndicateHqEntryState.Disposed;
}

public static class SyndicateHqReturnFallbackPlanner
{
    public static SyndicateHqReturnPlan Plan(SyndicateHqExteriorReturnState? state, Guid sessionEpoch, long loadEpoch, string canonicalPlayerId)
    {
        if (state is null || state.SessionEpoch != sessionEpoch || state.LoadEpoch != loadEpoch || !string.Equals(state.CanonicalPlayerId, canonicalPlayerId, StringComparison.Ordinal))
            return new(SyndicateHqReturnPath.NoSafeReturn, SyndicateHqVector3.Zero, "Captured exterior state was stale or unavailable.");
        if (IsFinite(state.CapturedPosition))
            return new(SyndicateHqReturnPath.CapturedExteriorPosition, state.CapturedPosition!.Value, "Captured exterior position was valid.");
        if (IsFinite(state.AccessPointPosition))
            return new(SyndicateHqReturnPath.DoorAccessPoint, state.AccessPointPosition!.Value, "Validated door access point was used.");
            return new(SyndicateHqReturnPath.NoSafeReturn, SyndicateHqVector3.Zero, "No safe exterior return position was available.");
    }

    public static SyndicateHqReturnPlan PlanFreshAccessPoint(
        SyndicateHqExteriorReturnState? state,
        Guid sessionEpoch,
        string canonicalPlayerId,
        SyndicateHqVector3? accessPointPosition)
    {
        if (state is null || state.SessionEpoch != sessionEpoch ||
            !string.Equals(state.CanonicalPlayerId, canonicalPlayerId, StringComparison.Ordinal) ||
            accessPointPosition is null || !accessPointPosition.Value.IsFinite)
            return new(SyndicateHqReturnPath.NoSafeReturn, SyndicateHqVector3.Zero, "Fresh canonical HQ access point was stale or unavailable.");

        return new(SyndicateHqReturnPath.DoorAccessPoint, accessPointPosition.Value, "Fresh canonical door access point was validated.");
    }

    private static bool IsFinite(SyndicateHqVector3? value) => value is not null && value.Value.IsFinite;
}
