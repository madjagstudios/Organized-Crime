namespace OrganizedCrime.Runtime;

/// <summary>
/// The optional per condition copy pair that rides alongside the elapsed text a mission already owns.
/// Optional matters: one shipped condition has a confirmed message and a breach quest line but no
/// breach message, and the other has a confirmed message and no breach surface at all. This type
/// defines the slots and never invents a surface, so no mission gains or loses a string by its
/// existence.
///
/// Under OC-64 it has no production call site: both shipped conditions project their confirmed and
/// breached surfaces from constants private to Release1PresentationPlan, and that file changes by zero
/// lines in this ticket. The first filler is the condition OC-65 swaps in, which brings its own strings.
///
/// Validation is the same bar every player facing string in this codebase meets: normalized already, so
/// Release1PlayerCopy.Normalize is a no-op on it, and free of the punctuation the copy rules ban.
/// </summary>
public sealed record Release1WindowCopy(string? ConfirmedText, string? BreachedText)
{
    /// <summary>A condition with no confirmed and no breached surface of its own.</summary>
    public static Release1WindowCopy None { get; } = new(null, null);

    public void Validate()
    {
        ValidateSlot(ConfirmedText, nameof(ConfirmedText));
        ValidateSlot(BreachedText, nameof(BreachedText));
    }

    private static void ValidateSlot(string? value, string parameterName)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A copy slot that exists cannot be empty.", parameterName);
        if (!string.Equals(Release1PlayerCopy.Normalize(value), value, StringComparison.Ordinal))
            throw new ArgumentException("Player copy must already be normalized.", parameterName);
        if (value.Contains("\u002D\u002D", StringComparison.Ordinal))
            throw new ArgumentException("Player copy cannot carry a double hyphen.", parameterName);
        foreach (var banned in new[] { '\u2018', '\u2019', '%' })
            if (value.Contains(banned))
                throw new ArgumentException("Player copy carries a banned character.", parameterName);
    }
}
