namespace OrganizedCrime.Runtime;

public sealed class Release1PostBenziesUnlockReader : IRelease1PostBenziesUnlockReader
{
    private readonly IRelease1StoryHostContext _hostContext;
    private readonly IRelease1CartelStatusSource _cartelStatusSource;

    public Release1PostBenziesUnlockReader(
        IRelease1StoryHostContext hostContext,
        IRelease1CartelStatusSource cartelStatusSource)
    {
        _hostContext = hostContext ?? throw new ArgumentNullException(nameof(hostContext));
        _cartelStatusSource = cartelStatusSource ?? throw new ArgumentNullException(nameof(cartelStatusSource));
    }

    public Release1PostBenziesUnlockReadStatus TryRead(out Release1PostBenziesUnlockSnapshot snapshot)
    {
        snapshot = default;

        try
        {
            var hostStatus = _hostContext.TryRead(out var hostSnapshot);
            if (hostStatus != Release1StoryHostContextReadStatus.Ready)
                return MapHostStatus(hostStatus);

            var cartelReadStatus = _cartelStatusSource.TryRead(out var cartelStatus);
            if (cartelReadStatus != Release1CartelStatusReadStatus.Ready)
                return cartelReadStatus == Release1CartelStatusReadStatus.Pending
                    ? Release1PostBenziesUnlockReadStatus.Pending
                    : Release1PostBenziesUnlockReadStatus.Faulted;

            snapshot = new Release1PostBenziesUnlockSnapshot(hostSnapshot, cartelStatus);
            return cartelStatus == Release1CartelStatus.Defeated
                ? Release1PostBenziesUnlockReadStatus.Unlocked
                : Release1PostBenziesUnlockReadStatus.Locked;
        }
        catch
        {
            snapshot = default;
            return Release1PostBenziesUnlockReadStatus.Faulted;
        }
    }

    private static Release1PostBenziesUnlockReadStatus MapHostStatus(Release1StoryHostContextReadStatus status) => status switch
    {
        Release1StoryHostContextReadStatus.Pending => Release1PostBenziesUnlockReadStatus.Pending,
        Release1StoryHostContextReadStatus.NotAuthoritative => Release1PostBenziesUnlockReadStatus.NotAuthoritative,
        Release1StoryHostContextReadStatus.UnsupportedMultiplayer => Release1PostBenziesUnlockReadStatus.UnsupportedMultiplayer,
        Release1StoryHostContextReadStatus.AmbiguousIdentity => Release1PostBenziesUnlockReadStatus.AmbiguousIdentity,
        _ => Release1PostBenziesUnlockReadStatus.Faulted
    };
}
