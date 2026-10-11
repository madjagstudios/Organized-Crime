using Il2CppFishNet;
using Il2CppFishNet.Managing.Server;
using OrganizedCrime.Core.Hosting;

namespace OrganizedCrime.Runtime.Platform;

/// <summary>
/// The production <see cref="IHostGate"/> over FishNet's server read, plus the
/// named call-site variants. The only production file that names
/// <c>OneServerStarted</c>; every former call site keeps its exact null
/// handling and throw mapping through the variant it uses.
/// </summary>
public sealed class FishNetHostGate : IHostGate
{
    public bool IsHost => IsHostNow();

    /// <summary>
    /// The Mod shape: host when a server manager exists and reports a
    /// started server, false on any fault. Reads FishNet's static once.
    /// Never throws.
    /// </summary>
    public static bool IsHostNow()
    {
        try
        {
            var serverManager = InstanceFinder.ServerManager;
            return serverManager is not null && serverManager.OneServerStarted();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The Il2Cpp-world shape: reads FishNet's static twice, exactly as the
    /// pre-refactor worlds did (null-check on the first read, started-read
    /// on the second), so a null or throwing second read maps to false.
    /// Never throws.
    /// </summary>
    public static bool IsHostNowDoubleRead() =>
        DoubleRead(static () => InstanceFinder.ServerManager, static manager => manager.OneServerStarted());

    /// <summary>
    /// The double-read evaluation behind <see cref="IsHostNowDoubleRead"/>.
    /// The reader runs twice and the started-read runs on the second value,
    /// so a null or throwing second read maps to false. Internal so tests
    /// pin the evaluation without the engine; production passes FishNet's
    /// static and the real started-read unchanged.
    /// </summary>
    internal static bool DoubleRead<TManager>(Func<TManager?> readManager, Func<TManager, bool> readStarted)
        where TManager : class
    {
        try
        {
            return readManager() is not null && readStarted(readManager()!);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Raw started read for call sites that already null-checked the manager
    /// and map throws themselves (LocalPressure, the HQ context, the storage
    /// patch). A null manager throws exactly like the direct call.
    /// </summary>
    public static bool IsStarted(ServerManager manager) => manager.OneServerStarted();

    /// <summary>
    /// Null-safe started read that still lets throws propagate, for call
    /// sites with their own fault mapping (the RuntimeProperty reads).
    /// </summary>
    public static bool IsStartedOrFalse(ServerManager? manager) =>
        manager is not null && manager.OneServerStarted();
}
