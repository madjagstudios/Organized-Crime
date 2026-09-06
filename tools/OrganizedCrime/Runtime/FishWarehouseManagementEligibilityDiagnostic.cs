namespace OrganizedCrime.Runtime;

/// <summary>
/// Functional management UI predicates. The previous clipboard observer was diagnostic-only;
/// these rules remain because canvas alignment and worldspace UI repair are runtime behavior.
/// </summary>
public static class FishWarehouseManagementEligibilityDiagnostic
{
    private const string PropertyCode = "oc_fishwarehouse";

    public static bool ShouldAlignCanvas(
        bool isOpen,
        string canvasPropertyCode,
        string playerPropertyCode,
        bool targetOwned) =>
        isOpen &&
        targetOwned &&
        string.Equals(playerPropertyCode, PropertyCode, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(canvasPropertyCode, PropertyCode, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldCreatePropertyWorldspaceUiContainer(
        bool targetOwned,
        bool hasContainer) =>
        targetOwned && !hasContainer;
}
