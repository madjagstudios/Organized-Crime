using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseManagementEligibilityDiagnosticTests
{
    [Theory]
    [InlineData(true, "tacoticklers", "oc_fishwarehouse", true, true)]
    [InlineData(true, "oc_fishwarehouse", "oc_fishwarehouse", true, false)]
    [InlineData(true, "tacoticklers", "barn", true, false)]
    [InlineData(false, "tacoticklers", "oc_fishwarehouse", true, false)]
    [InlineData(true, "tacoticklers", "oc_fishwarehouse", false, false)]
    public void Canvas_alignment_is_scoped_to_an_open_owned_fish_warehouse(
        bool isOpen,
        string canvasPropertyCode,
        string playerPropertyCode,
        bool targetOwned,
        bool expected)
    {
        Assert.Equal(
            expected,
            FishWarehouseManagementEligibilityDiagnostic.ShouldAlignCanvas(
                isOpen,
                canvasPropertyCode,
                playerPropertyCode,
                targetOwned));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Worldspace_ui_container_is_created_only_for_an_owned_property_that_lacks_one(
        bool targetOwned,
        bool hasContainer,
        bool expected)
    {
        Assert.Equal(
            expected,
            FishWarehouseManagementEligibilityDiagnostic.ShouldCreatePropertyWorldspaceUiContainer(
                targetOwned,
                hasContainer));
    }

}
