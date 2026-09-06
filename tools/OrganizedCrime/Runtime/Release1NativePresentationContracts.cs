namespace OrganizedCrime.Runtime;

/// <summary>
/// Outcome of a single call across the native presentation boundary. <see cref="Unavailable"/> and
/// <see cref="Rejected"/> are ordinary, recoverable conditions (the phone is off-screen, the native
/// surface refused the mutation); the reconciler stops the current pass without recording anything
/// and simply retries the diff on the next pass. <see cref="Faulted"/> is treated like a thrown
/// exception: the reconciler logs once per pass and stops.
/// </summary>
public enum Release1NativePresentationStatus
{
    Succeeded,
    Unavailable,
    Rejected,
    Faulted
}

/// <summary>
/// Decision prompt as currently observed on the native surface. <see cref="Id"/> is null when the
/// boundary cannot recover which logical decision produced these labels (for example, immediately
/// after a save load, before any <c>TrySetDecision</c> call in this session set it); in that case
/// the projector treats the desired decision as present only when both its option labels, in order,
/// and <see cref="Prompt"/> match, since two different decisions can share the same label set (for
/// example the intro and Small Courtesy accept decisions both offer "Accept"/"Not now") and
/// label-only matching would otherwise treat one as if it were the other. <see cref="Prompt"/> is the
/// normalized text of the most recent NPC message in native history, or null when there is none.
/// </summary>
public sealed record Release1ObservedDecision(string? Id, IReadOnlyList<string> Labels, string? Prompt);

/// <summary>Quest entry states as currently observed on the native surface.</summary>
public sealed record Release1ObservedQuest(string Key, IReadOnlyList<Release1DesiredEntryState> EntryStates);

/// <summary>
/// The native presentation boundary: passive messages, at most one decision prompt, and quest
/// entries. Implemented against S1API/Unity in a later task; this contract has no Unity or S1API
/// references so it can be exercised with a fake in tests.
/// </summary>
public interface IRelease1NativePresentation
{
    Release1NativePresentationStatus TryEnsureContact();

    /// <summary>
    /// Reads native message history sent to the player. Not called from
    /// <see cref="Release1PresentationProjector.Reconcile"/>: message sends are deduped solely by
    /// <see cref="Release1StoryState.PresentationReceipts"/> now, since native history and receipts
    /// revert together with the save. Kept on the interface for the boundary implementation and its
    /// own tests.
    /// </summary>
    Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts);
    Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision);
    Release1NativePresentationStatus TrySendMessage(string text);
    Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen);
    Release1NativePresentationStatus TryClearDecision();
    Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest);
    Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest);
    Release1NativePresentationStatus TryEndQuest(string key);
    void OnPreLoad();
    void OnSaveStart();
}

/// <summary>Sink for commands bound to a chosen decision option.</summary>
public interface IRelease1PresentationCommandSink
{
    bool TryInvoke(Release1PresentationCommand command);
}
