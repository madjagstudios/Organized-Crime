namespace OrganizedCrime.Runtime;

public static class LocalPressureHostLifecyclePolicies
{
    public static bool IsAuthoritativeHost(bool isServer, bool isLocalClient) =>
        isServer && isLocalClient;

    public static bool ShouldForwardAwakeHour(bool sleepInProgress) =>
        !sleepInProgress;

    public static bool ShouldForwardDayPass() => false;

    public static bool TryGetCanonicalHostIdentity(
        string? activeSaveFolder,
        out string? identity) => LocalPressureHostIdentity.TryGetCanonicalHostIdentity(activeSaveFolder, out identity);

    public static LocalPressurePlayerReadStatus ClassifyPlayerRegistrySnapshot(
        IReadOnlyList<LocalPressurePlayerSample>? registryPlayers,
        out IReadOnlyList<LocalPressurePlayerSample> supportedPlayers)
    {
        supportedPlayers = Array.Empty<LocalPressurePlayerSample>();
        if (registryPlayers is null)
            return LocalPressurePlayerReadStatus.Pending;

        var serverPlayers = registryPlayers
            .Where(player => player.IsHostOwned)
            .ToArray();
        if (serverPlayers.Length == 0)
            return LocalPressurePlayerReadStatus.Pending;

        supportedPlayers = serverPlayers;
        if (serverPlayers.Length > 1)
            return LocalPressurePlayerReadStatus.UnsupportedMultiplayer;

        var player = serverPlayers[0];
        if (!player.HasConnection || IsPlaceholderPlayerCode(player.PlayerCode))
            return LocalPressurePlayerReadStatus.Pending;

        return LocalPressurePlayerReadStatus.Ready;
    }

    public static bool IsPlaceholderPlayerCode(string? playerCode) => LocalPressureHostIdentity.IsPlaceholderPlayerCode(playerCode);

    public static bool IsCanonicalPlayerIdentity(string? identity) => LocalPressureHostIdentity.IsCanonicalPlayerIdentity(identity);

    public static bool TryAcceptSleepEnd(long totalGameMinutes, ref long? lastForwardedTotalGameMinutes)
    {
        if (lastForwardedTotalGameMinutes == totalGameMinutes)
            return false;

        lastForwardedTotalGameMinutes = totalGameMinutes;
        return true;
    }
}

public sealed class LocalPressureHostLifecycleBindingState
{
    private object? _boundInstance;

    public bool IsBoundTo(object instance) =>
        ReferenceEquals(_boundInstance, instance);

    public void MarkBound(object instance) =>
        _boundInstance = instance ?? throw new ArgumentNullException(nameof(instance));

    public void Clear() => _boundInstance = null;
}
