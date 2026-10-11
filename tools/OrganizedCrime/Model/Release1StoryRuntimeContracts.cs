namespace OrganizedCrime.Model;

public enum Release1StoryHostContextReadStatus { Ready, Pending, NotAuthoritative, UnsupportedMultiplayer, AmbiguousIdentity, Faulted }
public readonly record struct Release1StoryHostContextSnapshot(Guid SessionEpoch, long LoadEpoch, string PlayerId, string ActiveSaveFolder);
