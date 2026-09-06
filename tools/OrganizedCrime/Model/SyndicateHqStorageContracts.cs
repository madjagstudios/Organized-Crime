using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

public enum SyndicateHqStorageStatus
{
    NotPrepared,
    Ready,
    NotCanonicalHost,
    SaveBindingChanged,
    GridUnavailable,
    IdentityConflict,
    SpawnUnavailable,
    ValidationFailed,
    Disposed
}

public sealed record SyndicateHqStorageDefinition(
    string Name,
    Guid Guid,
    string ItemId,
    int RequiredSlotCount,
    SyndicateHqVector3 LocalPosition,
    float YawDegrees,
    int GridOriginX,
    int GridOriginY);

public readonly record struct SyndicateHqStorageGridCoordinate(int X, int Y);

public enum SyndicateHqStorageGridShape
{
    Invalid,
    LegacyTwoCloset,
    Current
}

public static class SyndicateHqStorageGridContract
{
    public const int LegacyWidth = 12;
    public const int LegacyDepth = 14;
    public const int Width = 16;
    public const int Depth = 32;

    public static SyndicateHqStorageGridShape Classify(IEnumerable<SyndicateHqStorageGridCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        var captured = coordinates.ToArray();
        if (IsCompleteRectangle(captured, Width, Depth)) return SyndicateHqStorageGridShape.Current;
        if (IsCompleteRectangle(captured, LegacyWidth, LegacyDepth) &&
            captured.Contains(new(4, 3)) && captured.Contains(new(4, 9)))
            return SyndicateHqStorageGridShape.LegacyTwoCloset;
        return SyndicateHqStorageGridShape.Invalid;
    }

    private static bool IsCompleteRectangle(
        IReadOnlyCollection<SyndicateHqStorageGridCoordinate> coordinates,
        int width,
        int depth) =>
        coordinates.Count == width * depth &&
        coordinates.Distinct().Count() == coordinates.Count &&
        coordinates.All(item => item.X >= 0 && item.X < width && item.Y >= 0 && item.Y < depth);
}

public sealed record SyndicateHqStorageContext(
    bool IsCanonicalHost,
    string CanonicalPlayerId,
    string BoundSaveFolder,
    string ActiveSaveFolder);

public sealed record SyndicateHqStorageResult(
    SyndicateHqStorageStatus Status,
    string Reason)
{
    public bool IsReady => Status == SyndicateHqStorageStatus.Ready;
}

public static class SyndicateHqStorageContract
{
    public const string ItemId = "hugestoragecloset";
    public const int SlotCount = 20;

    public static IReadOnlyList<SyndicateHqStorageDefinition> Definitions { get; } = new[]
    {
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_A",
            Guid.Parse("8ec9d63b-f0f7-4af9-86fb-f1c73c7af481"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(5.2f, 0f, -1.8f),
            270f,
            4,
            3),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_B",
            Guid.Parse("dcf4ca2a-4d27-47c4-a10a-2819ea298fe3"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(5.2f, 0f, 1.8f),
            270f,
            4,
            9),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_C",
            Guid.Parse("7c4553ca-837a-4daa-8c0e-8c5021ab8b3c"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(5.2f, 0f, -3.6f),
            270f,
            4,
            15),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_D",
            Guid.Parse("6a47a1fa-cc74-4eb6-8cd1-73c3ecf5a28f"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(5.2f, 0f, 0f),
            270f,
            4,
            21),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_E",
            Guid.Parse("77853faf-16c9-483a-a4b6-0c12cb85ad5b"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(5.2f, 0f, 3.6f),
            270f,
            4,
            27),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_F",
            Guid.Parse("6d19eda6-72fe-4e3c-ae68-8bd7f205713b"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(2.4f, 0f, -3.6f),
            90f,
            10,
            3),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_G",
            Guid.Parse("a92fc0f8-19fa-445c-9ef1-5af74904094d"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(2.4f, 0f, -1.8f),
            90f,
            10,
            9),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_I",
            Guid.Parse("bb63e787-4c46-4627-8f20-e2429f13a574"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(2.4f, 0f, 1.8f),
            90f,
            10,
            21),
        new SyndicateHqStorageDefinition(
            "HQ_PlayerLocker_J",
            Guid.Parse("11164773-6a3d-4b11-862a-9ac447626ec1"),
            ItemId,
            SlotCount,
            new SyndicateHqVector3(2.4f, 0f, 3.6f),
            90f,
            10,
            27)
    };

    public static bool IsValid(out string reason)
    {
        if (Definitions.Count != 9)
        {
            reason = "HQ storage must define exactly nine lockers.";
            return false;
        }

        if (Definitions.Select(definition => definition.Name).Distinct(StringComparer.Ordinal).Count() != Definitions.Count ||
            Definitions.Select(definition => definition.Guid).Distinct().Count() != Definitions.Count)
        {
            reason = "HQ storage names and GUIDs must be distinct.";
            return false;
        }

        if (Definitions.Any(definition =>
                string.IsNullOrWhiteSpace(definition.Name) ||
                definition.Guid == Guid.Empty ||
                !string.Equals(definition.ItemId, ItemId, StringComparison.Ordinal) ||
                definition.RequiredSlotCount != SlotCount ||
                !definition.LocalPosition.IsFinite ||
                !float.IsFinite(definition.YawDegrees) ||
                definition.GridOriginX < 0 || definition.GridOriginX >= SyndicateHqStorageGridContract.Width ||
                definition.GridOriginY < 0 || definition.GridOriginY >= SyndicateHqStorageGridContract.Depth))
        {
            reason = "HQ storage definition values were invalid.";
            return false;
        }

        if (Definitions.Select(definition => new SyndicateHqStorageGridCoordinate(definition.GridOriginX, definition.GridOriginY))
            .Distinct().Count() != Definitions.Count)
        {
            reason = "HQ storage native Grid origins must be distinct.";
            return false;
        }

        reason = "Nine distinct native 20-slot Huge Storage Closets are defined (180 total slots).";
        return true;
    }
}

public static class SyndicateHqStorageCullingGuard
{
    private static readonly object Sync = new();
    private static readonly HashSet<Guid> VisibleClosets = new();

    public static void SetVisible(Guid guid, bool visible)
    {
        if (!SyndicateHqStorageContract.Definitions.Any(definition => definition.Guid == guid)) return;
        lock (Sync)
        {
            if (visible) VisibleClosets.Add(guid);
            else VisibleClosets.Remove(guid);
        }
    }

    public static bool Filter(Guid guid, bool requestedCulled)
    {
        if (!requestedCulled) return false;
        lock (Sync) return !VisibleClosets.Contains(guid);
    }

    public static void ApplyVisibility(
        bool enabled,
        Action<bool> setGuardVisible,
        Action requestUncull,
        Action<bool> setActive)
    {
        setGuardVisible(enabled);
        if (enabled) requestUncull();
        setActive(enabled);
    }

    public static void Reset()
    {
        lock (Sync) VisibleClosets.Clear();
    }
}

public static class SyndicateHqStorageAdmission
{
    public static SyndicateHqStorageResult Evaluate(SyndicateHqStorageContext context)
    {
        if (!context.IsCanonicalHost ||
            !LocalPressureHostLifecyclePolicies.IsCanonicalPlayerIdentity(context.CanonicalPlayerId))
        {
            return new(SyndicateHqStorageStatus.NotCanonicalHost, "Canonical single-player host authority was unavailable.");
        }

        if (!TryReadSaveIdentity(context.BoundSaveFolder, out var boundIdentity) ||
            !TryReadSaveIdentity(context.ActiveSaveFolder, out var activeIdentity) ||
            !string.Equals(boundIdentity, context.CanonicalPlayerId, StringComparison.Ordinal) ||
            !string.Equals(activeIdentity, context.CanonicalPlayerId, StringComparison.Ordinal) ||
            !TryNormalizePath(context.BoundSaveFolder, out var boundPath) ||
            !TryNormalizePath(context.ActiveSaveFolder, out var activePath) ||
            !string.Equals(boundPath, activePath, StringComparison.OrdinalIgnoreCase))
        {
            return new(SyndicateHqStorageStatus.SaveBindingChanged, "The active save binding did not match the canonical bound save.");
        }

        return new(SyndicateHqStorageStatus.Ready, "Canonical host and save binding admit native HQ storage preparation.");
    }

    private static bool TryReadSaveIdentity(string path, out string identity)
    {
        identity = string.Empty;
        if (!LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(path, out var parsed) ||
            string.IsNullOrWhiteSpace(parsed))
            return false;
        identity = parsed;
        return true;
    }

    private static bool TryNormalizePath(string path, out string normalized)
    {
        normalized = string.Empty;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
