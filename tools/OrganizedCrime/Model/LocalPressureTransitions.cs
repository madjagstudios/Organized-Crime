namespace OrganizedCrime.Model;

public static class LocalPressureTransitions
{
    public static LocalPressureTransitionResult ApplyEvidence(
        LocalPressureState state,
        LocalPressureEvidenceEvent evidence,
        LocalPressureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ApplyEvidence(state, evidence, profile, profile.GetHeatDelta(evidence.ReasonCode));
    }

    public static LocalPressureTransitionResult ApplyEvidence(
        LocalPressureState state,
        LocalPressureEvidenceEvent evidence,
        LocalPressureProfile profile,
        int evidenceHeatDelta)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(profile);

        if (double.IsNaN(evidence.GameTimeHours) || double.IsInfinity(evidence.GameTimeHours) || evidence.GameTimeHours < 0)
            throw new ArgumentOutOfRangeException(nameof(evidence), "Evidence game time must be finite and non-negative.");
        // Activity time orders evidence; receipt time starts quiet grace so late delivery cannot erase it.
        var receivedGameTime = evidence.ReceivedGameTimeHours ?? evidence.GameTimeHours;
        if (double.IsNaN(receivedGameTime) || double.IsInfinity(receivedGameTime) || receivedGameTime < 0)
            throw new ArgumentOutOfRangeException(nameof(evidence), "Evidence receipt game time must be finite and non-negative.");
        if (evidenceHeatDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(evidenceHeatDelta), "Evidence heat delta cannot be negative.");
        if (state.PlayerId is not null && evidence.PlayerId is not null &&
            !string.Equals(state.PlayerId, evidence.PlayerId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Evidence player identity does not match the Local Pressure state.", nameof(evidence));
        }

        var previousTier = GetTier(state.LocalHeat, profile);
        var heat = ClampHeat(state.LocalHeat + evidenceHeatDelta);
        var knownOffender = state.KnownOffender || evidence.ReasonCode == LocalPressureReasonCode.Arrest;
        if (knownOffender)
            heat = Math.Max(heat, profile.GetHeatFloor(knownOffender));

        var lastEvidenceGameTime = state.LastEvidenceGameTime is null
            ? evidence.GameTimeHours
            : Math.Max(state.LastEvidenceGameTime.Value, evidence.GameTimeHours);
        var quietGraceUntil = Math.Max(
            state.QuietGraceUntil ?? double.MinValue,
            receivedGameTime + profile.QuietGraceHours);
        var resultState = new LocalPressureState(
            LocalHeat: heat,
            KnownOffender: knownOffender,
            LastEvidenceGameTime: lastEvidenceGameTime,
            QuietGraceUntil: quietGraceUntil,
            LastDecayEvaluation: state.LastDecayEvaluation,
            // PlayerId identifies the ledger; region and property remain latest-observed presentation context.
            PlayerId: evidence.PlayerId ?? state.PlayerId,
            Region: evidence.Region ?? state.Region,
            PropertyCode: evidence.PropertyCode ?? state.PropertyCode,
            Revision: checked(state.Revision + 1));

        return new LocalPressureTransitionResult(
            resultState,
            heat - state.LocalHeat,
            previousTier,
            GetTier(heat, profile),
            evidence.ReasonCode);
    }

    /// <summary>
    /// OC-73 spec decision 15. The one validated way Chief Campbell's payment clears the ledger:
    /// Local Heat to zero, Known Offender false, evidence time, quiet grace and the last decay
    /// evaluation preserved exactly, identity and presentation context preserved exactly, revision
    /// incremented. No new persisted field, so the Local Pressure sidecar schema stays at version 1.
    /// Clearing Known Offender is what actually matters: while it is true the decay floor is the
    /// profile's KnownOffenderFloor (25 on Moderate), so heat can never fall out of Noticed on its own.
    /// </summary>
    public static LocalPressureRecordWipeResult ApplyRecordWipe(LocalPressureState state, LocalPressureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(profile);
        var previousTier = GetTier(state.LocalHeat, profile);
        var wiped = new LocalPressureState(
            LocalHeat: LocalPressureProfile.MinimumHeat,
            KnownOffender: false,
            LastEvidenceGameTime: state.LastEvidenceGameTime,
            QuietGraceUntil: state.QuietGraceUntil,
            LastDecayEvaluation: state.LastDecayEvaluation,
            PlayerId: state.PlayerId,
            Region: state.Region,
            PropertyCode: state.PropertyCode,
            Revision: checked(state.Revision + 1));
        return new LocalPressureRecordWipeResult(
            wiped, LocalPressureProfile.MinimumHeat - state.LocalHeat, previousTier, GetTier(wiped.LocalHeat, profile));
    }

    public static LocalPressureTier GetTier(int heat, LocalPressureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        heat = ClampHeat(heat);
        if (heat <= profile.QuietUpperBound)
            return LocalPressureTier.Quiet;
        if (heat <= profile.NoticedUpperBound)
            return LocalPressureTier.Noticed;
        if (heat <= profile.WatchedUpperBound)
            return LocalPressureTier.Watched;
        return LocalPressureTier.Critical;
    }

    private static int ClampHeat(int heat) => Math.Clamp(heat, LocalPressureProfile.MinimumHeat, LocalPressureProfile.MaximumHeat);
}
