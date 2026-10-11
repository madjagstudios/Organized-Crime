namespace OrganizedCrime.Model;

/// <summary>
/// OC-134: Arthur Selby's spoken copy for the Chapter 3 enforcement
/// visits, in one owner-reviewable place. Nell's follow-up texts live
/// in <see cref="Chapter3NellCopy"/> with her other copy. Amounts
/// render like the QA status (<c>$1,250</c>).
/// </summary>
public static class Chapter3ArthurCopy
{
    /// <summary>
    /// The caller label for Arthur's phone fallback, matching the
    /// Release 1 Arthur calls' spelling exactly: one Arthur, not two.
    /// </summary>
    public const string CallerLabel = "Arthur Selby";

    public const string WarningLine1 = "Evening. Nell tells me you're behind.";

    public static string WarningLine2(int amountOwed) =>
        $"{FormatAmount(amountOwed)} to the family. That's what you owe.";

    public const string WarningLine3 = "Two days. I won't be this polite next time.";

    public const string CooperationLine1 = "Nell says you keep telling her no.";

    public const string CooperationLine2 = "The family doesn't like no. Take the next one.";

    public static string PhoneWarning(int amountOwed) =>
        $"Couldn't find you tonight. You owe the family {FormatAmount(amountOwed)}. Two days, then I find you.";

    public const string PhoneCooperation =
        "Nell says you keep saying no. Take the next job. That's not a request.";

    public static IReadOnlyList<string> WarningLines(int amountOwed) =>
        new[] { WarningLine1, WarningLine2(amountOwed), WarningLine3 };

    public const string BeatingScheduledLine = "I said I wouldn't be polite.";

    public const string BeatingAttackLine = "Wrong fucking move.";

    public static IReadOnlyList<string> CooperationLines() =>
        new[] { CooperationLine1, CooperationLine2 };

    private static string FormatAmount(int amountOwed) => $"${amountOwed:N0}";
}
