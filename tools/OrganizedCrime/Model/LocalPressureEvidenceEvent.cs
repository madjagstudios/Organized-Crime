namespace OrganizedCrime.Model;

public sealed record LocalPressureEvidenceEvent(
    LocalPressureReasonCode ReasonCode,
    double GameTimeHours,
    string? PlayerId = null,
    string? Region = null,
    string? PropertyCode = null,
    string? CorrelationId = null,
    double? ReceivedGameTimeHours = null);

public sealed record LocalPressureTransitionResult(
    LocalPressureState State,
    int HeatDelta,
    LocalPressureTier PreviousTier,
    LocalPressureTier CurrentTier,
    LocalPressureReasonCode ReasonCode);

/// <summary>
/// OC-73. The outcome of a Chief Campbell record wipe. Deliberately not a
/// <see cref="LocalPressureTransitionResult"/>: that record carries a
/// <see cref="LocalPressureReasonCode"/>, and a wipe is not evidence, so no member of that enum
/// honestly describes it.
/// </summary>
public sealed record LocalPressureRecordWipeResult(
    LocalPressureState State, int HeatDelta, LocalPressureTier PreviousTier, LocalPressureTier CurrentTier);
