namespace OrganizedCrime.Model;

public enum Release1PhonePresentationAttemptState
{
    Pending,
    Attempting,
    Delivered,
    Ambiguous,
    Completed
}

public sealed record Release1PhonePresentationAttempt(
    string CorrelationId,
    string MissionKey,
    int Attempt,
    string Role,
    string? RequiredPriorCorrelationId,
    Release1PhonePresentationAttemptState State,
    long Revision)
{
    private const string NellRolePrefix = "release1-phone-nell-";
    private const string ArthurRolePrefix = "release1-phone-arthur-";

    // Mirrors Release1PhoneCallCorrelation.NellPresentationPrefix (Runtime layer): the receipt shape
    // a receipted Nell message uses on the native presentation path, where Nell never places a call.
    // Duplicated here rather than referenced so this model stays independent of the runtime layer.
    private const string NellPresentationPrefix = "presentation-nell-";

    public void Validate()
    {
        if (!Release1LogicalCorrelation.TryParse(CorrelationId, out var correlation) ||
            correlation.MissionKey != MissionKey || correlation.Attempt != Attempt)
            throw new ArgumentException("Phone presentation correlation was not canonical for its attempt.", nameof(CorrelationId));
        if (!Release1MissionCatalog.IsMissionKey(MissionKey) || Attempt < 1)
            throw new ArgumentException("Phone presentation mission or attempt was invalid.", nameof(MissionKey));
        if (Role is not ("Nell" or "Arthur"))
            throw new ArgumentException("Phone presentation role was invalid.", nameof(Role));
        var rolePrefix = Role == "Nell" ? NellRolePrefix : ArthurRolePrefix;
        if (!correlation.ReceiptId.StartsWith(rolePrefix, StringComparison.Ordinal))
            throw new ArgumentException("Phone presentation correlation did not carry its role tag.", nameof(CorrelationId));
        if (Role == "Arthur" && string.IsNullOrWhiteSpace(RequiredPriorCorrelationId))
            throw new ArgumentException("Arthur phone presentations require a prior correlation.", nameof(RequiredPriorCorrelationId));
        if (Role == "Nell" && RequiredPriorCorrelationId is not null)
            throw new ArgumentException("Nell phone presentations cannot have a prior correlation.", nameof(RequiredPriorCorrelationId));
        if (RequiredPriorCorrelationId is not null && !Release1LogicalCorrelation.TryParse(RequiredPriorCorrelationId, out _))
            throw new ArgumentException("Phone presentation prerequisite was not canonical.", nameof(RequiredPriorCorrelationId));
        if (RequiredPriorCorrelationId is not null)
        {
            Release1LogicalCorrelation.TryParse(RequiredPriorCorrelationId, out var prior);
            if (prior.PlayerId != correlation.PlayerId || prior.MissionKey != MissionKey || prior.Attempt != Attempt ||
                !(prior.ReceiptId.StartsWith(NellRolePrefix, StringComparison.Ordinal) ||
                  prior.ReceiptId.StartsWith(NellPresentationPrefix, StringComparison.Ordinal)))
                throw new ArgumentException("Phone presentation prerequisite did not match the attempt.", nameof(RequiredPriorCorrelationId));
        }
        if (!Enum.IsDefined(State) || Revision < 0)
            throw new ArgumentException("Phone presentation attempt state or revision was invalid.");
    }
}
