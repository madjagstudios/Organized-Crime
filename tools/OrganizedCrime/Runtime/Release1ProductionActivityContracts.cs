namespace OrganizedCrime.Runtime;

/// <summary>The five station kinds that expose an operation object or a running flag at all.</summary>
public enum Release1ProductionStationKind { ChemistryStation, LabOven, MixingStation, Cauldron, DryingRack }

/// <summary>One employee, read only. IsAnyWorkInProgress is the only decisive working read this mission
/// has; the behaviour fields and TicksSinceLastWork are Home diagnostics no classifier ever consults.</summary>
public sealed record Release1ProductionEmployeeSnapshot(
    string EmployeeId,
    string DisplayName,
    string Role,
    bool Fired,
    bool PaidForToday,
    bool IsWaitingOutside,
    int TicksSinceLastWork,
    bool IsAnyWorkInProgress,
    string? ActiveBehaviourName,
    string? ActiveBehaviourTypeName,
    bool ActiveBehaviourActive)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EmployeeId)) throw new ArgumentException("An employee needs an id.", nameof(EmployeeId));
        if (string.IsNullOrWhiteSpace(DisplayName)) throw new ArgumentException("An employee needs a display name.", nameof(DisplayName));
        if (string.IsNullOrWhiteSpace(Role)) throw new ArgumentException("An employee needs a role.", nameof(Role));
        if (TicksSinceLastWork < 0) throw new ArgumentOutOfRangeException(nameof(TicksSinceLastWork));
    }
}

/// <summary>One production station. OperationIdentity is never null while Running. Progress is an increasing
/// counter, or zero where a kind's direction is unproven, so a decrease is always a genuine restart.
/// CookTime and RemainingCookTime are the Cauldron's own raw counters, carried alongside the derived
/// Progress so the Home gate harness can print both: null for every other station kind, and for a
/// Cauldron that is not currently running.</summary>
public sealed record Release1ProductionStationSnapshot(
    string StationGuid,
    Release1ProductionStationKind Kind,
    bool Running,
    string? OperationIdentity,
    int Progress,
    int? CookTime = null,
    int? RemainingCookTime = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StationGuid)) throw new ArgumentException("A station needs a guid.", nameof(StationGuid));
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("Station kind is not defined.", nameof(Kind));
        if (Progress < 0) throw new ArgumentOutOfRangeException(nameof(Progress));
        if (Running && string.IsNullOrWhiteSpace(OperationIdentity))
            throw new ArgumentException("A running station must report an operation identity.", nameof(OperationIdentity));
    }
}

/// <summary>One owned property, its employees and its production stations. Grow containers are never read.</summary>
public sealed record Release1ProductionPropertySnapshot(
    string PropertyCode,
    string PropertyName,
    IReadOnlyList<Release1ProductionEmployeeSnapshot> Employees,
    IReadOnlyList<Release1ProductionStationSnapshot> Stations)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PropertyCode)) throw new ArgumentException("A property needs a code.", nameof(PropertyCode));
        if (string.IsNullOrWhiteSpace(PropertyName)) throw new ArgumentException("A property needs a name.", nameof(PropertyName));
        if (Employees is null) throw new ArgumentNullException(nameof(Employees));
        if (Stations is null) throw new ArgumentNullException(nameof(Stations));
        foreach (var employee in Employees)
        {
            if (employee is null) throw new ArgumentException("Employees cannot be null.", nameof(Employees));
            employee.Validate();
        }
        foreach (var station in Stations)
        {
            if (station is null) throw new ArgumentException("Stations cannot be null.", nameof(Stations));
            station.Validate();
        }
        if (Employees.Select(employee => employee.EmployeeId).Distinct(StringComparer.Ordinal).Count() != Employees.Count)
            throw new ArgumentException("Employee ids must be unique within a property.", nameof(Employees));
        if (Stations.Select(station => station.StationGuid).Distinct(StringComparer.Ordinal).Count() != Stations.Count)
            throw new ArgumentException("Station guids must be unique within a property.", nameof(Stations));
    }
}

/// <summary>Every property the player owns, deduplicated by PropertyCode across Property.OwnedProperties and
/// Business.OwnedBusinesses, in one read. OC never writes any part of this.</summary>
public sealed record Release1ProductionActivitySnapshot(IReadOnlyList<Release1ProductionPropertySnapshot> Properties)
{
    public static Release1ProductionActivitySnapshot Empty { get; } =
        new(Array.Empty<Release1ProductionPropertySnapshot>());

    public void Validate()
    {
        if (Properties is null) throw new ArgumentNullException(nameof(Properties));
        foreach (var property in Properties)
        {
            if (property is null) throw new ArgumentException("Properties cannot be null.", nameof(Properties));
            property.Validate();
        }
        if (Properties.Select(property => property.PropertyCode).Distinct(StringComparer.Ordinal).Count() != Properties.Count)
            throw new ArgumentException("Property codes must be unique.", nameof(Properties));
        var guids = Properties.SelectMany(property => property.Stations).Select(station => station.StationGuid).ToArray();
        if (guids.Distinct(StringComparer.Ordinal).Count() != guids.Length)
            throw new ArgumentException("Station guids must be unique across every owned property.", nameof(Properties));
    }
}

/// <summary>Work behaviour type names as strings, so nothing that consumes them needs an IL2CPP reference and
/// the harness stays test linkable. Every name is reflection verified. A Home diagnostic only: decision 5
/// makes Employee.IsAnyWorkInProgress() the decisive read, and no classifier calls anything here.</summary>
public static class Release1ProductionWorkBehaviours
{
    public static IReadOnlyList<string> WorkBehaviourTypeNames { get; } = new[]
    {
        "StartChemistryStationBehaviour", "StartLabOvenBehaviour", "FinishLabOvenBehaviour",
        "StartCauldronBehaviour", "StartMixingStationBehaviour", "PackagingStationBehaviour",
        "BrickPressBehaviour", "StartDryingRackBehaviour", "StopDryingRackBehaviour",
        "PickUpTrashBehaviour", "EmptyTrashGrabberBehaviour", "BagTrashCanBehaviour",
        "DisposeTrashBagBehaviour", "UseSpawnStationBehaviour", "AddSoilToGrowContainerBehaviour",
        "ApplyAdditiveToGrowContainerBehaviour", "SowSeedInPotBehaviour", "WaterPotBehaviour",
        "HarvestPotBehaviour", "MistMushroomBedBehaviour", "HarvestMushroomBedBehaviour",
        "ApplySpawnToMushroomBedBehaviour"
    };

    public static bool ReadsAsWorking(Release1ProductionEmployeeSnapshot? employee) =>
        employee is not null &&
        employee.ActiveBehaviourActive &&
        employee.ActiveBehaviourTypeName is { } name &&
        WorkBehaviourTypeNames.Contains(name, StringComparer.Ordinal);
}

/// <summary>The one counter whose direction managed reflection cannot settle: RemainingCookTime is paired with
/// a fixed CookTime, the shape of a countdown, and IL2CPP interop decompiles to native stubs. The gate
/// samples it live; either way this converts it to an increasing quantity.</summary>
public static class Release1ProductionStationProgress
{
    public const bool CauldronRemainingCountsDown = true;

    public static int CauldronProgress(int cookTime, int remainingCookTime, bool remainingCountsDown) =>
        remainingCountsDown ? Math.Max(0, cookTime - remainingCookTime) : Math.Max(0, remainingCookTime);
}
