namespace OrganizedCrime.Model;

/// <summary>
/// Signals a job board transition produced. Later tickets (messaging, make-goods,
/// the overnight line) consume these; OC-113 only logs them.
/// </summary>
public abstract record Chapter3JobEffect;

public sealed record Chapter3OfferCreatedEffect(Chapter3JobOffer Offer) : Chapter3JobEffect;

public sealed record Chapter3OfferExpiredEffect(Chapter3JobOffer Offer, int RefusalStreak) : Chapter3JobEffect;

public sealed record Chapter3NellHintEffect(int RefusalStreak) : Chapter3JobEffect;

public sealed record Chapter3ArthurCooperationVisitEffect(int RefusalStreak) : Chapter3JobEffect;

public sealed record Chapter3MakeGoodRequiredEffect(int JobId, Chapter3JobType JobType, int ReferenceAmount) : Chapter3JobEffect;

public sealed record Chapter3JobCompletedEffect(int JobId, Chapter3JobType JobType, bool Succeeded) : Chapter3JobEffect;

public sealed record Chapter3DeadlineExtendedEffect(int JobId, int NewDeadlineDay, int Cost) : Chapter3JobEffect;
