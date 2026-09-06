namespace OrganizedCrime.Model;

public enum Release1NativeEffectPhase
{
    Prepared,
    Applied,
    Committed
}

public sealed record Release1NativeEffectJournalEntry
{
    public Release1NativeEffectJournalEntry(string EffectId, string MissionKey, int Attempt, string EffectKind, string SourceIdentity, string DestinationIdentity, string AmountOrCargoIdentity, Release1NativeEffectPhase Phase, string? NativeReceiptId, long PreparedStoryRevision, string? CommittedStoryCorrelationId = null, string? AuthorizedStoryCorrelationId = null, bool ExecutionBlocked = false, long AuthorizedMissionRevision = -1)
    {
        this.EffectId = EffectId; this.MissionKey = MissionKey; this.Attempt = Attempt; this.EffectKind = EffectKind;
        this.SourceIdentity = SourceIdentity; this.DestinationIdentity = DestinationIdentity; this.AmountOrCargoIdentity = AmountOrCargoIdentity;
        this.Phase = Phase; this.NativeReceiptId = NativeReceiptId; this.PreparedStoryRevision = PreparedStoryRevision; this.CommittedStoryCorrelationId = CommittedStoryCorrelationId; this.AuthorizedStoryCorrelationId = AuthorizedStoryCorrelationId; this.ExecutionBlocked = ExecutionBlocked; this.AuthorizedMissionRevision = AuthorizedMissionRevision;
        Validate();
    }
    public string EffectId { get; init; }
    public string MissionKey { get; init; }
    public int Attempt { get; init; }
    public string EffectKind { get; init; }
    public string SourceIdentity { get; init; }
    public string DestinationIdentity { get; init; }
    public string AmountOrCargoIdentity { get; init; }
    public Release1NativeEffectPhase Phase { get; init; }
    public string? NativeReceiptId { get; init; }
    public long PreparedStoryRevision { get; init; }
    public string? CommittedStoryCorrelationId { get; init; }
    public string? AuthorizedStoryCorrelationId { get; init; }
    public bool ExecutionBlocked { get; init; }
    public long AuthorizedMissionRevision { get; init; }
    public void Validate()
    {
        Release1MissionRecord.ValidateId(EffectId, nameof(EffectId));
        if (!Release1MissionCatalog.IsEffectScopeKey(MissionKey) || Attempt < 1 || PreparedStoryRevision < 0 || AuthorizedMissionRevision < -1 || !Enum.IsDefined(Phase) || (ExecutionBlocked && Phase != Release1NativeEffectPhase.Prepared)) throw new ArgumentException("Native effect metadata is invalid.");
        Release1MissionRecord.ValidateId(EffectKind, nameof(EffectKind)); Release1MissionRecord.ValidateId(SourceIdentity, nameof(SourceIdentity));
        Release1MissionRecord.ValidateId(DestinationIdentity, nameof(DestinationIdentity)); Release1MissionRecord.ValidateId(AmountOrCargoIdentity, nameof(AmountOrCargoIdentity));
        if (Phase != Release1NativeEffectPhase.Prepared && string.IsNullOrWhiteSpace(NativeReceiptId)) throw new ArgumentException("Applied effects require a native receipt.", nameof(NativeReceiptId));
        if (Phase == Release1NativeEffectPhase.Prepared && NativeReceiptId is not null) throw new ArgumentException("Prepared effects cannot contain a native receipt.", nameof(NativeReceiptId));
        if (NativeReceiptId is not null) Release1MissionRecord.ValidateId(NativeReceiptId, nameof(NativeReceiptId));
        if (Phase == Release1NativeEffectPhase.Committed && string.IsNullOrWhiteSpace(CommittedStoryCorrelationId)) throw new ArgumentException("Committed effects require the accepted story correlation.", nameof(CommittedStoryCorrelationId));
        if (Phase != Release1NativeEffectPhase.Committed && CommittedStoryCorrelationId is not null) throw new ArgumentException("Only committed effects can contain a committed story correlation.", nameof(CommittedStoryCorrelationId));
        if (CommittedStoryCorrelationId is not null && !Release1LogicalCorrelation.TryParse(CommittedStoryCorrelationId, out _)) throw new ArgumentException("Committed story correlation was not canonical.", nameof(CommittedStoryCorrelationId));
        if (AuthorizedStoryCorrelationId is not null && !Release1LogicalCorrelation.TryParse(AuthorizedStoryCorrelationId, out _)) throw new ArgumentException("Authorized story correlation was not canonical.", nameof(AuthorizedStoryCorrelationId));
    }
    public bool ValueEquals(Release1NativeEffectJournalEntry? other) => other is not null && Equals(other);
    public bool Equals(Release1NativeEffectJournalEntry? other) => other is not null && EffectId == other.EffectId && MissionKey == other.MissionKey && Attempt == other.Attempt && EffectKind == other.EffectKind && SourceIdentity == other.SourceIdentity && DestinationIdentity == other.DestinationIdentity && AmountOrCargoIdentity == other.AmountOrCargoIdentity && Phase == other.Phase && NativeReceiptId == other.NativeReceiptId && PreparedStoryRevision == other.PreparedStoryRevision && CommittedStoryCorrelationId == other.CommittedStoryCorrelationId && AuthorizedStoryCorrelationId == other.AuthorizedStoryCorrelationId && ExecutionBlocked == other.ExecutionBlocked && AuthorizedMissionRevision == other.AuthorizedMissionRevision;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EffectId); hash.Add(MissionKey); hash.Add(Attempt); hash.Add(EffectKind);
        hash.Add(SourceIdentity); hash.Add(DestinationIdentity); hash.Add(AmountOrCargoIdentity);
        hash.Add(Phase); hash.Add(NativeReceiptId); hash.Add(PreparedStoryRevision); hash.Add(CommittedStoryCorrelationId); hash.Add(AuthorizedStoryCorrelationId); hash.Add(ExecutionBlocked); hash.Add(AuthorizedMissionRevision);
        return hash.ToHashCode();
    }
}

public enum Release1NativeEffectCommandKind { Prepare, MarkApplied, Commit }

public sealed record Release1NativeEffectCommand(
    Release1NativeEffectCommandKind Kind,
    string EffectId,
    string MissionKey,
    int Attempt,
    string EffectKind,
    string SourceIdentity,
    string DestinationIdentity,
    string AmountOrCargoIdentity,
    string? NativeReceiptId = null,
    string? StoryCorrelationId = null,
    long AuthorizedMissionRevision = -1);

public enum Release1NativeEffectTransitionRejectReason { None, InvalidCommand, InvalidEffect, DuplicateConflict, WrongPhase, MissingNativeReceipt, MissingStoryCorrelation }

public sealed record Release1NativeEffectTransitionResult(bool Accepted, bool Idempotent, IReadOnlyList<Release1NativeEffectJournalEntry> Effects, Release1NativeEffectTransitionRejectReason RejectReason, string Message)
{
    public static Release1NativeEffectTransitionResult Reject(Release1NativeEffectTransitionRejectReason reason, string message, IReadOnlyList<Release1NativeEffectJournalEntry> effects) => new(false, false, effects, reason, message);
}

public static class Release1NativeEffectJournal
{
    public static Release1NativeEffectTransitionResult Apply(IReadOnlyList<Release1NativeEffectJournalEntry> effects, Release1NativeEffectCommand command, long storyRevision)
    {
        effects ??= Array.Empty<Release1NativeEffectJournalEntry>();
        if (command is null || !Enum.IsDefined(command.Kind)) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.InvalidCommand, "Native effect command kind was not defined.", effects);
        var existing = effects.FirstOrDefault(e => e.EffectId == command.EffectId);
        try
        {
            if (command.Kind == Release1NativeEffectCommandKind.Prepare)
            {
                var entry = new Release1NativeEffectJournalEntry(command.EffectId, command.MissionKey, command.Attempt, command.EffectKind, command.SourceIdentity, command.DestinationIdentity, command.AmountOrCargoIdentity, Release1NativeEffectPhase.Prepared, null, storyRevision, AuthorizedStoryCorrelationId: command.StoryCorrelationId, AuthorizedMissionRevision: command.AuthorizedMissionRevision);
                if (existing is not null)
                {
                    var sameMetadata = existing.EffectId == entry.EffectId && existing.MissionKey == entry.MissionKey && existing.Attempt == entry.Attempt && existing.EffectKind == entry.EffectKind && existing.SourceIdentity == entry.SourceIdentity && existing.DestinationIdentity == entry.DestinationIdentity && existing.AmountOrCargoIdentity == entry.AmountOrCargoIdentity && existing.AuthorizedStoryCorrelationId == entry.AuthorizedStoryCorrelationId && existing.AuthorizedMissionRevision == entry.AuthorizedMissionRevision;
                    if (existing.Phase == Release1NativeEffectPhase.Prepared && sameMetadata) return new(true, true, effects, Release1NativeEffectTransitionRejectReason.None, "Duplicate prepare was idempotent.");
                    return Release1NativeEffectTransitionResult.Reject(existing.Phase == Release1NativeEffectPhase.Prepared ? Release1NativeEffectTransitionRejectReason.DuplicateConflict : Release1NativeEffectTransitionRejectReason.WrongPhase, "Effect ID already has different or finalized metadata.", effects);
                }
                return new(true, false, effects.Append(entry).ToArray(), Release1NativeEffectTransitionRejectReason.None, "Effect prepared.");
            }
            if (existing is null) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.InvalidEffect, "Effect was not prepared.", effects);
            if (command.Kind == Release1NativeEffectCommandKind.MarkApplied)
            {
                if (existing.Phase == Release1NativeEffectPhase.Applied && existing.NativeReceiptId == command.NativeReceiptId) return new(true, true, effects, Release1NativeEffectTransitionRejectReason.None, "Duplicate apply was idempotent.");
                if (existing.Phase != Release1NativeEffectPhase.Prepared) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.WrongPhase, "Only Prepared effects may be applied.", effects);
                if (string.IsNullOrWhiteSpace(command.NativeReceiptId)) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.MissingNativeReceipt, "Native receipt is required.", effects);
                var applied = existing with { Phase = Release1NativeEffectPhase.Applied, NativeReceiptId = command.NativeReceiptId, ExecutionBlocked = false };
                return new(true, false, effects.Select(e => e.EffectId == command.EffectId ? applied : e).ToArray(), Release1NativeEffectTransitionRejectReason.None, "Effect marked applied.");
            }
            if (existing.Phase == Release1NativeEffectPhase.Committed)
            {
                if (existing.NativeReceiptId == command.NativeReceiptId && existing.CommittedStoryCorrelationId == command.StoryCorrelationId)
                    return new(true, true, effects, Release1NativeEffectTransitionRejectReason.None, "Duplicate commit was idempotent.");
                return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.DuplicateConflict, "Committed effect metadata cannot be changed.", effects);
            }
            if (existing.Phase != Release1NativeEffectPhase.Applied) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.WrongPhase, "Only Applied effects may be committed.", effects);
            if (existing.NativeReceiptId != command.NativeReceiptId) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.DuplicateConflict, "Native receipt did not match the applied effect.", effects);
            if (string.IsNullOrWhiteSpace(command.StoryCorrelationId)) return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.MissingStoryCorrelation, "Story correlation is required.", effects);
            if (!Release1LogicalCorrelation.TryParse(command.StoryCorrelationId, out var correlation) || correlation.MissionKey != existing.MissionKey || correlation.Attempt != existing.Attempt)
                return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.MissingStoryCorrelation, "Story correlation did not match the effect.", effects);
            if (existing.AuthorizedStoryCorrelationId is null || existing.AuthorizedStoryCorrelationId != command.StoryCorrelationId)
                return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.MissingStoryCorrelation, "Story correlation did not match the effect authorization.", effects);
            var committed = existing with { Phase = Release1NativeEffectPhase.Committed, CommittedStoryCorrelationId = command.StoryCorrelationId };
            return new(true, false, effects.Select(e => e.EffectId == command.EffectId ? committed : e).ToArray(), Release1NativeEffectTransitionRejectReason.None, "Effect committed.");
        }
        catch (ArgumentException ex) { return Release1NativeEffectTransitionResult.Reject(Release1NativeEffectTransitionRejectReason.InvalidEffect, ex.Message, effects); }
    }
}
