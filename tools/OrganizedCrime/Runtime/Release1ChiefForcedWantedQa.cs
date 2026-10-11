namespace OrganizedCrime.Runtime;

/// <summary>
/// Owner QA only (OwnerQaKeys): the read-only forced-Wanted view QA reports: the lockdown and
/// its age, the warning, and every live trigger input. Null fields are unreadable inputs,
/// never guesses.
/// </summary>
public sealed record Release1ChiefForcedWantedSnapshot(
    bool HasRecord,
    bool LockdownEngaged,
    double? LockdownAgeHours,
    bool Warned,
    bool? CurfewActive,
    bool? Outdoors,
    bool Protected,
    int? PursuitLevel,
    int? LastNightFired,
    int Fires,
    int Dispatches,
    int Refusals,
    bool Armed,
    bool Held,
    float? TimeSinceSighted,
    float? SearchTime,
    bool? Arrested);

/// <summary>
/// Owner QA only (OwnerQaKeys): what the oc_ch3 lockdown and wanted commands drive.
/// Implemented by <see cref="Release1ChiefService"/>; faked in tests.
/// </summary>
public interface IRelease1ChiefForcedWantedQa
{
    Chapter3QaResult QaLockdownAge(double hours);
    Release1ChiefForcedWantedSnapshot QaForcedWantedSnapshot();
}
