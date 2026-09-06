using Il2Cpp;
using Il2CppScheduleOne.Cartel;

namespace OrganizedCrime.Runtime;

public sealed class S1CartelStatusSource : IRelease1CartelStatusSource
{
    public Release1CartelStatusReadStatus TryRead(out Release1CartelStatus status)
    {
        status = default;

        try
        {
            if (!Cartel.InstanceExists)
                return Release1CartelStatusReadStatus.Pending;

            var cartel = Cartel.Instance;
            if (cartel == null)
                return Release1CartelStatusReadStatus.Pending;

            return TryMap(cartel.Status, out status)
                ? Release1CartelStatusReadStatus.Ready
                : Release1CartelStatusReadStatus.UnsupportedValue;
        }
        catch
        {
            status = default;
            return Release1CartelStatusReadStatus.Faulted;
        }
    }

    internal static bool TryMap(ECartelStatus nativeStatus, out Release1CartelStatus status)
    {
        status = nativeStatus switch
        {
            ECartelStatus.Unknown => Release1CartelStatus.Unknown,
            ECartelStatus.Hostile => Release1CartelStatus.Hostile,
            ECartelStatus.Truced => Release1CartelStatus.Truced,
            ECartelStatus.Defeated => Release1CartelStatus.Defeated,
            _ => default
        };

        return nativeStatus is ECartelStatus.Unknown or
            ECartelStatus.Hostile or
            ECartelStatus.Truced or
            ECartelStatus.Defeated;
    }
}
