using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqContractTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void ExactShellAndDoorFingerprintIsAccepted()
    {
        var result = SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() }));

        Assert.True(result.Accepted, result.Reason);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("changed")]
    public void ChangedOrAmbiguousFingerprintFailsClosed(string caseName)
    {
        var shells = caseName == "missing" ? Array.Empty<string>() : caseName == "duplicate"
            ? new[] { SyndicateHqContract.NightclubShellPath, SyndicateHqContract.NightclubShellPath }
            : new[] { SyndicateHqContract.NightclubShellPath };
        var door = caseName == "changed" ? Door() with { BuildingGuid = "changed" } : Door();

        var result = SyndicateHqIdentity.Validate(new(shells, new[] { door }));

        Assert.False(result.Accepted);
    }

    [Fact]
    public void Fingerprint_rejects_non_finite_positions_access_points_and_zero_forward()
    {
        Assert.False(SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() with { Position = new(float.NaN, 0, 0) } })).Accepted);
        Assert.False(SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() with { AccessPointPosition = new(float.PositiveInfinity, 0, 0) } })).Accepted);
        Assert.False(SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() with { AccessPointPosition = new(float.NegativeInfinity, 0, 0) } })).Accepted);
        Assert.False(SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() with { AccessPointPosition = null } })).Accepted);
        Assert.False(SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() with { Forward = SyndicateHqVector3.Zero } })).Accepted);
    }

    [Fact]
    public void Fingerprint_compares_non_normalized_forward_by_direction()
    {
        var result = SyndicateHqIdentity.Validate(new(new[] { SyndicateHqContract.NightclubShellPath }, new[] { Door() with { Forward = new(100, 0, 0) } }));

        Assert.True(result.Accepted, result.Reason);
    }

    [Fact]
    public void UnlockRequiresCanonicalOc10RecognitionAndSaveIdentity()
    {
        var intro = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value;
        var recognition = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.Release1Recognized, "recognition").Value;
        var story = Release1StoryState.CreateAccepted(PlayerId, intro) with { Release1Recognized = true, RecognitionLogicalCorrelationIds = new[] { recognition } };
        var context = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, PlayerId, @"C:\Saves\76561190000000001\SaveGame_4");

        var result = SyndicateHqUnlockAdmission.Evaluate(Release1StoryHostContextReadStatus.Ready, context, story);

        Assert.True(result.IsUnlocked, result.Reason);
    }

    [Fact]
    public void UnlockRejectsWrongIdentityEvenWhenStoryIsRecognized()
    {
        var intro = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value;
        var recognition = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.Release1Recognized, "recognition").Value;
        var story = Release1StoryState.CreateAccepted(PlayerId, intro) with { Release1Recognized = true, RecognitionLogicalCorrelationIds = new[] { recognition } };
        var context = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, "76561197984645360", @"C:\Saves\76561190000000001\SaveGame_4");

        Assert.False(SyndicateHqUnlockAdmission.Evaluate(Release1StoryHostContextReadStatus.Ready, context, story).IsUnlocked);
    }

    [Fact]
    public void UnlockAcceptsWrongAddressSatisfiedWithNoRecognition()
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.Satisfied);
        var context = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, PlayerId, @"C:\Saves\76561190000000001\SaveGame_4");

        var result = SyndicateHqUnlockAdmission.Evaluate(Release1StoryHostContextReadStatus.Ready, context, story);

        Assert.True(result.IsUnlocked, result.Reason);
        Assert.Equal(SyndicateHqUnlockDecision.UnlockedByWrongAddressSatisfied().Reason, result.Reason);
    }

    [Theory]
    [InlineData(Release1MissionState.Active)]
    [InlineData(Release1MissionState.Offered)]
    public void UnlockStaysLockedWhenWrongAddressIsNotYetSatisfiedAndThereIsNoRecognition(Release1MissionState state)
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), state);
        var context = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, PlayerId, @"C:\Saves\76561190000000001\SaveGame_4");

        Assert.False(SyndicateHqUnlockAdmission.Evaluate(Release1StoryHostContextReadStatus.Ready, context, story).IsUnlocked);
    }

    [Fact]
    public void UnlockRejectsWrongAddressSatisfiedWhenPlayerIdMismatches()
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.Satisfied);
        var context = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, "76561197984645360", @"C:\Saves\76561190000000001\SaveGame_4");

        Assert.False(SyndicateHqUnlockAdmission.Evaluate(Release1StoryHostContextReadStatus.Ready, context, story).IsUnlocked);
    }

    private static Release1StoryState AcceptedStory(string playerId)
    {
        var introCorrelation = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "test-intro-receipt").Value;
        return Release1StoryState.CreateAccepted(playerId, introCorrelation);
    }

    private static Release1StoryState WithWrongAddressState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.WrongAddress
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "wrong-address-v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-wa-reward-receipt" : null
                }
                : mission).ToArray()
        };

    [Fact]
    public void EntryAndExitStateMachineIsIdempotentAndFailClosed()
    {
        var machine = new SyndicateHqEntryStateMachine();
        Assert.True(machine.MarkReady());
        Assert.True(machine.MarkReady());
        Assert.True(machine.ObserveDoor(true));
        Assert.True(machine.BeginEntry(true));
        Assert.False(machine.BeginEntry(true));
        Assert.True(machine.CompleteEntry(true));
        Assert.True(machine.BeginExit());
        Assert.False(machine.BeginExit());
        Assert.True(machine.CompleteExit(true));
        Assert.Equal(SyndicateHqEntryState.AwaitingDoor, machine.State);
    }

    [Fact]
    public void ReturnPlannerUsesCapturedPositionThenValidatedFallbacks()
    {
        var epoch = Guid.NewGuid();
        var state = new SyndicateHqExteriorReturnState(
            new(1, 2, 3), new(4, 5, 6), epoch, 2, PlayerId);

        var plan = SyndicateHqReturnFallbackPlanner.Plan(state, epoch, 2, PlayerId);

        Assert.Equal(SyndicateHqReturnPath.CapturedExteriorPosition, plan.Path);
        Assert.Equal(new SyndicateHqVector3(1, 2, 3), plan.Position);
    }

    [Fact]
    public void ReturnPlanner_skips_malformed_capture_and_uses_only_validated_access_point()
    {
        var epoch = Guid.NewGuid();
        var state = new SyndicateHqExteriorReturnState(new(float.NaN, 0, 0), new(4, 5, 6), epoch, 2, PlayerId);

        var plan = SyndicateHqReturnFallbackPlanner.Plan(state, epoch, 2, PlayerId);

        Assert.Equal(SyndicateHqReturnPath.DoorAccessPoint, plan.Path);
    }

    [Fact]
    public void ReturnPlanner_has_no_raw_door_position_fallback()
    {
        var epoch = Guid.NewGuid();
        var state = new SyndicateHqExteriorReturnState(new(float.NaN, 0, 0), new(float.PositiveInfinity, 0, 0), epoch, 2, PlayerId);

        Assert.Equal(SyndicateHqReturnPath.NoSafeReturn, SyndicateHqReturnFallbackPlanner.Plan(state, epoch, 2, PlayerId).Path);
    }

    [Fact]
    public void Interior_reuse_requires_exact_owned_root_and_complete_runtime_shell()
    {
        var names = SyndicateHqInteriorDefinition.Markers.Select(marker => marker.Name).ToHashSet(StringComparer.Ordinal);
        var geometry = SyndicateHqInteriorDefinition.RequiredColliderNames.ToHashSet(StringComparer.Ordinal);
        var valid = new SyndicateHqInteriorOwnershipSnapshot("owned", new[] { "owned" }, names, geometry, false);

        Assert.True(SyndicateHqInteriorOwnership.CanReuse(valid, out _));
        Assert.False(SyndicateHqInteriorOwnership.CanReuse(valid with { SceneRootTokens = new[] { "foreign" } }, out _));
        Assert.False(SyndicateHqInteriorOwnership.CanReuse(valid with { SceneRootTokens = new[] { "owned", "duplicate" } }, out _));
        Assert.False(SyndicateHqInteriorOwnership.CanReuse(valid with { ColliderGeometryNames = new HashSet<string>() }, out _));
        Assert.False(SyndicateHqInteriorOwnership.CanReuse(valid with { DestroyPending = true }, out _));
    }

    [Fact]
    public void Native_root_ownership_uses_unity_instance_identity_instead_of_managed_wrapper_identity()
    {
        Assert.True(SyndicateHqInteriorOwnership.IsRetainedNativeRoot(42, new[] { 42 }, out _));
        Assert.False(SyndicateHqInteriorOwnership.IsRetainedNativeRoot(42, new[] { 73 }, out var mismatchReason));
        Assert.Contains("did not match", mismatchReason, StringComparison.Ordinal);
        Assert.False(SyndicateHqInteriorOwnership.IsRetainedNativeRoot(42, new[] { 42, 42 }, out var duplicateReason));
        Assert.Contains("2 HQ roots", duplicateReason, StringComparison.Ordinal);
    }

    [Fact]
    public void InteriorHasStableEntryExitAndInertStorageMarkers()
    {
        Assert.True(SyndicateHqInteriorDefinition.IsValid(out var reason), reason);
        Assert.Equal(new[] { SyndicateHqContract.EntryMarker, SyndicateHqContract.ExitMarker, SyndicateHqContract.StorageMarker }, SyndicateHqInteriorDefinition.Markers.Select(marker => marker.Name));
        Assert.True(SyndicateHqInteriorDefinition.Markers.Single(marker => marker.Name == SyndicateHqContract.StorageMarker).IsInert);
    }

    [Fact]
    public void Interior_uses_an_isolated_same_elevation_pocket_instead_of_a_sky_spawn()
    {
        var exterior = new SyndicateHqVector3(-43.7f, -4f, 156.4f);

        var pocket = SyndicateHqInteriorDefinition.GetPocketRoot(exterior);

        Assert.Equal(exterior.Y, pocket.Y);
        Assert.Equal(exterior.X + 1000f, pocket.X);
        Assert.Equal(exterior.Z + 1000f, pocket.Z);
        Assert.True(SyndicateHqInteriorDefinition.Markers.Single(marker => marker.Name == SyndicateHqContract.EntryMarker).Position.Y > 0f);
    }

    [Fact]
    public void Interior_primitives_define_only_the_visible_collidable_shell()
    {
        Assert.Contains(SyndicateHqInteriorDefinition.Primitives, item => item.Name == "Floor" && item.HasCollider && item.IsVisible);
        Assert.Contains(SyndicateHqInteriorDefinition.Primitives, item => item.Name == "SafetyFloor" && item.HasCollider && !item.IsVisible);
        Assert.Contains(SyndicateHqInteriorDefinition.Primitives, item =>
            item.Name == "DoorBarrier" && item.HasCollider && !item.IsVisible && item.IsRequiredCollider);
        var placeholderFurniture = new[]
        {
            "EntranceDoor", "ExitSign", "EntranceRug", "PlanningTable", "PlanningTableTop",
            "BossDesk", "BossChairSeat", "BossChairBack", "EnforcerTable", "FutureStorageBank",
            "CargoCrate_A", "CargoCrate_B", "BackWallMap"
        };
        Assert.DoesNotContain(SyndicateHqInteriorDefinition.Primitives, item => placeholderFurniture.Contains(item.Name, StringComparer.Ordinal));
        Assert.True(SyndicateHqInteriorDefinition.Lights.Count >= 3);
        Assert.All(SyndicateHqInteriorDefinition.RequiredColliderNames, name =>
            Assert.Contains(SyndicateHqInteriorDefinition.Primitives, item => item.Name == name && item.HasCollider));
    }

    [Fact]
    public void Interior_definition_exposes_a_native_visual_prop_catalog()
    {
        var nativeProps = SyndicateHqInteriorDefinition.NativeProps;

        Assert.True(SyndicateHqInteriorDefinition.IsValid(out var reason), reason);
        Assert.True(nativeProps.Count >= 12);
        Assert.Equal(nativeProps.Count, nativeProps.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(nativeProps, item =>
        {
            Assert.StartsWith("Map/Hyland Point/", item.SourceHierarchyPath, StringComparison.Ordinal);
            Assert.True(item.Position.IsFinite);
            Assert.True(float.IsFinite(item.PitchDegrees));
            Assert.True(float.IsFinite(item.YawDegrees));
            Assert.True(float.IsFinite(item.RollDegrees));
            Assert.True(item.UniformScale > 0f);
        });
        var entranceDoor = Assert.Single(nativeProps, item => item.Name == "EntranceDoorNative");
        Assert.Equal(
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/dockswarehouse/Walls/DoorFrameWall/Industrial Metal Door/Container/IndustrialMetalDoor",
            entranceDoor.SourceHierarchyPath);
        Assert.False(entranceDoor.KeepColliders);
        Assert.Equal(90f, entranceDoor.YawDegrees);
        Assert.True(entranceDoor.IncludeInactiveChildren);
        Assert.Contains(nativeProps, item => item.Name == "BossDeskNative" && item.SourceHierarchyPath.EndsWith("/Ornate Desk/ornate desk", StringComparison.Ordinal));
        Assert.Contains(nativeProps, item => item.Name == "WhiteboardNative" && !item.KeepColliders && item.Anchor == SyndicateHqNativePropAnchor.Center);
        Assert.Contains(nativeProps, item => item.Name == "OperationsRadioNative" && !item.KeepColliders);
        Assert.DoesNotContain(nativeProps, item => item.Name == "ShippingPalletRackNative");
        Assert.Contains(nativeProps, item => item.Name == "SafeNative" && !item.KeepColliders);
        Assert.Contains(nativeProps, item => item.Name == "WallCabinetNative" && !item.KeepColliders);
        Assert.Contains(nativeProps, item => item.Name == "WallClockNative" && !item.KeepColliders);
    }

    [Fact]
    public void Interior_dressing_forms_the_approved_command_briefing_lounge_and_staging_zones()
    {
        var props = SyndicateHqInteriorDefinition.NativeProps.ToDictionary(item => item.Name, StringComparer.Ordinal);

        var bossDesk = props["BossDeskNative"];
        var enforcerDesk = props["EnforcerDeskNative"];
        Assert.True(bossDesk.Position.Z >= 3f && bossDesk.Position.X < 0f);
        Assert.True(enforcerDesk.Position.Z >= 3f && enforcerDesk.Position.X < bossDesk.Position.X);
        Assert.InRange(bossDesk.Position.X - enforcerDesk.Position.X, 2f, 3.5f);

        foreach (var name in new[] { "BossWhiskyNative", "OperationsRadioNative" })
        {
            Assert.True(props[name].Position.Y >= 0.9f);
            Assert.True(props[name].Position.Z >= 2.8f);
            Assert.False(props[name].KeepColliders);
        }
        Assert.DoesNotContain(props.Values, item => item.Name == "BossComputerNative");

        var briefingChairs = props.Values
            .Where(item => item.Name.StartsWith("BriefingChairNative", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, briefingChairs.Length);
        Assert.All(briefingChairs, chair =>
        {
            Assert.EndsWith("/Interior/Outdoor chair", chair.SourceHierarchyPath, StringComparison.Ordinal);
            Assert.True(chair.KeepColliders);
            Assert.True(chair.Position.X < -2f);
            Assert.InRange(chair.Position.Z, -1f, 1.8f);
        });

        Assert.EndsWith("/Interior/Double Sofa", props["LoungeSofaNative"].SourceHierarchyPath, StringComparison.Ordinal);
        Assert.EndsWith("/Interior/Coffee Table", props["LoungeCoffeeTableNative"].SourceHierarchyPath, StringComparison.Ordinal);
        Assert.True(props["LoungeSofaNative"].Position.X < -4f && props["LoungeSofaNative"].Position.Z < -1.5f);
        Assert.True(props["LoungeCoffeeTableNative"].Position.X < -3f && props["LoungeCoffeeTableNative"].Position.Z < -1.5f);
        Assert.True(props["LoungeArmchairNative"].Position.X < -3f && props["LoungeArmchairNative"].Position.Z < -1.5f);

        var stagingCrates = props.Values
            .Where(item => item.Name.StartsWith("CargoCrateNative", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, stagingCrates.Length);
        Assert.All(stagingCrates, crate =>
        {
            Assert.InRange(crate.Position.X, 0.5f, 1.75f);
            Assert.True(crate.Position.Z < -2.5f);
        });

        Assert.DoesNotContain(props.Values, item =>
            item.Anchor == SyndicateHqNativePropAnchor.Floor &&
            item.Name != "EntranceDoorNative" &&
            item.Position.X >= 2f);
    }

    [Fact]
    public void Native_prop_rotation_supports_the_source_pitch_required_by_the_desk_and_clock()
    {
        var props = SyndicateHqInteriorDefinition.NativeProps.ToDictionary(item => item.Name, StringComparer.Ordinal);

        Assert.Equal(-90f, props["BossDeskNative"].PitchDegrees);
        Assert.Equal(-90f, props["WallClockNative"].PitchDegrees);
        Assert.Equal(90f, props["WallClockNative"].YawDegrees);
        Assert.Equal(0f, props["EntranceDoorNative"].PitchDegrees);
    }

    [Fact]
    public void Entrance_opening_matches_the_two_by_two_point_five_metre_vanilla_door_frame()
    {
        var primitives = SyndicateHqInteriorDefinition.Primitives.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var openingWidth = SyndicateHqInteriorDefinition.Width
            - primitives["Wall_South_Left"].Scale.X
            - primitives["Wall_South_Right"].Scale.X;
        var openingHeight = SyndicateHqInteriorDefinition.Height - primitives["Door_Header"].Scale.Y;

        Assert.Equal(2f, openingWidth, precision: 3);
        Assert.Equal(2.5f, openingHeight, precision: 3);
        Assert.Equal(new SyndicateHqVector3(2f, 2f, 0.25f), primitives["Door_Header"].Scale);
        Assert.True(primitives["DoorBarrier"].Scale.X < openingWidth);
        Assert.True(primitives["DoorBarrier"].Scale.Y <= openingHeight);
    }

    [Fact]
    public void Native_prop_placement_aligns_visual_bounds_to_the_authored_anchor()
    {
        var bounds = new SyndicateHqBoundsSnapshot(new(10, 2, 20), new(8, 1, 18));

        Assert.Equal(new SyndicateHqVector3(-10, -1, -20),
            SyndicateHqNativePropPlacement.AlignmentOffset(SyndicateHqVector3.Zero, bounds, SyndicateHqNativePropAnchor.Floor));
        Assert.Equal(new SyndicateHqVector3(-10, -2, -20),
            SyndicateHqNativePropPlacement.AlignmentOffset(SyndicateHqVector3.Zero, bounds, SyndicateHqNativePropAnchor.Center));
    }

    [Fact]
    public void Hq_prompt_layout_is_readable_at_ordinary_desktop_resolution()
    {
        Assert.True(SyndicateHqPromptLayout.Width >= 520f);
        Assert.True(SyndicateHqPromptLayout.Height >= 140f);
        Assert.True(SyndicateHqPromptLayout.ActionHeight >= 44f);
        Assert.True(SyndicateHqPromptLayout.BodyFontSize >= 20);
        Assert.True(SyndicateHqPromptLayout.ActionFontSize >= 18);
    }

    private static SyndicateHqDoorFingerprint Door() => new(
        SyndicateHqContract.NightclubDoorPath,
        "Il2CppScheduleOne.Doors.StaticDoor",
        SyndicateHqContract.NightclubDoorPath + "/IntObj",
        "Il2CppScheduleOne.Interaction.InteractableObject",
        SyndicateHqContract.NightclubBuildingPath,
        "Il2CppScheduleOne.Map.NPCEnterableBuilding",
        SyndicateHqContract.NightclubBuildingGuid,
        SyndicateHqContract.NightclubDoorPath + "/AccessPoint",
        0,
        new(-43.746002f, -4.000184f, 156.47406f),
        new(1, 0, 0), new(-43.346f, -4.000184f, 156.47406f), true, true, true, true, true);
}
