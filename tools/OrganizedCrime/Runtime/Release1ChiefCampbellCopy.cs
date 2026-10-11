using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Every player facing string Chief Campbell can say, plus the demand ladder the copy is written
/// around. Pure and S1API free so it is test linkable. The ladder caps at the third demand: a round
/// past the cap reads the third demand, never a fourth number, which is what spec decision 12 means
/// by "capped". No dash characters, plain apostrophes, no percent sign; every string round trips
/// through <see cref="Release1PlayerCopy.Normalize"/> unchanged.
/// </summary>
public static class Release1ChiefCampbellCopy
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    public const string ContactFirstName = "Chief";
    public const string ContactLastName = "Campbell";
    public const int FirstDemand = 25_000;
    public const int SecondDemand = 30_000;
    public const int ThirdDemand = 35_000;

    /// <summary>
    /// The whole dollar demand for a round, clamped to the ladder's three rungs. Delegates to
    /// <see cref="OrganizedCrime.Model.Release1ChiefDemandLadder"/> so there is exactly one
    /// definition of 25000, 30000, 35000 in the codebase.
    /// </summary>
    public static int DemandFor(int round) => OrganizedCrime.Model.Release1ChiefDemandLadder.For(round);

    /// <summary>Renders a whole dollar amount with no separator and no decimal, regardless of culture.</summary>
    public static string Dollars(int amount) => amount.ToString("0", Invariant);

    public const string FirstDemandText = "This is Chief Campbell of the Hyland Point police. Your file is on my desk. Prints, statements, an arrest record with your name on it. Twenty five thousand in cash and the file is gone.";
    public const string SecondDemandText = "Chief Campbell again. My officers booked you in, so the file is thicker and the price is thirty thousand.";
    public const string ThirdDemandText = "Chief Campbell. Thirty five thousand. That is the last number I write down. It does not rise again and it does not go away.";
    public static string DemandText(int round) => round <= 1 ? FirstDemandText : round == 2 ? SecondDemandText : ThirdDemandText;

    public const string DeclineReplyText = "Your choice. The file stays where it is. It gets thicker every time my officers pick you up, and the number goes up with it.";
    public const string ShortCashFormat = "You are short. The number is {0} in cash, in hand. Come back when you have it.";
    public static string ShortCashText(int demand) => string.Format(Invariant, ShortCashFormat, Dollars(demand));
    public const string PaidText = "Received. The file is shredded, your name is off the board, and my officers have no reason to know you. Do not give them one.";
    public const string WatchListText = "Chief Campbell. You are on the watch list now. My officers are told to look at you twice. That is all it is, for now.";
    public const string LockdownAnnouncementText = "Chief Campbell. The town goes to curfew tonight and stays there, and it is because of you. Every hour is curfew and my officers will arrest you on sight. Pay what you owe, or stay quiet long enough for this to cool.";
    public const string LiftPaidText = "Curfew is lifted. Consider the account settled.";
    public const string LiftCooledText = "Curfew is lifted. You went quiet long enough. The file is still on my desk.";
    public const string ForcedWantedWarningText = "A full day and you still haven't paid. Get caught out after curfew and my officers come for you.";
    // OC-73 review fix (finding 2). Sent once, the first time a payment's debit is permanently
    // blocked (Ambiguous or Rejected, never re-debited), so the player is told the demand still
    // stands instead of the record going quietly wedged with no further word from the Chief.
    public const string PaymentBlockedText = "Your payment could not be confirmed. The demand still stands.";

    public static string PayLabel(int demand) => $"Pay {Dollars(demand)}";
    public const string DeclineLabel = "Not today";

    public static string LateFeeText(int demand) =>
        $"Every day you make me wait costs you. It's {Dollars(demand)} now.";

    // OC-118. The protection retainer's words. The price tag renders the one tunable weekly price
    // with its thousands separator, so the offer and both buttons always agree with the debit. The
    // blocked line is the demand's blocked line with the demand taken out: a blocked protection
    // payment leaves no demand standing, so the shared text would lie.
    public static string ProtectionPriceTag() =>
        "$" + Release1ChiefProtection.ProtectionWeeklyPrice.ToString("N0", Invariant);

    public static string ProtectionOfferText() =>
        $"You're making noise. {ProtectionPriceTag()} a week and my people look the other way. Offer stands.";

    public static string BuyProtectionLabel() => $"Buy protection ({ProtectionPriceTag()})";

    public static string ExtendProtectionLabel() => $"Extend protection ({ProtectionPriceTag()})";

    public static string ProtectionCoveredText(int untilDay) => $"You're covered until day {untilDay}.";

    public static string ProtectionWarningText() => "Your cover ends tomorrow.";

    public static string ProtectionLapseText() => "You're on your own now.";

    public static string ProtectionSettleFirstText() => "Settle your file first.";

    public const string ProtectionBlockedText = "Your protection payment could not be confirmed. The offer stands.";

    public static string StandingText(int demand) =>
        $"The file is still on my desk. {Dollars(demand)} in cash and it is gone.";

    /// <summary>
    /// The one fixed line logged per load while the presentation mode is ImguiFallback (OC-52 rule,
    /// spec decision 24): the Chief presents nothing at all in that mode.
    /// </summary>
    public const string FallbackLogText = "Chief Campbell has no fallback presenter; he stays silent while the presentation mode is ImguiFallback.";
}
