using Il2CppFishNet;
using Il2CppScheduleOne.PlayerScripts;

namespace OrganizedCrime.PhoneCallProof;

public static class PhoneProofHostGate
{
    public static PhoneCallProofContext ReadContext()
    {
        try
        {
            var server = InstanceFinder.ServerManager;
            var client = InstanceFinder.ClientManager;
            if (server is null || client is null || !server.OneServerStarted() || !client.Started)
                return new(false);

            var hostPlayers = 0;
            foreach (var player in Player.PlayerList ?? new Il2CppSystem.Collections.Generic.List<Player>())
            {
                if (player is not null && player.IsServerInitialized)
                    hostPlayers++;
            }

            return new(hostPlayers == 1);
        }
        catch
        {
            return new(false);
        }
    }
}
