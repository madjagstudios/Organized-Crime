namespace OrganizedCrime.Model;

/// <summary>
/// OC-118. The read-only protection view Chapter 3 presents: the offer flag, the absolute cover
/// end day, and whether the Chief's own clock calls it covered now. The quest planner re-derives
/// cover from the end day against Chapter 3's own processed day instead of trusting CoveredNow,
/// so the line and the journal never disagree by a tick.
/// </summary>
public sealed record Release1ChiefProtectionSnapshot(bool Offered, int? ProtectedUntilDay, bool CoveredNow);
