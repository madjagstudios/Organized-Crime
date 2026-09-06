using Il2CppFishNet;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class SyndicateHqHostContextAdapter : IRelease1StoryHostContext
{
    private readonly Guid _sessionEpoch = Guid.NewGuid();
    private long _loadEpoch;

    public void BeginLoad() => _loadEpoch++;

    public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            var server = InstanceFinder.ServerManager;
            var client = InstanceFinder.ClientManager;
            if (server is null || client is null) return Release1StoryHostContextReadStatus.Pending;
            if (!server.OneServerStarted()) return Release1StoryHostContextReadStatus.NotAuthoritative;
            if (!client.Started) return Release1StoryHostContextReadStatus.Pending;
            var folder = LoadManager.Instance?.LoadedGameFolderPath;
            if (!LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(folder, out var identity)) return Release1StoryHostContextReadStatus.Pending;

            var players = new List<Player>();
            var registry = Player.PlayerList;
            if (registry is null) return Release1StoryHostContextReadStatus.Pending;
            foreach (var player in registry)
                if (player is not null && player.IsServerInitialized) players.Add(player);
            if (players.Count == 0) return Release1StoryHostContextReadStatus.Pending;
            if (players.Count > 1) return Release1StoryHostContextReadStatus.UnsupportedMultiplayer;
            var playerId = players[0].PlayerCode?.Trim();
            if (players[0].Connection is null || LocalPressureHostLifecyclePolicies.IsPlaceholderPlayerCode(playerId)) return Release1StoryHostContextReadStatus.Pending;
            if (!string.Equals(identity, playerId, StringComparison.Ordinal)) return Release1StoryHostContextReadStatus.AmbiguousIdentity;
            snapshot = new(_sessionEpoch, _loadEpoch, identity!, folder!);
            return Release1StoryHostContextReadStatus.Ready;
        }
        catch { snapshot = default; return Release1StoryHostContextReadStatus.Faulted; }
    }
}
