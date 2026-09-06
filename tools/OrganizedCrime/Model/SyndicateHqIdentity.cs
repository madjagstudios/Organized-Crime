namespace OrganizedCrime.Model;

public static class SyndicateHqIdentity
{
    public static SyndicateHqIdentityCheckResult Validate(SyndicateHqSceneSnapshot? snapshot)
    {
        if (snapshot is null)
            return SyndicateHqIdentityCheckResult.Reject("Scene snapshot was unavailable.");

        var shellMatches = snapshot.ShellPaths.Count(path => string.Equals(path, SyndicateHqContract.NightclubShellPath, StringComparison.Ordinal));
        if (shellMatches != 1)
            return SyndicateHqIdentityCheckResult.Reject(shellMatches == 0 ? "Nightclub shell was missing." : "Nightclub shell was duplicated.");

        var doors = snapshot.Doors.Where(door => string.Equals(door.DoorPath, SyndicateHqContract.NightclubDoorPath, StringComparison.Ordinal)).ToArray();
        if (doors.Length != 1)
            return SyndicateHqIdentityCheckResult.Reject(doors.Length == 0 ? "Validated Nightclub door was missing." : "Validated Nightclub door was duplicated.");

        var door = doors[0];
        if (!door.Position.IsFinite || door.AccessPointPosition is null || !door.AccessPointPosition.Value.IsFinite)
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door position data was non-finite.");
        if (!string.Equals(door.DoorType, "Il2CppScheduleOne.Doors.StaticDoor", StringComparison.Ordinal))
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door runtime type changed.");
        if (!string.Equals(door.InteractablePath, SyndicateHqContract.NightclubDoorPath + "/IntObj", StringComparison.Ordinal) ||
            !string.Equals(door.InteractableType, "Il2CppScheduleOne.Interaction.InteractableObject", StringComparison.Ordinal))
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door interaction target changed.");
        if (!string.Equals(door.BuildingPath, SyndicateHqContract.NightclubBuildingPath, StringComparison.Ordinal) ||
            !string.Equals(door.BuildingType, "Il2CppScheduleOne.Map.NPCEnterableBuilding", StringComparison.Ordinal) ||
            !string.Equals(door.BuildingGuid, SyndicateHqContract.NightclubBuildingGuid, StringComparison.Ordinal))
            return SyndicateHqIdentityCheckResult.Reject("Nightclub building identity changed.");
        if (!string.Equals(door.AccessPointPath, SyndicateHqContract.NightclubDoorPath + "/AccessPoint", StringComparison.Ordinal))
            return SyndicateHqIdentityCheckResult.Reject("Nightclub access point changed.");
        if (door.DoorIndex != SyndicateHqContract.NightclubDoorIndex)
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door index changed.");
        if (!door.HasIntObj || !door.HasBuilding || !door.HasAccessPoint || !door.HasCanKnock || !door.HasUsable)
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door fingerprint fields were incomplete.");

        var expectedPosition = new SyndicateHqVector3(-43.746002f, -4.000184f, 156.47406f);
        var expectedForward = new SyndicateHqVector3(1f, 0f, 0f);
        if (door.Position.DistanceTo(expectedPosition) > SyndicateHqContract.PositionTolerance)
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door position moved beyond tolerance.");
        if (!door.Forward.IsNonZero)
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door forward vector was zero or non-finite.");
        var dot = door.Forward.NormalizedDot(expectedForward);
        if (dot < SyndicateHqContract.ForwardDotTolerance)
            return SyndicateHqIdentityCheckResult.Reject("Nightclub door orientation changed.");

        return SyndicateHqIdentityCheckResult.Accept(door);
    }
}
