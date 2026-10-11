namespace OrganizedCrime.Model;

/// <summary>
/// Signals a ledger transition produced. Later tickets (messaging, Arthur, the
/// overnight line) consume these; OC-115 only logs them.
/// </summary>
public abstract record Chapter3MakeGoodEffect;

public sealed record Chapter3MakeGoodOpenedEffect(Chapter3MakeGood MakeGood) : Chapter3MakeGoodEffect;

public sealed record Chapter3MakeGoodOverdueEffect(Chapter3MakeGood MakeGood) : Chapter3MakeGoodEffect;

public sealed record Chapter3WarningDueEffect(Chapter3MakeGood MakeGood) : Chapter3MakeGoodEffect;

public sealed record Chapter3BeatingDueEffect(Chapter3MakeGood MakeGood) : Chapter3MakeGoodEffect;

public sealed record Chapter3MakeGoodPaidEffect(Chapter3MakeGood MakeGood) : Chapter3MakeGoodEffect;
