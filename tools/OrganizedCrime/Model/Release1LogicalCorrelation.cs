namespace OrganizedCrime.Model;

public sealed record Release1LogicalCorrelation(
    string Value,
    string PlayerId,
    string MissionKey,
    int Attempt,
    Release1TransitionKind TransitionKind,
    string ReceiptId)
{
    public static Release1LogicalCorrelation Create(
        string playerId,
        string missionKey,
        int attempt,
        Release1TransitionKind transitionKind,
        string receiptId) =>
        ParseOrThrow($"oc10/v1/{playerId}/{missionKey}/{attempt}/{transitionKind}/{receiptId}");

    public static bool TryParse(string? value, out Release1LogicalCorrelation correlation)
    {
        correlation = null!;
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('/');
        if (parts.Length != 7 || parts[0] != "oc10" || parts[1] != "v1") return false;
        if (!int.TryParse(parts[4], out var attempt)) return false;
        if (!Enum.TryParse<Release1TransitionKind>(parts[5], ignoreCase: false, out var kind) ||
            !Enum.IsDefined(kind)) return false;
        if (!IsValidPart(parts[2], 128) || !IsValidPart(parts[3], 128) || !IsValidPart(parts[6], 256)) return false;
        if (parts[3] != Release1MissionCatalog.IntroScopeKey && !Release1MissionCatalog.IsEffectScopeKey(parts[3])) return false;
        if (parts[3] == Release1MissionCatalog.IntroScopeKey ? attempt != 0 : attempt < 1) return false;
        try
        {
            Release1StoryState.ValidatePlayerId(parts[2]);
            correlation = new Release1LogicalCorrelation(value, parts[2], parts[3], attempt, kind, parts[6]);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool IsValidPart(string value, int maxLength) =>
        value.Length > 0 && value.Length <= maxLength && !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace);

    private static Release1LogicalCorrelation ParseOrThrow(string value) =>
        TryParse(value, out var result) ? result : throw new ArgumentException("Correlation is not canonical.", nameof(value));
}
