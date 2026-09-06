namespace OrganizedCrime.Model;

public enum SyndicateHqMaterialRole
{
    Floor,
    Brick,
    Ceiling,
    Wood,
    Metal,
    Accent
}

public sealed record SyndicateHqMarkerDefinition(string Name, SyndicateHqVector3 Position, bool IsInert);

public sealed record SyndicateHqPrimitiveDefinition(
    string Name,
    SyndicateHqVector3 Position,
    SyndicateHqVector3 Scale,
    SyndicateHqMaterialRole MaterialRole,
    bool HasCollider,
    bool IsVisible,
    bool IsRequiredCollider = false);

public enum SyndicateHqNativePropAnchor
{
    Floor,
    Center
}

public sealed record SyndicateHqNativePropDefinition(
    string Name,
    string SourceHierarchyPath,
    SyndicateHqVector3 Position,
    float YawDegrees,
    float UniformScale,
    SyndicateHqNativePropAnchor Anchor,
    bool KeepColliders,
    bool IsRequired = true,
    bool IncludeInactiveChildren = false,
    float PitchDegrees = 0f,
    float RollDegrees = 0f);

public readonly record struct SyndicateHqBoundsSnapshot(
    SyndicateHqVector3 Center,
    SyndicateHqVector3 Minimum);

public static class SyndicateHqNativePropPlacement
{
    public static SyndicateHqVector3 AlignmentOffset(
        SyndicateHqVector3 target,
        SyndicateHqBoundsSnapshot bounds,
        SyndicateHqNativePropAnchor anchor)
    {
        var sourceAnchor = anchor == SyndicateHqNativePropAnchor.Floor
            ? new SyndicateHqVector3(bounds.Center.X, bounds.Minimum.Y, bounds.Center.Z)
            : bounds.Center;
        return new(
            target.X - sourceAnchor.X,
            target.Y - sourceAnchor.Y,
            target.Z - sourceAnchor.Z);
    }
}

public sealed record SyndicateHqLightDefinition(
    string Name,
    SyndicateHqVector3 Position,
    SyndicateHqColor Color,
    float Intensity,
    float Range);

public readonly record struct SyndicateHqColor(float R, float G, float B);

public static class SyndicateHqPromptLayout
{
    public const float Width = 560f;
    public const float Height = 156f;
    public const float ActionHeight = 48f;
    public const int BodyFontSize = 22;
    public const int ActionFontSize = 20;
}

public static class SyndicateHqInteriorDefinition
{
    public const float Width = 14f;
    public const float Depth = 10f;
    public const float Height = 4.5f;
    public const float PocketOffset = 1000f;

    public static IReadOnlyList<SyndicateHqMarkerDefinition> Markers { get; } = new[]
    {
        new SyndicateHqMarkerDefinition(SyndicateHqContract.EntryMarker, new SyndicateHqVector3(0, 1, -3.0f), false),
        new SyndicateHqMarkerDefinition(SyndicateHqContract.ExitMarker, new SyndicateHqVector3(0, 1, -4.5f), false),
        new SyndicateHqMarkerDefinition(SyndicateHqContract.StorageMarker, new SyndicateHqVector3(5.4f, 1, 1.4f), true)
    };

    public static IReadOnlyList<SyndicateHqPrimitiveDefinition> Primitives { get; } = new[]
    {
        new SyndicateHqPrimitiveDefinition("Floor", new(0, -0.2f, 0), new(Width, 0.4f, Depth), SyndicateHqMaterialRole.Floor, true, true, true),
        new SyndicateHqPrimitiveDefinition("SafetyFloor", new(0, -1.7f, 0), new(Width + 2f, 3f, Depth + 2f), SyndicateHqMaterialRole.Floor, true, false, true),
        new SyndicateHqPrimitiveDefinition("Ceiling", new(0, Height, 0), new(Width, 0.2f, Depth), SyndicateHqMaterialRole.Ceiling, true, true, true),
        new SyndicateHqPrimitiveDefinition("Wall_North", new(0, Height / 2f, Depth / 2f), new(Width, Height, 0.25f), SyndicateHqMaterialRole.Brick, true, true, true),
        new SyndicateHqPrimitiveDefinition("Wall_West", new(-Width / 2f, Height / 2f, 0), new(0.25f, Height, Depth), SyndicateHqMaterialRole.Brick, true, true, true),
        new SyndicateHqPrimitiveDefinition("Wall_East", new(Width / 2f, Height / 2f, 0), new(0.25f, Height, Depth), SyndicateHqMaterialRole.Brick, true, true, true),
        new SyndicateHqPrimitiveDefinition("Wall_South_Left", new(-4f, Height / 2f, -Depth / 2f), new(6f, Height, 0.25f), SyndicateHqMaterialRole.Brick, true, true, true),
        new SyndicateHqPrimitiveDefinition("Wall_South_Right", new(4f, Height / 2f, -Depth / 2f), new(6f, Height, 0.25f), SyndicateHqMaterialRole.Brick, true, true, true),
        new SyndicateHqPrimitiveDefinition("Door_Header", new(0, 3.5f, -Depth / 2f), new(2f, 2f, 0.25f), SyndicateHqMaterialRole.Brick, true, true, true),
        new SyndicateHqPrimitiveDefinition("DoorBarrier", new(0, 1.25f, -4.86f), new(1.8f, 2.5f, 0.18f), SyndicateHqMaterialRole.Metal, true, false, true)
    };

    public static IReadOnlyList<SyndicateHqNativePropDefinition> NativeProps { get; } = new[]
    {
        new SyndicateHqNativePropDefinition(
            "EntranceDoorNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/dockswarehouse/Walls/DoorFrameWall/Industrial Metal Door/Container/IndustrialMetalDoor",
            new(0, 0, -4.86f), 90f, 1f, SyndicateHqNativePropAnchor.Floor, false, true, true),
        new SyndicateHqNativePropDefinition(
            "PlanningTableNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Office_Table",
            new(-3.6f, 0, 0.45f), 90f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "BriefingChairNativeA",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Outdoor chair",
            new(-4.9f, 0, 0.45f), 90f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "BriefingChairNativeB",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Outdoor chair",
            new(-2.3f, 0, 0.45f), 270f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "BriefingChairNativeC",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Outdoor chair",
            new(-3.6f, 0, -0.75f), 0f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "BriefingChairNativeD",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Outdoor chair",
            new(-3.6f, 0, 1.65f), 180f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "BossDeskNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/InteriorDisable/Mezzanine/Ornate Desk/ornate desk",
            new(-1.4f, 0, 3.15f), 180f, 1f, SyndicateHqNativePropAnchor.Floor, true, PitchDegrees: -90f),
        new SyndicateHqNativePropDefinition(
            "BossArmchairNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Armchair",
            new(-1.4f, 0, 4.15f), 180f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "BossWhiskyNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/InteriorDisable/Mezzanine/Ornate Desk/Whisky Bottle",
            new(-2.0f, 1.05f, 3.15f), 20f, 1f, SyndicateHqNativePropAnchor.Center, false),
        new SyndicateHqNativePropDefinition(
            "EnforcerDeskNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Frontdesk",
            new(-4.1f, 0, 3.15f), 180f, 0.65f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "EnforcerArmchairNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Armchair (1)",
            new(-4.1f, 0, 4.05f), 180f, 0.9f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "FilingCabinetNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/InteriorDisable/Mezzanine/docks mezzanine/Filing Cabinet",
            new(-6.0f, 0, -1.25f), 90f, 1f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "LoungeSofaNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Double Sofa",
            new(-5.7f, 0, -2.8f), 90f, 0.9f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "LoungeCoffeeTableNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Coffee Table",
            new(-4.45f, 0, -2.65f), 0f, 0.8f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "LoungeArmchairNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Armchair",
            new(-3.35f, 0, -3.35f), 0f, 0.85f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "CargoCrateNativeA",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Wood Crate Prop",
            new(0.75f, 0, -3.25f), 15f, 0.9f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "CargoCrateNativeB",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Interior/Wood Crate Prop",
            new(1.5f, 0, -2.85f), 345f, 0.7f, SyndicateHqNativePropAnchor.Floor, true),
        new SyndicateHqNativePropDefinition(
            "WhiteboardNative",
            "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/Whiteboard/whiteboard",
            new(-6.82f, 2.35f, 0.6f), 90f, 1f, SyndicateHqNativePropAnchor.Center, false),
        new SyndicateHqNativePropDefinition(
            "OperationsRadioNative",
            "Map/Hyland Point/Region_Docks/Clothing store with interior/Interior/SM_Item_Radio_01",
            new(-4.0f, 1.05f, 3.15f), 90f, 0.8f, SyndicateHqNativePropAnchor.Center, false, false),
        new SyndicateHqNativePropDefinition(
            "SafeNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Safe",
            new(-6.05f, 0, 3.65f), 90f, 0.9f, SyndicateHqNativePropAnchor.Floor, false, false),
        new SyndicateHqNativePropDefinition(
            "WallCabinetNative",
            "Map/Hyland Point/Region_Northtown/Motel/Office_Complete/Office Props/Office_Wallcabinet",
            new(-6.82f, 2.25f, 2.15f), 90f, 0.85f, SyndicateHqNativePropAnchor.Center, false, false),
        new SyndicateHqNativePropDefinition(
            "WallClockNative",
            "Map/Hyland Point/Region_Docks/Clothing store with interior/Interior/Wall Clock/Wall clock/Clock",
            new(0, 3.2f, 4.78f), 90f, 0.9f, SyndicateHqNativePropAnchor.Center, false, false, PitchDegrees: -90f),
        new SyndicateHqNativePropDefinition(
            "WallLanternNativeLeft",
            "Map/Hyland Point/Region_Downtown/TownCenter/Bank/WallLantern/walllantern",
            new(-4.8f, 2.75f, 4.78f), 180f, 1f, SyndicateHqNativePropAnchor.Center, false, false),
        new SyndicateHqNativePropDefinition(
            "WallLanternNativeRight",
            "Map/Hyland Point/Region_Downtown/TownCenter/Bank/WallLantern/walllantern",
            new(4.8f, 2.75f, 4.78f), 180f, 1f, SyndicateHqNativePropAnchor.Center, false, false)
    };

    public static IReadOnlyList<SyndicateHqLightDefinition> Lights { get; } = new[]
    {
        new SyndicateHqLightDefinition("EntryLight", new(0, 3.65f, -3.3f), new(1.0f, 0.58f, 0.27f), 1.65f, 7f),
        new SyndicateHqLightDefinition("PlanningLight", new(-3.6f, 3.75f, 0.45f), new(1.0f, 0.68f, 0.38f), 2.15f, 8f),
        new SyndicateHqLightDefinition("BossLight", new(0, 3.55f, 3.15f), new(1.0f, 0.50f, 0.20f), 1.55f, 6f),
        new SyndicateHqLightDefinition("StorageLight", new(5.2f, 3.25f, 0.5f), new(0.72f, 0.80f, 1.0f), 1.15f, 5f)
    };

    public static IReadOnlyList<string> RequiredColliderNames { get; } =
        Primitives.Where(item => item.IsRequiredCollider).Select(item => item.Name).ToArray();

    public static SyndicateHqVector3 GetPocketRoot(SyndicateHqVector3 exteriorAnchor) =>
        exteriorAnchor + new SyndicateHqVector3(PocketOffset, 0, PocketOffset);

    public static bool IsValid(out string reason)
    {
        var markerNames = Markers.Select(marker => marker.Name).ToArray();
        if (markerNames.Distinct(StringComparer.Ordinal).Count() != markerNames.Length) { reason = "HQ marker names were duplicated."; return false; }
        if (Markers.Count(marker => marker.Name == SyndicateHqContract.EntryMarker) != 1 || Markers.Count(marker => marker.Name == SyndicateHqContract.ExitMarker) != 1) { reason = "HQ entry/exit markers were incomplete."; return false; }
        if (Markers.Single(marker => marker.Name == SyndicateHqContract.StorageMarker).IsInert == false) { reason = "HQ storage marker was not inert."; return false; }
        if (Markers.Any(marker => !marker.Position.IsFinite)) { reason = "HQ marker position was not finite."; return false; }

        var primitiveNames = Primitives.Select(item => item.Name).ToArray();
        if (primitiveNames.Distinct(StringComparer.Ordinal).Count() != primitiveNames.Length) { reason = "HQ primitive names were duplicated."; return false; }
        if (Primitives.Any(item => !item.Position.IsFinite || !item.Scale.IsFinite || item.Scale.X <= 0 || item.Scale.Y <= 0 || item.Scale.Z <= 0)) { reason = "HQ primitive geometry was invalid."; return false; }
        if (RequiredColliderNames.Count == 0 || RequiredColliderNames.Any(name => !Primitives.Any(item => item.Name == name && item.HasCollider))) { reason = "HQ required collider shell was incomplete."; return false; }

        var nativePropNames = NativeProps.Select(item => item.Name).ToArray();
        if (nativePropNames.Distinct(StringComparer.Ordinal).Count() != nativePropNames.Length) { reason = "HQ native prop names were duplicated."; return false; }
        if (NativeProps.Count(item => item.IsRequired) < 10 || NativeProps.Any(item =>
                string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.SourceHierarchyPath) ||
                !item.SourceHierarchyPath.StartsWith("Map/Hyland Point/", StringComparison.Ordinal) ||
                !item.Position.IsFinite ||
                !float.IsFinite(item.PitchDegrees) ||
                !float.IsFinite(item.YawDegrees) ||
                !float.IsFinite(item.RollDegrees) ||
                !float.IsFinite(item.UniformScale) || item.UniformScale <= 0))
        { reason = "HQ native prop catalog was invalid."; return false; }

        var lightNames = Lights.Select(light => light.Name).ToArray();
        if (lightNames.Distinct(StringComparer.Ordinal).Count() != lightNames.Length || Lights.Any(light => !light.Position.IsFinite || light.Intensity <= 0 || light.Range <= 0)) { reason = "HQ lighting definition was invalid."; return false; }
        reason = "Deterministic visible HQ shell and inert native prop catalog are valid.";
        return true;
    }
}
