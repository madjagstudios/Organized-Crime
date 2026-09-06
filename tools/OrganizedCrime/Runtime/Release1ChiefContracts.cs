using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>Reads this player's Local Pressure ledger. Null means unreadable; the pass writes nothing and retries.</summary>
public delegate LocalPressureState? Release1ChiefLedgerReader(string playerId);

/// <summary>Applies the one validated record wipe. Backed by LocalPressureRuntimeService.TryApplyRecordWipe.</summary>
public delegate LocalPressureEvidenceWriteResult Release1ChiefLedgerWiper(string playerId);

/// <summary>Reads the epoch the wipe and the drain must both agree with.</summary>
public delegate bool Release1ChiefEpochReader(out Guid sessionEpoch, out long loadEpoch);
