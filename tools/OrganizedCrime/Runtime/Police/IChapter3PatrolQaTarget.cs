using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Police;

internal interface IChapter3PatrolQaTarget
{
    Chapter3QaResult PatrolHeat(LocalPressureTier tier) => new(false, "heat: not supported.");
    Chapter3QaResult PatrolLockdown(bool on) => new(false, "lockdown: not supported.");
    Chapter3QaResult PatrolAuto(bool on) => new(false, "patrol auto: not supported.");
    Chapter3QaResult PatrolHere(OcPatrolDuty duty);
    Chapter3QaResult PatrolStop();
    IReadOnlyList<string> PatrolStatus();
}
