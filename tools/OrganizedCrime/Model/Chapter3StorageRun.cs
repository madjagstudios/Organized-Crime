namespace OrganizedCrime.Model;

// OC-165: the quoted route and durable progress belong to Chapter 3 alone.
internal sealed record Chapter3StorageRun(
    Chapter3PackageRecovery Route,
    double HoldMinutes,
    double? StowedAt = null,
    string? ClosetGuid = null,
    double? MissingSince = null,
    bool Released = false,
    int? PendingSlot = null,
    int? PendingPreQuantity = null,
    double? CreditBefore = null)
{
    internal bool Reconciling => Route.RemovalConfirmed || PendingSlot.HasValue || CreditBefore.HasValue;
    internal static double HoldForReward(int reward) =>
        Chapter3JobTuning.BricksForReward(reward) == 2 ? 720d : 1440d;

    internal void Validate(int reward, bool offered)
    {
        ArgumentNullException.ThrowIfNull(Route);
        Route.Validate();
        if (reward < 15_000 || reward > 20_000 || reward % 500 != 0 ||
            Route.BrickCount != Chapter3JobTuning.BricksForReward(reward) || HoldMinutes != HoldForReward(reward))
            throw new ArgumentException("Storage run terms were invalid.");
        foreach (var time in new[] { StowedAt, MissingSince, CreditBefore })
            if (time.HasValue && (!double.IsFinite(time.Value) || time.Value < 0))
                throw new ArgumentException("Storage run progress was invalid.");
        if (StowedAt.HasValue != (ClosetGuid is not null) ||
            (StowedAt.HasValue && !Route.Custody) || (Released && !StowedAt.HasValue) ||
            (MissingSince.HasValue && (!StowedAt.HasValue || MissingSince < StowedAt || Released)) ||
            (Route.RemovalConfirmed && (!Released || PendingSlot.HasValue)) ||
            PendingSlot.HasValue != PendingPreQuantity.HasValue ||
            (PendingSlot.HasValue && (!Released || PendingSlot.Value < 0 || PendingPreQuantity != Route.BrickCount)) ||
            (CreditBefore.HasValue && !Route.RemovalConfirmed))
            throw new ArgumentException("Storage run stage was inconsistent.");
        if (ClosetGuid is not null && !PlayerClosetIds.Contains(ClosetGuid))
            throw new ArgumentException("Storage run closet was outside the player room.");
        if (offered && (Route.Staged || Route.StagedSlotIndex != -1 || Route.Custody || StowedAt.HasValue || Released || Reconciling))
            throw new ArgumentException("Storage offer already carried progress.");
        if (!offered && (!Route.Staged || Route.StagedSlotIndex < 0))
            throw new ArgumentException("Storage assignment was not staged.");
    }

    internal static readonly IReadOnlySet<string> PlayerClosetIds = SyndicateHqStorageContract.Definitions
        .Where(definition => definition.Name != SyndicateHqStorageContract.FamilyLockerDefinitionName)
        .Select(definition => definition.Guid.ToString()).ToHashSet(StringComparer.Ordinal);

    internal static Release1HoldObservation Observe(
        Release1HoldRoomSnapshot? room, Chapter3StorageRun run, out string? closet)
    {
        closet = null;
        if (room is null || room.Readiness != Release1HoldRoomReadiness.Ready)
            return Release1HoldObservation.RoomNotReady;
        try { room.Validate(); }
        catch (ArgumentException) { return Release1HoldObservation.RoomNotReady; }
        var playerClosets = room.Closets.Where(item => PlayerClosetIds.Contains(item.ClosetGuid)).ToArray();
        if (playerClosets.Length != PlayerClosetIds.Count)
            return Release1HoldObservation.RoomNotReady;
        return Release1HoldRoomClassifier.Classify(
            new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, playerClosets),
            new Release1ConsignmentFingerprint(run.Route.ProductId, run.Route.PackagingId, run.Route.BrickCount),
            PlayerClosetIds.Count, out closet);
    }
}
