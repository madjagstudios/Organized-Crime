namespace OrganizedCrime.Model;

/// <summary>
/// Proves a Chapter 3 message reached Nell's conversation once. Correlations
/// look like oc11-ch3/v1/{playerId}/{scope}; the player segment keeps one
/// slot's receipts from suppressing another slot's messages.
/// </summary>
public sealed record Chapter3PresentationReceipt(string CorrelationId, long Revision)
{
    public const string Prefix = "oc11-ch3/v1/";

    public static string Correlation(string playerId, string scope) => $"{Prefix}{playerId}/{scope}";

    public static bool TryParse(string? correlationId, out string playerId, out string scope)
    {
        playerId = string.Empty;
        scope = string.Empty;
        if (string.IsNullOrWhiteSpace(correlationId) || !correlationId.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var rest = correlationId.Substring(Prefix.Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return false;
        playerId = rest.Substring(0, slash);
        scope = rest.Substring(slash + 1);
        return true;
    }
}
