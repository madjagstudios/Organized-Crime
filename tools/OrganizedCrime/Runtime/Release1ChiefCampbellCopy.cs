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
    public const int FirstDemand = 15_000;
    public const int SecondDemand = 20_000;
    public const int ThirdDemand = 25_000;

    /// <summary>
    /// The whole dollar demand for a round, clamped to the ladder's three rungs. Delegates to
    /// <see cref="OrganizedCrime.Model.Release1ChiefDemandLadder"/> so there is exactly one
    /// definition of 15000, 20000, 25000 in the codebase.
    /// </summary>
    public static int DemandFor(int round) => OrganizedCrime.Model.Release1ChiefDemandLadder.For(round);

    /// <summary>Renders a whole dollar amount with no separator and no decimal, regardless of culture.</summary>
    public static string Dollars(int amount) => amount.ToString("0", Invariant);

    public const string FirstDemandText = "This is Chief Campbell of the Hyland Point police. Your file is on my desk. Prints, statements, an arrest record with your name on it. Fifteen thousand in cash and the file is gone.";
    public const string SecondDemandText = "Chief Campbell again. My officers booked you in, so the file is thicker and the price is twenty thousand.";
    public const string ThirdDemandText = "Chief Campbell. Twenty five thousand. That is the last number I write down. It does not rise again and it does not go away.";
    public static string DemandText(int round) => round <= 1 ? FirstDemandText : round == 2 ? SecondDemandText : ThirdDemandText;

    public const string DeclineReplyText = "Your choice. The file stays where it is. It gets thicker every time my officers pick you up, and the number goes up with it.";
    public const string ShortCashFormat = "You are short. The number is {0} in cash, in hand. Come back when you have it.";
    public static string ShortCashText(int demand) => string.Format(Invariant, ShortCashFormat, Dollars(demand));
    public const string PaidText = "Received. The file is shredded, your name is off the board, and my officers have no reason to know you. Do not give them one.";
    public const string WatchListText = "Chief Campbell. You are on the watch list now. My officers are told to look at you twice. That is all it is, for now.";
    public const string LockdownAnnouncementText = "Chief Campbell. The town goes to curfew tonight and stays there, and it is because of you. Every hour is curfew and my officers will arrest you on sight. Pay what you owe, or stay quiet long enough for this to cool.";
    public const string LiftPaidText = "Curfew is lifted. Consider the account settled.";
    public const string LiftCooledText = "Curfew is lifted. You went quiet long enough. The file is still on my desk.";
    // OC-73 review fix (finding 2). Sent once, the first time a payment's debit is permanently
    // blocked (Ambiguous or Rejected, never re-debited), so the player is told the demand still
    // stands instead of the record going quietly wedged with no further word from the Chief.
    public const string PaymentBlockedText = "Your payment could not be confirmed. The demand still stands.";

    public static string PayLabel(int demand) => $"Pay {Dollars(demand)}";
    public const string DeclineLabel = "Not today";

    /// <summary>
    /// The one fixed line logged per load while the presentation mode is ImguiFallback (OC-52 rule,
    /// spec decision 24): the Chief presents nothing at all in that mode.
    /// </summary>
    public const string FallbackLogText = "Chief Campbell has no fallback presenter; he stays silent while the presentation mode is ImguiFallback.";
}
