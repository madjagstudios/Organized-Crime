using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

internal enum Chapter3ShutdownObservation { Unreadable, NotOwned, Working, Quiet }

internal static class Chapter3ShutdownClassifier
{
    internal static Chapter3ShutdownObservation Classify(Release1ProductionActivitySnapshot? snapshot,
        string code, out Release1ProductionPropertySnapshot? target)
    {
        target = null;
        if (snapshot is null || string.IsNullOrWhiteSpace(code)) return Chapter3ShutdownObservation.Unreadable;
        try { snapshot.Validate(); }
        catch (ArgumentException) { return Chapter3ShutdownObservation.Unreadable; }
        target = snapshot.Properties.SingleOrDefault(p => p.PropertyCode == code);
        if (target is null) return Chapter3ShutdownObservation.NotOwned;
        return target.Employees.Any(e => e.IsAnyWorkInProgress) || target.Stations.Any(s => s.Running)
            ? Chapter3ShutdownObservation.Working : Chapter3ShutdownObservation.Quiet;
    }
}
