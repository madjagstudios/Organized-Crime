using S1API.Entities;
using S1API.Law;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74: the game reads behind <see cref="IChapter3PlayerExposureReader"/>. On foot is
/// <c>Player.IsInVehicle</c>, outdoors is a null <c>Player.CurrentProperty</c>, and the
/// pursuit level is <c>CrimeData.CurrentPursuitLevel</c>, arrested is <c>Player.IsArrested</c>.
/// Every entry point is total: any
/// fault reads as unreadable, never throws. Shared by Arthur's visit conditions and the
/// Chief's forced-Wanted trigger so the native lookups live in exactly one place.
/// </summary>
public sealed class S1ApiChapter3PlayerExposureReader : IChapter3PlayerExposureReader
{
    private readonly Action<string> _warn;
    private bool _disposed;

    public S1ApiChapter3PlayerExposureReader(Action<string> warn)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public bool TryRead(out Chapter3PlayerExposure exposure)
    {
        exposure = new(false, false, null, null, "the player exposure was unreadable.");
        if (_disposed)
            return false;
        try
        {
            var player = Player.Local;
            if (player is null)
            {
                exposure = new(false, false, null, null, "the local player was missing.");
                return false;
            }
            var onFoot = !player.IsInVehicle;
            var property = player.CurrentProperty;
            var crime = player.CrimeData;
            exposure = new(onFoot, property is null,
                crime is null ? null : (int?)crime.CurrentPursuitLevel,
                player.IsArrested,
                Describe(onFoot, property is null, property?.PropertyCode, crime));
            return true;
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 player exposure failed closed: {exception.GetType().Name}.");
            exposure = new(false, false, null, null, "the player exposure threw.");
            return false;
        }
    }

    public void Dispose() => _disposed = true;

    private static string Describe(bool onFoot, bool outdoors, string? propertyCode, PlayerCrimeData? crime)
    {
        if (!onFoot)
            return "the player is in a vehicle.";
        if (!outdoors)
            return string.IsNullOrWhiteSpace(propertyCode)
                ? "the player is inside a property."
                : $"the player is inside {propertyCode}.";
        if (crime is null)
            return "the player's crime data was unreadable.";
        if (crime.CurrentPursuitLevel == PursuitLevel.None)
            return "on foot, outdoors, clear of pursuit.";
        return $"the player is at pursuit level {crime.CurrentPursuitLevel}.";
    }
}
