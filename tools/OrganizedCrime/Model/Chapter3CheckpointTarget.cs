namespace OrganizedCrime.Model;

/// <summary>
/// The daily checkpoint target: a property code plus the elapsed day it was
/// picked. Both stay null until the first pick; the pair is persisted in the
/// Chapter 3 save.
/// </summary>
public sealed record Chapter3CheckpointTarget(string? PropertyCode, int? PickDay);
