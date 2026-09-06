namespace OrganizedCrime.Model;

public sealed record Release1MissionDefinition(
    int Sequence,
    int Chapter,
    string MissionKey,
    string Title,
    string Archetype);

public static class Release1MissionCatalog
{
    public const string IntroScopeKey = "release1.intro";
    public const string SmallCourtesy = "release1.small-courtesy";
    public const string WrongAddress = "release1.wrong-address";
    public const string RoomWithNoName = "release1.room-with-no-name";
    public const string ShortNotice = "release1.short-notice";
    public const string KeepTheLightsOff = "release1.keep-the-lights-off";
    public const string TheEnvelope = "release1.the-envelope";

    /// <summary>
    /// OC-73. Chief Campbell's scope key. Deliberately absent from <see cref="All"/>: he is not a
    /// mission, so the mission ladder, unlock order, Standing table and quest projection never see him.
    /// Only correlations, the native effect journal and the revert tolerant rule accept it.
    /// </summary>
    public const string ChiefCampbell = "release1.chief-campbell";

    public static IReadOnlyList<Release1MissionDefinition> All { get; } =
        new[]
        {
            new Release1MissionDefinition(1, 1, SmallCourtesy, "Small Courtesy", "DeadDrop"),
            new Release1MissionDefinition(2, 1, WrongAddress, "Wrong Address", "PackageRecovery"),
            new Release1MissionDefinition(3, 1, RoomWithNoName, "A Room With No Name", "TemporaryStorageSafehouse"),
            new Release1MissionDefinition(4, 2, ShortNotice, "Short Notice", "EmergencySupply"),
            new Release1MissionDefinition(5, 2, KeepTheLightsOff, "Keep the Lights Off", "OperationalShutdown"),
            new Release1MissionDefinition(6, 2, TheEnvelope, "The Envelope", "CashMovement")
        };

    public static bool IsMissionKey(string? key) =>
        key is not null && All.Any(mission => string.Equals(mission.MissionKey, key, StringComparison.Ordinal));

    /// <summary>
    /// Keys a native effect journal entry may carry: the six missions, plus Chief Campbell's scope
    /// key. Replaces the <see cref="IsMissionKey"/> guard inside
    /// <c>Release1NativeEffectJournalEntry.Validate</c> and nowhere else.
    /// </summary>
    public static bool IsEffectScopeKey(string? key) =>
        IsMissionKey(key) || string.Equals(key, ChiefCampbell, StringComparison.Ordinal);

    public static int IndexOf(string key) =>
        All.ToList().FindIndex(mission => string.Equals(mission.MissionKey, key, StringComparison.Ordinal));

    /// <summary>
    /// The missions whose native effects may leave the Prepared phase before the sidecar has
    /// persisted them, because their whole native footprint reverts with the same save the sidecar
    /// does. Small Courtesy stays save gated.
    /// </summary>
    public static bool AllowsRevertTolerantEffects(string? missionKey) =>
        string.Equals(missionKey, WrongAddress, StringComparison.Ordinal) ||
        string.Equals(missionKey, RoomWithNoName, StringComparison.Ordinal) ||
        string.Equals(missionKey, ShortNotice, StringComparison.Ordinal) ||
        string.Equals(missionKey, KeepTheLightsOff, StringComparison.Ordinal) ||
        string.Equals(missionKey, TheEnvelope, StringComparison.Ordinal) ||
        // OC-73 decision 4. The Chief's whole native footprint is a wallet balance, which reverts
        // with the same save the sidecar does, exactly like the five missions above.
        string.Equals(missionKey, ChiefCampbell, StringComparison.Ordinal);
}
