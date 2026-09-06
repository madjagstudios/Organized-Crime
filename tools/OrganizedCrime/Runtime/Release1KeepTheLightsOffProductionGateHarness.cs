namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-65 owner QA harness, gate key Home. Proves, without any mission logic, that OC can read every owned
/// property, its employees and its stations, and that the two working reads agree. The first press dumps
/// everything; the second re dumps only the unpaid employees and reports both
/// Employee.IsAnyWorkInProgress()'s own answer and the active behaviour type heuristic's answer, so the
/// two are cross checked rather than the heuristic being trusted alone. Read only: exactly one world call.
/// Mirrors <see cref="Release1TheEnvelopeClosetCashGateHarness"/>, free of any Unity or native game plugin
/// reference so it stays test linkable.
/// </summary>
public static class Release1KeepTheLightsOffProductionGateHarness
{
    public static Release1StagingHarnessResult TryDumpProductionActivity(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });
        var lines = new List<string>();
        try
        {
            if (!TryRead(world, lines, out var activity, out var failure)) return failure;
            lines.Add($"owned properties: {activity!.Properties.Count}");
            foreach (var property in activity.Properties.OrderBy(candidate => candidate.PropertyCode, StringComparer.Ordinal))
            {
                lines.Add($"property {property.PropertyCode} ({property.PropertyName}), " +
                          $"employees {property.Employees.Count}, stations {property.Stations.Count}");
                foreach (var employee in property.Employees.OrderBy(candidate => candidate.EmployeeId, StringComparer.Ordinal))
                    lines.Add(
                        $"employee {employee.EmployeeId} ({employee.DisplayName}) role {employee.Role} " +
                        $"fired {employee.Fired} paidForToday {employee.PaidForToday} " +
                        $"waitingOutside {employee.IsWaitingOutside} ticksSinceLastWork {employee.TicksSinceLastWork} " +
                        $"isAnyWorkInProgress {employee.IsAnyWorkInProgress} " +
                        $"behaviour {employee.ActiveBehaviourName ?? "none"} " +
                        $"behaviourType {employee.ActiveBehaviourTypeName ?? "none"} " +
                        $"behaviourActive {employee.ActiveBehaviourActive}");
                foreach (var station in property.Stations.OrderBy(candidate => candidate.StationGuid, StringComparer.Ordinal))
                    lines.Add(
                        $"station {station.StationGuid} kind {station.Kind} running {station.Running} " +
                        $"operation {station.OperationIdentity ?? "none"} progress {station.Progress} " +
                        // Decision 7's sample: the Cauldron's own raw counters, printed alongside the
                        // derived progress so the counter direction (does RemainingCookTime count up or
                        // down) is directly observable rather than inferred from Progress alone, which
                        // Math.Max(0, ...) can clamp to a constant 0 either way. Null for every other
                        // station kind and for an idle Cauldron.
                        $"cookTime {station.CookTime?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"} " +
                        $"remainingCookTime {station.RemainingCookTime?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}");
            }
            if (activity.Properties.Count == 0)
            {
                lines.Add("no owned property was reported.");
                return new(Release1StagingHarnessStatus.Unavailable, lines);
            }
            lines.Add("press this key three times a few seconds apart while a cauldron is cooking " +
                      "(this key alternates dump, cross check, dump, so the second press runs the " +
                      "cross check, not a second dump), then compare the cauldron's progress figure " +
                      "and its raw cookTime/remainingCookTime pair between the first and third dumps. " +
                      "A running drying rack's progress is permanently 0 by design; that is not a " +
                      "stalled read.");
            return new(Release1StagingHarnessStatus.Succeeded, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while dumping production activity: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    public static Release1StagingHarnessResult TryCrossCheckUnpaidEmployees(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });
        var lines = new List<string>();
        try
        {
            if (!TryRead(world, lines, out var activity, out var failure)) return failure;
            var unpaid = activity!.Properties
                .SelectMany(property => property.Employees.Select(employee => (property, employee)))
                .Where(pair => !pair.employee.PaidForToday)
                .OrderBy(pair => pair.employee.EmployeeId, StringComparer.Ordinal)
                .ToArray();
            if (unpaid.Length == 0)
            {
                lines.Add("no unpaid employee was reported at any owned property.");
                return new(Release1StagingHarnessStatus.Unavailable, lines);
            }

            var disagreements = 0;
            var working = 0;
            foreach (var (property, employee) in unpaid)
            {
                var heuristic = Release1ProductionWorkBehaviours.ReadsAsWorking(employee);
                var agree = heuristic == employee.IsAnyWorkInProgress;
                if (!agree) disagreements++;
                if (employee.IsAnyWorkInProgress) working++;
                lines.Add(
                    $"unpaid employee {employee.EmployeeId} ({employee.DisplayName}) at {property.PropertyCode} " +
                    $"role {employee.Role} isAnyWorkInProgress {employee.IsAnyWorkInProgress} " +
                    $"heuristic {heuristic} agree {agree} " +
                    $"behaviourType {employee.ActiveBehaviourTypeName ?? "none"} " +
                    $"ticksSinceLastWork {employee.TicksSinceLastWork}");
            }
            lines.Add($"{unpaid.Length} unpaid employee(s), {disagreements} disagreement(s), {working} working.");
            return new(
                disagreements == 0 && working == 0
                    ? Release1StagingHarnessStatus.Succeeded
                    : Release1StagingHarnessStatus.Faulted,
                lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while cross checking unpaid employees: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    private static bool TryRead(
        IRelease1SmallCourtesyWorld world,
        List<string> lines,
        out Release1ProductionActivitySnapshot? activity,
        out Release1StagingHarnessResult failure)
    {
        var status = world.TryReadProductionActivity(out var read);
        if (status == Release1SmallCourtesyWorldReadStatus.Ready && read is not null)
        {
            activity = read;
            failure = null!;
            return true;
        }
        activity = null;
        lines.Add($"production activity could not be read, status {status}.");
        failure = new(MapReadStatus(status), lines);
        return false;
    }

    private static Release1StagingHarnessStatus MapReadStatus(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldReadStatus.Pending => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldReadStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldReadStatus.Ready => Release1StagingHarnessStatus.Unavailable,
        _ => Release1StagingHarnessStatus.Faulted
    };
}
