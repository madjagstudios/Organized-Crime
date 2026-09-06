using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseContinuousNavigationCoordinatorTests
{
    [Fact]
    public void Fails_before_mutation_when_the_read_only_geometry_preflight_fails()
    {
        var actions = new FakeActions { GeometryPreflightSucceeds = false };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(new[] { "preflight" }, actions.Calls);
    }

    [Fact]
    public void Continues_after_geometry_preflight_when_the_retired_exterior_path_result_is_false()
    {
        var actions = new FakeActions
        {
            GeometryPreflightSucceeds = true,
            RetiredExteriorPathSucceeds = false,
            Signature = "stable"
        };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        Assert.False(actions.TryRetiredExteriorPath());
        int retiredExteriorPathChecksBeforeStart = actions.RetiredExteriorPathChecks;

        coordinator.Start(0f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, coordinator.State);
        Assert.Equal(new[] { "preflight", "activate-room", "open-garage" }, actions.Calls);
        Assert.Equal(retiredExteriorPathChecksBeforeStart, actions.RetiredExteriorPathChecks);
    }

    [Fact]
    public void Action_contract_has_no_exterior_or_path_preflight_member()
    {
        Assert.DoesNotContain(
            typeof(IFishWarehouseContinuousNavigationActions).GetMembers(),
            member =>
                member.Name.Contains("Exterior", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Activates_room_then_opens_garage_then_builds_after_a_half_second_stable_settle()
    {
        var actions = new FakeActions { Signature = "stable" };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.49f);
        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, coordinator.State);
        coordinator.Tick(0.50f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Ready, coordinator.State);
        Assert.Equal(new[] { "preflight", "activate-room", "open-garage", "build", "validate" }, actions.Calls);
    }

    [Fact]
    public void Pending_navigation_build_retries_and_can_reach_ready_within_the_settle_timeout()
    {
        var actions = new FakeActions { Signature = "stable" };
        actions.BuildResults.Enqueue(FishWarehouseNativeNavigationBuildResult.Pending);
        actions.BuildResults.Enqueue(FishWarehouseNativeNavigationBuildResult.Succeeded);
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, coordinator.State);
        Assert.Equal(new[] { "preflight", "activate-room", "open-garage", "build" }, actions.Calls);

        coordinator.Tick(1.00f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Ready, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "open-garage", "build", "build", "validate" },
            actions.Calls);
    }

    [Fact]
    public void Pending_navigation_build_rolls_back_at_the_existing_five_second_timeout()
    {
        var actions = new FakeActions { Signature = "stable" };
        actions.BuildResults.Enqueue(FishWarehouseNativeNavigationBuildResult.Pending);
        actions.BuildResults.Enqueue(FishWarehouseNativeNavigationBuildResult.Pending);
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);
        coordinator.Tick(4.99f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, coordinator.State);
        Assert.Equal(new[] { "preflight", "activate-room", "open-garage", "build", "build" }, actions.Calls);

        coordinator.Tick(5.00f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[]
            {
                "preflight", "activate-room", "open-garage", "build", "build",
                "remove", "restore-garage", "dispose-room"
            },
            actions.Calls);
    }

    [Fact]
    public void Begins_settle_windows_on_the_first_tick_after_slow_synchronous_startup()
    {
        var actions = new FakeActions { Signature = "stable" };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);
        float firstPostStartTick =
            FishWarehouseContinuousNavigationCoordinator.SettleTimeoutSeconds + 0.01f;

        coordinator.Start(0f);
        coordinator.Tick(firstPostStartTick);

        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, coordinator.State);

        coordinator.Tick(
            firstPostStartTick + FishWarehouseContinuousNavigationCoordinator.StableSettleSeconds + 0.01f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Ready, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "open-garage", "build", "validate" },
            actions.Calls);
    }

    [Fact]
    public void Resets_the_settle_clock_when_identity_or_transform_signature_changes()
    {
        var actions = new FakeActions { Signature = "first" };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        actions.Signature = "second";
        coordinator.Tick(0.40f);
        coordinator.Tick(0.89f);
        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, coordinator.State);
        coordinator.Tick(0.91f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Ready, coordinator.State);
    }

    [Fact]
    public void Rolls_back_in_reverse_order_after_a_post_mutation_navigation_failure_and_is_idempotent()
    {
        var actions = new FakeActions { Signature = "stable", BuildSucceeds = false };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);
        coordinator.Stop();
        coordinator.Stop();

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "open-garage", "build", "remove", "restore-garage", "dispose-room" },
            actions.Calls);
    }

    [Fact]
    public void Rolls_back_when_room_activation_fails_without_attempting_the_garage()
    {
        var actions = new FakeActions { RoomActivationSucceeds = false };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "remove", "restore-garage", "dispose-room" },
            actions.Calls);
    }

    [Fact]
    public void Rolls_back_when_garage_opening_fails_after_room_activation()
    {
        var actions = new FakeActions { GarageOpenSucceeds = false };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "open-garage", "remove", "restore-garage", "dispose-room" },
            actions.Calls);
    }

    [Fact]
    public void Rolls_back_when_built_navigation_fails_validation()
    {
        var actions = new FakeActions { Signature = "stable", ValidationSucceeds = false };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "open-garage", "build", "validate", "remove", "restore-garage", "dispose-room" },
            actions.Calls);
    }

    [Fact]
    public void Times_out_at_exactly_five_seconds_without_building()
    {
        var actions = new FakeActions { Signature = "stable" };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(5.00f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[] { "preflight", "activate-room", "open-garage", "remove", "restore-garage", "dispose-room" },
            actions.Calls);
    }

    [Fact]
    public void Resets_run_state_before_a_second_lifecycle_and_rolls_back_its_failure()
    {
        var actions = new FakeActions { Signature = "first" };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);
        coordinator.Stop();

        actions.Signature = "second";
        actions.BuildSucceeds = false;
        coordinator.Start(10f);
        coordinator.Tick(10.50f);

        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, coordinator.State);
        Assert.Equal(
            new[]
            {
                "preflight", "activate-room", "open-garage", "build", "validate",
                "remove", "restore-garage", "dispose-room",
                "preflight", "activate-room", "open-garage", "build",
                "remove", "restore-garage", "dispose-room"
            },
            actions.Calls);
    }

    [Fact]
    public void Does_not_build_or_remove_twice_after_a_successful_lifecycle()
    {
        var actions = new FakeActions { Signature = "stable" };
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);
        coordinator.Tick(1.00f);
        coordinator.Start(1.00f);
        coordinator.Stop();
        coordinator.Stop();

        Assert.Equal(FishWarehouseContinuousNavigationState.Inactive, coordinator.State);
        Assert.Equal(
            new[]
            {
                "preflight", "activate-room", "open-garage", "build", "validate",
                "remove", "restore-garage", "dispose-room"
            },
            actions.Calls);
    }

    private sealed class FakeActions : IFishWarehouseContinuousNavigationActions
    {
        public bool GeometryPreflightSucceeds { get; set; } = true;
        public bool RetiredExteriorPathSucceeds { get; set; } = true;
        public bool RoomActivationSucceeds { get; set; } = true;
        public bool GarageOpenSucceeds { get; set; } = true;
        public bool BuildSucceeds { get; set; } = true;
        public bool ValidationSucceeds { get; set; } = true;
        public string Signature { get; set; } = string.Empty;
        public List<string> Calls { get; } = new();
        public Queue<FishWarehouseNativeNavigationBuildResult> BuildResults { get; } = new();
        public int RetiredExteriorPathChecks { get; private set; }

        public bool TryPreflightGeometry() => RecordPreflight() && GeometryPreflightSucceeds;
        public bool TryActivateRoom() { Calls.Add("activate-room"); return RoomActivationSucceeds; }
        public bool TryOpenGarage() { Calls.Add("open-garage"); return GarageOpenSucceeds; }
        public string GetBuildableAndConfigurableSignature() => Signature;
        public FishWarehouseNativeNavigationBuildResult TryBuildNavigation()
        {
            Calls.Add("build");
            return BuildResults.Count > 0
                ? BuildResults.Dequeue()
                : BuildSucceeds
                    ? FishWarehouseNativeNavigationBuildResult.Succeeded
                    : FishWarehouseNativeNavigationBuildResult.Failed;
        }
        public bool ValidateNavigation() { Calls.Add("validate"); return ValidationSucceeds; }
        public bool RemoveNavigation() { Calls.Add("remove"); return true; }
        public void RestoreGarage() => Calls.Add("restore-garage");
        public void DisposeRoom() => Calls.Add("dispose-room");

        public bool TryRetiredExteriorPath()
        {
            RetiredExteriorPathChecks++;
            return RetiredExteriorPathSucceeds;
        }

        private bool RecordPreflight()
        {
            Calls.Add("preflight");
            return true;
        }
    }
}
