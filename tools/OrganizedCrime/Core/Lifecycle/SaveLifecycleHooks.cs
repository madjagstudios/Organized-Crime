using OrganizedCrime.Model;

namespace OrganizedCrime.Core.Lifecycle;

/// <summary>
/// Every result a <see cref="SaveLifecycle{TPhase, TResult, TEnvelope}"/>
/// skeleton can build. The six Faulted/Failed outcomes carry a message (an
/// exception message or a downstream failure message); the rest are fixed
/// per service and the message argument is ignored for them.
/// </summary>
public enum SaveLifecycleOutcome
{
    Disposed,
    PreLoadRepeat,
    LoadRepeat,
    SaveStartRepeat,
    SaveCompleteNoop,
    ContextPending,
    NotAuthoritative,
    RepositoryUnbound,
    PathMismatch,
    IdentityMismatch,
    SaveInactive,
    PreLoadOk,
    LoadOk,
    SaveStartOk,
    SaveCompleteOk,
    ContextFaulted,
    FactoryFaulted,
    LoadFaulted,
    LoadFailed,
    SaveFaulted,
    SaveFailed,
}

/// <summary>
/// Neutral repository-load projection for the load skeleton: whether the
/// load succeeded, the failure message when it did not, whether the envelope
/// carried a state at all, that state's player identity (which may itself be
/// null), and the envelope for activation. The service Load hook stashes raw
/// quarantine bytes itself before projecting.
/// </summary>
public sealed record SaveLifecycleLoadAttempt<TEnvelope>(
    bool Succeeded,
    string Message,
    bool HasState,
    string? EnvelopePlayerId,
    TEnvelope? Envelope) where TEnvelope : class
{
    public static SaveLifecycleLoadAttempt<TEnvelope> Loaded(bool hasState, string? envelopePlayerId, TEnvelope? envelope) =>
        new(true, string.Empty, hasState, envelopePlayerId, envelope);

    public static SaveLifecycleLoadAttempt<TEnvelope> Failed(string message) =>
        new(false, message, false, null, null);
}

/// <summary>Neutral host-context read: the Model status plus the snapshot.</summary>
public sealed record SaveLifecycleContextRead(
    Release1StoryHostContextReadStatus Status,
    Release1StoryHostContextSnapshot Snapshot);

/// <summary>
/// Neutral repository binding: whether the repository is bound to a
/// validated save folder, and that folder when it is.
/// </summary>
public sealed record SaveLifecycleBoundFolder(bool IsBound, string? Folder);

/// <summary>
/// Neutral save-update projection: whether the snapshot persisted, plus the
/// failure message when it did not. Success travels separately from the
/// message so a failed update with a null message still fails the save.
/// </summary>
public sealed record SaveLifecycleSaveAttempt(bool Succeeded, string? Message)
{
    public static SaveLifecycleSaveAttempt Ok() =>
        new(true, null);

    public static SaveLifecycleSaveAttempt Failed(string? message) =>
        new(false, message);
}

/// <summary>
/// Per-service behaviour behind a <see
/// cref="SaveLifecycle{TPhase, TResult, TEnvelope}"/>. All delegates run
/// under the service lock; the lifecycle takes no lock of its own. Unless a
/// hook is documented otherwise, a throw propagates out of the lifecycle call
/// with whatever phase was current when it threw.
/// </summary>
/// <typeparam name="TPhase">The service phase enum.</typeparam>
/// <typeparam name="TResult">The service lifecycle-result type.</typeparam>
/// <typeparam name="TEnvelope">The service save-envelope type.</typeparam>
public sealed class SaveLifecycleHooks<TPhase, TResult, TEnvelope>
    where TPhase : struct, Enum
    where TEnvelope : class
{
    /// <summary>The service AwaitingLoad phase value.</summary>
    public TPhase AwaitingLoadPhase { get; set; }
    /// <summary>The service Active phase value.</summary>
    public TPhase ActivePhase { get; set; }
    /// <summary>The service Saving phase value.</summary>
    public TPhase SavingPhase { get; set; }
    /// <summary>The service Quarantined phase value.</summary>
    public TPhase QuarantinedPhase { get; set; }
    /// <summary>Reads the current service phase.</summary>
    public Func<TPhase> GetPhase { get; set; } = null!;
    /// <summary>Writes the service phase.</summary>
    public Action<TPhase> SetPhase { get; set; } = null!;
    /// <summary>Whether the service is disposed.</summary>
    public Func<bool> IsDisposed { get; set; } = null!;

    /// <summary>
    /// Builds a service result for an outcome. Called at the same point the
    /// service used to build it, so current-phase readers see the same phase.
    /// The message is non-null only for the six Faulted/Failed outcomes.
    /// </summary>
    public Func<SaveLifecycleOutcome, string?, TResult> BuildResult { get; set; } = null!;

    /// <summary>
    /// Maps a non-ready, non-pending, non-authoritative-settling context
    /// status to the quarantine result. Runs once per rejected load, after
    /// the phase is set to Quarantined.
    /// </summary>
    public Func<Release1StoryHostContextReadStatus, TResult> MapContextStatus { get; set; } = null!;

    /// <summary>
    /// Optional. Runs on every non-disposed load completion after the
    /// disposed check and before the Active/Saving repeat guard, so it also
    /// runs on repeats. The Chapter 3 visit pump re-arms here; the story has
    /// no pre-guard step and leaves this null. A throw propagates and the
    /// load aborts before the guard.
    /// </summary>
    public Action? BeforeLoadGuard { get; set; }

    /// <summary>
    /// Reads the host context. Runs once per guarded load. A throw faults
    /// the load into Quarantined (ContextFaulted with the exception message).
    /// </summary>
    public Func<SaveLifecycleContextRead> ReadContext { get; set; } = null!;

    /// <summary>
    /// Creates and assigns the repository for the active save folder. Runs
    /// once per guarded load with a ready context. A throw (including a
    /// null factory result, which the service raises itself) faults the load
    /// into Quarantined (FactoryFaulted with the exception message).
    /// </summary>
    public Action<string> CreateRepository { get; set; } = null!;

    /// <summary>
    /// Reads the repository's bound save folder. Runs once per guarded load
    /// after creation. An unbound repository quarantines the load
    /// (RepositoryUnbound) without consulting SameSaveFolder. A throw faults
    /// the load into Quarantined (FactoryFaulted with the exception message),
    /// as the getter did when it ran inside the folder-compare try.
    /// </summary>
    public Func<SaveLifecycleBoundFolder> GetBoundSaveFolder { get; set; } = null!;

    /// <summary>
    /// Compares the bound folder with the context folder. Runs once per
    /// guarded load with a bound repository. A mismatch quarantines the load
    /// (PathMismatch); a throw faults it (FactoryFaulted). The two services
    /// keep their own comparisons (one never throws, one normalizes).
    /// </summary>
    public Func<string, string, bool> SameSaveFolder { get; set; } = null!;

    /// <summary>
    /// Loads the sidecar and projects the neutral attempt, stashing raw
    /// quarantine bytes on failure exactly as the service used to. Runs once
    /// per guarded load with matching folders. A throw faults the load
    /// (LoadFaulted); a failed attempt quarantines it (LoadFailed with the
    /// attempt message) without running Activate.
    /// </summary>
    public Func<SaveLifecycleLoadAttempt<TEnvelope>> Load { get; set; } = null!;

    /// <summary>
    /// Assigns the loaded state (and active context / persisted revision)
    /// from the envelope and snapshot. Runs once per guarded load whose
    /// identity check passed, before the phase is set to Active. A throw
    /// propagates with the pre-activation phase still current.
    /// </summary>
    public Action<TEnvelope?, Release1StoryHostContextSnapshot> Activate { get; set; } = null!;

    /// <summary>
    /// Runs post-activation steps once per effective load, after the phase is
    /// set to Active. Returns null to finish with LoadOk, or a result to
    /// return instead (the phase stays Active). Repeats never reach it.
    /// A throw propagates with the phase already Active.
    /// </summary>
    public Func<TResult?> AfterLoad { get; set; } = null!;

    /// <summary>
    /// Optional. Runs on every effective preload after the guards and before
    /// ClearForLoad. The Chapter 3 beating aborts here; repeats never reach
    /// it. A throw propagates with the phase and state still untouched.
    /// </summary>
    public Action? BeforeClear { get; set; }

    /// <summary>
    /// Clears the service state for load, including setting the phase to
    /// AwaitingLoad at the service's historical position. Runs once per
    /// effective preload, after BeforeClear. A throw propagates.
    /// </summary>
    public Action ClearForLoad { get; set; } = null!;

    /// <summary>
    /// Preserves quarantined raw bytes. Runs on every save start while
    /// Quarantined (it is not repeat-guarded) and returns the result
    /// directly. The service hook converts repository faults to results
    /// itself; anything it throws propagates.
    /// </summary>
    public Func<TResult> PreserveQuarantined { get; set; } = null!;

    /// <summary>
    /// Captures the save snapshot from the active state. Runs once per
    /// effective save start, after the pre-save phase is recorded and before
    /// the phase is set to Saving. A throw propagates with the phase still
    /// Active.
    /// </summary>
    public Action CaptureSaveSnapshot { get; set; } = null!;

    /// <summary>
    /// Clears the save snapshot. Runs on every save completion path: after a
    /// fault or failure (before quarantine), and on success (before the
    /// pre-save phase is restored). A throw propagates.
    /// </summary>
    public Action ClearSaveSnapshot { get; set; } = null!;

    /// <summary>
    /// Persists the save snapshot. Runs once per save completion while
    /// Saving. A failed attempt quarantines with SaveFailed and the attempt
    /// message (which may itself be null); a throw quarantines with
    /// SaveFaulted. Timing stays inside the service closure.
    /// </summary>
    public Func<SaveLifecycleSaveAttempt> SaveUpdate { get; set; } = null!;

    /// <summary>
    /// Optional. Runs once per successful save completion after the update
    /// and before the snapshot is cleared and the phase restored. The story
    /// records the persisted revision here; Chapter 3 leaves this null.
    /// A throw propagates with the phase still Saving.
    /// </summary>
    public Action? AfterSave { get; set; }
}
