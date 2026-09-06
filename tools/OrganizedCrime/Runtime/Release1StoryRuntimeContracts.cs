using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public enum Release1StoryHostContextReadStatus { Ready, Pending, NotAuthoritative, UnsupportedMultiplayer, AmbiguousIdentity, Faulted }
public readonly record struct Release1StoryHostContextSnapshot(Guid SessionEpoch, long LoadEpoch, string PlayerId, string ActiveSaveFolder);
public interface IRelease1StoryHostContext { Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot); }
public interface IRelease1StoryRepository
{
    Release1StoryStoreLoadResult Load();
    Release1StoryStoreUpdateResult Update(Release1StoryState? state);
}
public interface IRelease1StorySaveFolderBoundRepository
{
    string BoundSaveFolder { get; }
}
/// <summary>
/// Optional companion interface (OC-10) implemented by repositories that can rewrite the exact
/// bytes a failed load could not parse. The story runtime pattern-matches for this rather than
/// adding a required member to <see cref="IRelease1StoryRepository"/>, so the many test fakes that
/// only implement the base repository contract keep compiling unchanged.
/// </summary>
public interface IRelease1StoryRawContentPreservingRepository
{
    Release1StoryStoreUpdateResult PreserveRawContents(string rawContents);
}
public interface IRelease1StoryRepositoryFactory
{
    IRelease1StoryRepository Create(string activeSaveFolder);
}
public sealed class Release1StoryStateStoreRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository, IRelease1StoryRawContentPreservingRepository
{
    private readonly Release1StoryStateStore _store;
    public Release1StoryStateStoreRepository(Release1StoryStateStore store) => _store = store;
    public string BoundSaveFolder => _store.SavePath.ActiveSaveFolder;
    public Release1StoryStoreLoadResult Load() { _store.TryLoad(out var result); return result; }
    public Release1StoryStoreUpdateResult Update(Release1StoryState? state) { _store.TryUpdate(state, out var result); return result; }
    public Release1StoryStoreUpdateResult PreserveRawContents(string rawContents) { _store.TryPreserveRawContents(rawContents, out var result); return result; }
}
public enum Release1StoryRuntimePhase { Detached, AwaitingLoad, Active, Saving, Quarantined, Disposed }
public enum Release1StoryCommandStatus { Accepted, NoOp, DeferredSaving, Rejected }
public enum Release1StoryRuntimeRejectReason
{
    None, Inactive, ContextPending, NotAuthoritative, UnsupportedMultiplayer, AmbiguousIdentity,
    ContextFaulted, WrongEpoch, IdentityMismatch, InvalidTransition, SidecarLoadFailed,
    SidecarSaveFailed, Quarantined, Disposed, LifecycleTransition, RepositoryUnbound, RepositoryPathMismatch,
    RepositoryFaulted, EffectNotFound, EffectAmbiguous
}
public sealed record Release1StoryRuntimeCommandResult(Release1StoryCommandStatus Status, Release1StoryRuntimeRejectReason RejectReason, Release1StoryState? State, string Message)
{
    public bool Accepted => Status is Release1StoryCommandStatus.Accepted or Release1StoryCommandStatus.NoOp;
}
public enum Release1NativeEffectIssuanceOutcome { Applied, Failed, Ambiguous }
public enum Release1NativeEffectPersistenceMode { SaveGated, RevertTolerant }
public sealed record Release1StoryRuntimeLifecycleResult(Release1StoryRuntimePhase Phase, Release1StoryRuntimeRejectReason RejectReason, string Message);

/// <summary>
/// OC-10: the story runtime's lifecycle calls (OnPreLoad, OnLoadComplete, OnSaveStart,
/// OnSaveComplete) return a result the mod shell used to discard, so a quarantine (a rejected
/// host context, a corrupt sidecar, a mismatched identity) never reached the log. Both callers
/// route every non-accepted result through this single seam so the warning line stays uniform
/// across every lifecycle phase and every build (Debug and Release both wire
/// OrganizedCrimeLog.Warning to MelonLogger.Warning).
/// </summary>
public static class Release1StoryLifecycleLogging
{
    public static void LogIfRejected(string phase, Release1StoryRuntimeLifecycleResult? result)
    {
        if (result is null || result.RejectReason == Release1StoryRuntimeRejectReason.None) return;
        OrganizedCrimeLog.Warning($"Story runtime {phase} rejected: {result.RejectReason}: {result.Message}");
    }
}
