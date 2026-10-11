namespace OrganizedCrime.Model;

/// <summary>
/// Signals a weekly-cut transition produced. The runtime logs each one and
/// feeds CutMissed into OC-115's ledger; later tickets (Nell, money) consume
/// these. OC-116 never moves money.
/// </summary>
public abstract record Chapter3WeeklyCutEffect;

public sealed record Chapter3WeeklyCutDueEffect(Chapter3WeeklyCut Cut) : Chapter3WeeklyCutEffect;

public sealed record Chapter3WeeklyCutPaidEffect(Chapter3WeeklyCut Cut) : Chapter3WeeklyCutEffect;

public sealed record Chapter3WeeklyCutMissedEffect(Chapter3WeeklyCut Cut) : Chapter3WeeklyCutEffect;
