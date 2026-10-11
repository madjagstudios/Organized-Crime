namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74: the player-side facts the forced-Wanted trigger reads, as plain data so this file
/// stays test linkable. PursuitLevel is the raw S1API PursuitLevel value (None is 0, Arresting
/// is 2, pinned by the boundary tests), null when the crime data could not be read.
/// Arrested is the S1API Player.IsArrested read, null when unreadable; true or null blocks
/// a fire, so a custody ride never re-triggers Wanted.
/// </summary>
public sealed record Chapter3PlayerExposure(
    bool OnFoot,
    bool Outdoors,
    int? PursuitLevel,
    bool? Arrested,
    string Detail);

/// <summary>
/// OC-74: reads the player exposure. Total: false when the player cannot be read, so a
/// broken read fails closed and nothing fires.
/// </summary>
public interface IChapter3PlayerExposureReader
{
    bool TryRead(out Chapter3PlayerExposure exposure);
}
