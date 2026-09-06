namespace OrganizedCrime.Runtime;

/// <summary>
/// How the Small Courtesy quest's native lifetime relates to a save load. <see cref="S1ApiPersisted"/>
/// leaves the S1API quest instance alone across saves and loads (S1API's own save data keeps it in
/// sync); <see cref="DisposablePerLoad"/> ends the quest on <c>OnSaveStart</c> and lets the boundary
/// recreate it fresh on the next apply. No Unity or S1API references, so this file and its parser
/// (and every other helper below) are test-linkable; the MelonPreferences-backed reader lives beside
/// <see cref="Release1PresentationModePreference"/> instead.
/// </summary>
public enum Release1QuestPersistencePolicy
{
    S1ApiPersisted,
    DisposablePerLoad
}

/// <summary>Pure, deterministic parse of a stored preference string into a quest persistence policy.</summary>
public static class Release1QuestPersistencePolicyParser
{
    public static Release1QuestPersistencePolicy Parse(string? value) =>
        string.Equals(value, "S1ApiPersisted", StringComparison.OrdinalIgnoreCase)
            ? Release1QuestPersistencePolicy.S1ApiPersisted
            : Release1QuestPersistencePolicy.DisposablePerLoad;
}

/// <summary>Which native quest-entry call a desired entry state projects through.</summary>
public enum Release1QuestEntryProjectionAction
{
    SetInactive,
    Begin,
    Complete
}

/// <summary>Outcome of resolving the Nell contact against the NPC ids S1API already knows about.</summary>
public enum Release1ContactResolutionOutcome
{
    /// <summary>No NPC has the Nell id yet; safe to create one.</summary>
    Create,
    /// <summary>An NPC with the Nell id exists and is a <c>Release1NellNpc</c>; adopt it.</summary>
    Adopt,
    /// <summary>An NPC with the Nell id exists but is not a <c>Release1NellNpc</c>; never create a second one.</summary>
    WrongType
}

/// <summary>Outcome of resolving the Small Courtesy quest against a title lookup.</summary>
public enum Release1QuestResolutionOutcome
{
    /// <summary>No quest has the expected title yet; safe to create one.</summary>
    None,
    /// <summary>A quest with the expected title exists and is the expected type; adopt it.</summary>
    Found,
    /// <summary>A quest with the expected title exists but is not the expected type; never create a second one.</summary>
    WrongType
}

/// <summary>
/// Pure, S1API-free helpers backing <see cref="S1ApiRelease1NativePresentation"/>: the Nell contact
/// identity and creation/adoption/wrong-type classification, sent-message text normalization, desired
/// quest-entry-state classification, quest lookup classification, the decision-map label lookup, and
/// the bind-existing-versus-send-fresh classification for a desired decision.
/// </summary>
public static class Release1NativePresentationSupport
{
    /// <summary>Stable S1API identity for the Nell custom NPC; shared with the S1API-dependent Release1NellNpc.</summary>
    public const string NellNpcId = "oc_release1_nell";

    /// <summary>Stable S1API identity for the Chief Campbell custom NPC; shared with Release1ChiefCampbellNpc.</summary>
    public const string ChiefNpcId = "oc_release1_chief_campbell";

    /// <summary>
    /// Classifies how a contact should be resolved against the NPCs S1API already knows
    /// about: <see cref="Release1ContactResolutionOutcome.Adopt"/> when an NPC with
    /// <paramref name="targetId"/> is already an instance of this mod's contact type, <see cref="Release1ContactResolutionOutcome.WrongType"/>
    /// when an NPC with that id exists but isn't (never create a second NPC with the same id), and
    /// <see cref="Release1ContactResolutionOutcome.Create"/> when no NPC has that id yet.
    /// </summary>
    public static Release1ContactResolutionOutcome ClassifyContact(
        IEnumerable<(string? Id, bool IsContact)> candidates, string targetId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(targetId);
        foreach (var (id, isContact) in candidates)
        {
            if (!string.Equals(id, targetId, StringComparison.Ordinal)) continue;
            return isContact ? Release1ContactResolutionOutcome.Adopt : Release1ContactResolutionOutcome.WrongType;
        }
        return Release1ContactResolutionOutcome.Create;
    }

    /// <summary>
    /// Classifies a quest-by-title lookup: <see cref="Release1QuestResolutionOutcome.None"/> when no
    /// quest has the title yet, <see cref="Release1QuestResolutionOutcome.WrongType"/> when a quest
    /// has the title but isn't the expected type (never create a second quest with the same title),
    /// and <see cref="Release1QuestResolutionOutcome.Found"/> otherwise.
    /// </summary>
    public static Release1QuestResolutionOutcome ClassifyQuestLookup(bool exists, bool isExpectedType)
    {
        if (!exists) return Release1QuestResolutionOutcome.None;
        return isExpectedType ? Release1QuestResolutionOutcome.Found : Release1QuestResolutionOutcome.WrongType;
    }

    /// <summary>
    /// True when the labels currently observed natively already equal the desired option labels, in
    /// order, so <c>TrySetDecision</c> should bind callbacks onto the existing responses instead of
    /// sending a fresh prompt.
    /// </summary>
    public static bool ShouldBindExistingResponses(IReadOnlyList<string> existingLabels, IReadOnlyList<string> desiredLabels)
    {
        ArgumentNullException.ThrowIfNull(existingLabels);
        ArgumentNullException.ThrowIfNull(desiredLabels);
        return existingLabels.SequenceEqual(desiredLabels, StringComparer.Ordinal);
    }

    /// <summary>Normalizes native sent-message texts so the reconciler compares like with like.</summary>
    public static IReadOnlyList<string> NormalizeSentTexts(IEnumerable<string?> rawTexts)
    {
        ArgumentNullException.ThrowIfNull(rawTexts);
        var result = new List<string>();
        foreach (var text in rawTexts)
        {
            if (text is null) continue;
            result.Add(Release1PlayerCopy.Normalize(text));
        }
        return result;
    }

    /// <summary>Classifies a desired quest-entry state into the native call that should project it.</summary>
    public static Release1QuestEntryProjectionAction ClassifyEntryAction(Release1DesiredEntryState state) => state switch
    {
        Release1DesiredEntryState.Active => Release1QuestEntryProjectionAction.Begin,
        Release1DesiredEntryState.Complete => Release1QuestEntryProjectionAction.Complete,
        _ => Release1QuestEntryProjectionAction.SetInactive
    };

    /// <summary>
    /// Looks up the command bound to a label against a decision's option map. Used by
    /// <c>TrySetDecision</c> to reattach an <c>OnTriggered</c> callback to each already-showing
    /// native response when binding onto existing responses instead of sending a fresh prompt.
    /// </summary>
    public static bool TryMatchRebindCommand(
        IReadOnlyDictionary<string, Release1PresentationCommand>? activeOptions,
        string? label,
        out Release1PresentationCommand command)
    {
        command = default;
        if (activeOptions is null || label is null) return false;
        return activeOptions.TryGetValue(label, out command);
    }

    /// <summary>
    /// True when every desired option label has a matching entry in <paramref name="availableLabels"/>
    /// (the normalized labels of responses actually restored and available to bind, e.g.
    /// <c>Release1NellNpc.LoadedResponses.Keys</c>). Used by <c>TrySetDecision</c>'s bind-existing
    /// branch as a pre-check, before mutating any restored <c>Response</c>, so that a partial match
    /// (some desired labels have no restored counterpart) never leaves some callbacks bound and others
    /// not: when this returns false, the caller binds nothing and does not commit the decision id or
    /// callback, so the projector retries the whole bind on its next reconcile pass instead of treating
    /// a half-bound decision as settled.
    /// </summary>
    public static bool AllDesiredLabelsAvailable(IReadOnlyCollection<string> desiredLabels, IReadOnlySet<string> availableLabels)
    {
        ArgumentNullException.ThrowIfNull(desiredLabels);
        ArgumentNullException.ThrowIfNull(availableLabels);
        foreach (var label in desiredLabels)
        {
            if (!availableLabels.Contains(label)) return false;
        }
        return true;
    }
}
