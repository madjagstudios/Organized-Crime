using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseDiagnosticBoundaryTests
{
    [Fact]
    public void Obsolete_warehouse_controls_and_diagnostic_surfaces_are_absent()
    {
        string repositoryRoot = FindRepositoryRoot();
        string modSource = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "OrganizedCrime", "Mod.cs"));
        string organizedCrimeSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(repositoryRoot, "tools", "OrganizedCrime"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        // KeyCode.F9 and F10 are the OC-52 owner QA staging proof keys, KeyCode.F11 is the OC-57
        // hold room proof key, KeyCode.F6 is the OC-58 quantity proof key, KeyCode.F1 is the
        // OC-60 cash gate proof key, KeyCode.F2 is the OC-61 closet cash gate proof key, and
        // KeyCode.F3, F4 and F5 are the OC-69 field contact spike keys, all gated on
        // _ownerQaKeysEnabled alongside F8. KeyCode.F12 stays banned because it is the Steam
        // screenshot key, and KeyCode.F7 stays banned alongside it.
        Assert.DoesNotContain("KeyCode.F12", modSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F7", modSource, StringComparison.Ordinal);

        foreach (string obsoleteSymbol in new[]
                 {
                     "FishWarehouseMappingPointDefinition",
                     "FishWarehouseEmployeePointCapture",
                     "FishWarehouseCoordinateCensus",
                     "FishWarehouseVisualCensus",
                     "FishWarehouseEmployeeAssignmentObserver",
                     "FishWarehouseManagementClipboardObserver",
                     "ObserveEmployeeAssignments",
                     "ObserveManagementClipboard",
                     "CaptureManagementEligibility",
                     "FishWarehouseVisualSourceReportWriter",
                     "fish-warehouse-coordinate-census",
                     "fish-warehouse-employee-points",
                     "fish-warehouse-visual-census",
                     "fish-warehouse-visual-source-report",
                     "Fish Warehouse employee movement trace",
                     "Fish Warehouse employee work dispatch configured",
                     "Fish Warehouse garage collider census",
                     "Fish Warehouse frontage vehicle census: path=",
                     "Fish Warehouse native build Grid source layout",
                     "Fish Warehouse native build Grid diagnostics",
                     "matched-coordinate patch",
                     "parent-tile patch",
                     "Fish Warehouse employee infrastructure is host-only",
                     "Fish Warehouse persistence replay configured",
                     "Starting Fish Warehouse runtime Property",
                     "Fish Warehouse property bounds enabled",
                     "Fish Warehouse frontage clearance scanned",
                     "Fish Warehouse frontage clearance disabled",
                     "runtime Property spawned (objectId=",
                     "Attempting developer-only Fish Warehouse ownership unlock",
                     "Calling Property.SetOwned()",
                     "Fish Warehouse persistence object replay marked complete",
                     "Fish Warehouse restored employee locker adopted",
                     "Fish Warehouse garage preflight passed",
                     "Fish Warehouse authored interior room prepared inactive",
                     "Fish Warehouse garage opening enabled",
                     "Fish Warehouse garage crossing assist armed",
                     "Fish Warehouse garage crossing assist engaged",
                     "Fish Warehouse garage crossing assist completed",
                     "Fish Warehouse garage crossing assist disarmed",
                     "VehicleDiagnosticMargin",
                     "VehicleDiagnosticEnvelope",
                     "IsVehicleLikeSceneObjectName",
                     "IsVehicleInsideClearanceScope"
                 })
        {
            Assert.DoesNotContain(obsoleteSymbol, organizedCrimeSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Owned_property_without_worldspace_container_still_requires_repair()
    {
        Assert.True(FishWarehouseManagementEligibilityDiagnostic.ShouldCreatePropertyWorldspaceUiContainer(
            targetOwned: true,
            hasContainer: false));
        Assert.False(FishWarehouseManagementEligibilityDiagnostic.ShouldCreatePropertyWorldspaceUiContainer(
            targetOwned: true,
            hasContainer: true));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git", "HEAD")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Organized Crime repository root could not be located.");
    }
}
