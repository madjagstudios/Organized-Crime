using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseFrontageClearanceDefinitionTests
{
    [Fact]
    public void Center_right_docks_are_fixed_order_non_overlapping_and_inside_clearance_envelope()
    {
        var docks = FishWarehouseLoadingDockDefinition.Docks;

        Assert.Equal(new[] { 0, 1 }, docks.Select(dock => dock.Index));
        Assert.Equal(6.82f, docks[0].CenterX, 1);
        Assert.Equal(6.82f, docks[1].CenterX, 1);
        Assert.Equal("978f8d8e-e2bc-42fd-b218-3aa34cd4fb31", docks[0].DockGuid);
        Assert.Equal("520a20f6-aa83-4458-83bc-9106dcbd7a0c", docks[1].DockGuid);
        Assert.False(FishWarehouseLoadingDockDefinition.Overlaps(docks[0], docks[1]));
        Assert.True(FishWarehouseFrontageClearanceDefinition.ClearanceEnvelope.Contains(docks[0]));
        Assert.True(FishWarehouseFrontageClearanceDefinition.ClearanceEnvelope.Contains(docks[1]));
        Assert.True(FishWarehouseLoadingDockDefinition.HasValidLayout());
    }

    [Fact]
    public void Clearance_envelope_is_expanded_narrowly_from_the_dock_target()
    {
        var dockEnvelope = FishWarehouseFrontageClearanceDefinition.DockEnvelope;
        var clearanceEnvelope = FishWarehouseFrontageClearanceDefinition.ClearanceEnvelope;

        Assert.True(clearanceEnvelope.MinX < dockEnvelope.MinX);
        Assert.True(clearanceEnvelope.MaxX > dockEnvelope.MaxX);
        Assert.True(clearanceEnvelope.MinZ < dockEnvelope.MinZ);
        Assert.True(clearanceEnvelope.MaxZ > dockEnvelope.MaxZ);
        Assert.Equal(-0.5f, clearanceEnvelope.MinX - dockEnvelope.MinX, 3);
        Assert.Equal(0.5f, clearanceEnvelope.MaxX - dockEnvelope.MaxX, 3);
        Assert.Equal(-0.5f, clearanceEnvelope.MinZ - dockEnvelope.MinZ, 3);
        Assert.Equal(0.5f, clearanceEnvelope.MaxZ - dockEnvelope.MaxZ, 3);
    }

    [Fact]
    public void Per_dock_clearance_does_not_expand_a_vehicle_outside_both_dock_regions_into_scope()
    {
        var outsideBounds = new FishWarehouseFrontageBounds(10.0f, 10.5f, 8.0f, 14.5f);

        Assert.Empty(FishWarehouseFrontageClearanceDefinition.GetOverlappingDockIndices(outsideBounds));
        Assert.False(FishWarehouseFrontageClearanceDefinition.IsInsideAnyDockClearanceScope(outsideBounds));
    }

    [Fact]
    public void Per_dock_clearance_reports_only_the_dock_with_overlapping_geometry()
    {
        var dockTwoBounds = new FishWarehouseFrontageBounds(4.0f, 5.0f, 7.0f, 7.5f);

        Assert.Equal(new[] { 1 }, FishWarehouseFrontageClearanceDefinition.GetOverlappingDockIndices(dockTwoBounds));
        Assert.True(FishWarehouseFrontageClearanceDefinition.IsInsideAnyDockClearanceScope(dockTwoBounds));
    }

    [Fact]
    public void Clears_only_static_container_candidates_inside_the_envelope()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/Fish Warehouse/ContainerStack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: Array.Empty<string>(),
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f));

        var decision = FishWarehouseFrontageClearanceDefinition.Evaluate(candidate);

        Assert.Equal(FishWarehouseFrontageClearanceDecision.Clear, decision);
    }

    [Theory]
    [InlineData("Map/Hyland Point/Region_Docks/Red Shipping Container")]
    [InlineData("Map/Hyland Point/Region_Docks/Red Shipping Container (1)")]
    [InlineData("Map/Hyland Point/Region_Docks/Blue Shipping Container")]
    [InlineData("Map/Hyland Point/Region_Docks/Green Shipping Container")]
    public void Recognizes_only_the_observed_direct_dock_shipping_containers(string path)
    {
        Assert.True(FishWarehouseFrontageClearanceDefinition.IsApprovedShippingContainerPath(path));
    }

    [Theory]
    [InlineData("Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Blue Shipping Container")]
    [InlineData("Map/Hyland Point/Region_Docks/Fish Warehouse/Red Shipping Container")]
    [InlineData("Map/Hyland Point/Region_Docks/Red Shipping Container/Child")]
    public void Does_not_approve_nested_or_unrelated_shipping_container_paths(string path)
    {
        Assert.False(FishWarehouseFrontageClearanceDefinition.IsApprovedShippingContainerPath(path));
    }

    [Fact]
    public void Explicit_dock_shipping_container_can_clear_despite_scene_flags()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/Blue Shipping Container",
            ActiveSelf: true,
            IsStatic: false,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: new[] { "UnityEngine.Transform", "UnityEngine.Renderer", "UnityEngine.Collider" },
            Bounds: new FishWarehouseFrontageBounds(-30f, -20f, 20f, 30f),
            HasUnknownBehaviour: true);

        Assert.Equal(FishWarehouseFrontageClearanceDecision.Clear, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Theory]
    [InlineData("LandVehicle")]
    [InlineData("ParkingLot")]
    [InlineData("LoadingDock")]
    [InlineData("VehicleDetector")]
    [InlineData("NPC")]
    [InlineData("DealLocation")]
    [InlineData("TransitEntity")]
    [InlineData("NetworkObject")]
    [InlineData("Property")]
    public void Leaves_protected_candidates_untouched(string protectedTypeName)
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/Fish Warehouse/ContainerStack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: new[] { protectedTypeName },
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f));

        var decision = FishWarehouseFrontageClearanceDefinition.Evaluate(candidate);

        Assert.Equal(FishWarehouseFrontageClearanceDecision.Protected, decision);
    }

    [Fact]
    public void Leaves_billy_or_parking_candidates_untouched_even_without_component_metadata()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/Billy parking/Vehicle",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: Array.Empty<string>(),
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f));

        Assert.Equal(FishWarehouseFrontageClearanceDecision.Protected, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Fact]
    public void Leaves_candidates_under_protected_gameplay_ancestors_untouched()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/SceneContainer/Stack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: Array.Empty<string>(),
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f),
            HasProtectedAncestor: true);

        Assert.Equal(FishWarehouseFrontageClearanceDecision.Protected, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Fact]
    public void Leaves_dynamic_or_non_visual_candidates_untouched()
    {
        var dynamicCandidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/Fish Warehouse/ContainerStack",
            ActiveSelf: true,
            IsStatic: false,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: Array.Empty<string>(),
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f));
        var nonVisualCandidate = dynamicCandidate with { IsStatic = true, HasRenderer = false, HasCollider = false };

        Assert.Equal(FishWarehouseFrontageClearanceDecision.NotEligible, FishWarehouseFrontageClearanceDefinition.Evaluate(dynamicCandidate));
        Assert.Equal(FishWarehouseFrontageClearanceDecision.NotEligible, FishWarehouseFrontageClearanceDefinition.Evaluate(nonVisualCandidate));
    }

    [Fact]
    public void Leaves_candidates_with_unknown_components_untouched()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/ContainerStack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: new[] { "UnityEngine.Transform", "SomeGameplayComponent" },
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f));

        Assert.Equal(FishWarehouseFrontageClearanceDecision.NotEligible, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Fact]
    public void Leaves_generic_component_projection_untouched()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/ContainerStack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: new[] { "UnityEngine.Transform", "UnityEngine.Component", "UnityEngine.Renderer" },
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f));

        Assert.Equal(FishWarehouseFrontageClearanceDecision.NotEligible, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Fact]
    public void Leaves_candidates_with_unknown_behaviours_untouched()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/ContainerStack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: new[] { "UnityEngine.Transform", "UnityEngine.Renderer" },
             Bounds: new FishWarehouseFrontageBounds(2f, 7f, 8f, 14.5f),
            HasUnknownBehaviour: true);

        Assert.Equal(FishWarehouseFrontageClearanceDecision.NotEligible, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Fact]
    public void Leaves_candidates_outside_the_narrow_envelope_untouched()
    {
        var candidate = new FishWarehouseFrontageCandidate(
            "Map/Hyland Point/Region_Docks/Fish Warehouse/ContainerStack",
            ActiveSelf: true,
            IsStatic: true,
            HasRenderer: true,
            HasCollider: true,
            ComponentTypeNames: Array.Empty<string>(),
            Bounds: new FishWarehouseFrontageBounds(-20f, -14f, -13f, -8f));

        Assert.Equal(FishWarehouseFrontageClearanceDecision.OutsideEnvelope, FishWarehouseFrontageClearanceDefinition.Evaluate(candidate));
    }

    [Fact]
    public void Vehicle_scope_uses_per_dock_clearance_for_the_observed_vehicle_geometry()
    {
         var vehicleBounds = new FishWarehouseFrontageBounds(4.0f, 5.0f, 8.0f, 14.5f);

        Assert.Equal(new[] { 0, 1 }, FishWarehouseFrontageClearanceDefinition.GetOverlappingDockIndices(vehicleBounds));
        Assert.True(FishWarehouseFrontageClearanceDefinition.IsInsideAnyDockClearanceScope(vehicleBounds));
    }

    [Fact]
    public void Restore_contract_preserves_each_candidate_original_active_state()
    {
        var states = FishWarehouseFrontageClearanceDefinition.CaptureOriginalStates(new[]
        {
            new FishWarehouseFrontageCandidateState("car", true),
            new FishWarehouseFrontageCandidateState("container", false)
        });

        Assert.Equal(new[] { new FishWarehouseFrontageCandidateState("car", true), new FishWarehouseFrontageCandidateState("container", false) }, states);
        Assert.True(FishWarehouseFrontageClearanceDefinition.CanRestore(states, 2));
        Assert.False(FishWarehouseFrontageClearanceDefinition.CanRestore(states, 1));
    }
}
