namespace OrganizedCrime.Runtime;

public enum Release1CartelStatus
{
    Unknown,
    Hostile,
    Truced,
    Defeated
}

public enum Release1CartelStatusReadStatus
{
    Ready,
    Pending,
    UnsupportedValue,
    Faulted
}

public enum Release1PostBenziesUnlockReadStatus
{
    Unlocked,
    Locked,
    Pending,
    NotAuthoritative,
    UnsupportedMultiplayer,
    AmbiguousIdentity,
    Faulted
}

public readonly record struct Release1PostBenziesUnlockSnapshot(
    Release1StoryHostContextSnapshot HostContext,
    Release1CartelStatus CartelStatus);

public interface IRelease1CartelStatusSource
{
    Release1CartelStatusReadStatus TryRead(out Release1CartelStatus status);
}

public interface IRelease1PostBenziesUnlockReader
{
    Release1PostBenziesUnlockReadStatus TryRead(out Release1PostBenziesUnlockSnapshot snapshot);
}
