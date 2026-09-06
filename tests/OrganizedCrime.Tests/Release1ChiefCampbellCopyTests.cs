using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefCampbellCopyTests
{
    private static void AssertPlayerCopy(string value)
    {
        Assert.Equal(value, Release1PlayerCopy.Normalize(value));
        Assert.DoesNotContain('—', value);
        Assert.DoesNotContain('–', value);
        Assert.DoesNotContain("--", value, StringComparison.Ordinal);
        Assert.DoesNotContain('’', value);
        Assert.DoesNotContain('%', value);
    }

    [Theory]
    [InlineData(0, 15000, "15000")] [InlineData(1, 15000, "15000")] [InlineData(2, 20000, "20000")]
    [InlineData(3, 25000, "25000")] [InlineData(4, 25000, "25000")] [InlineData(99, 25000, "25000")]
    public void The_ladder_is_fifteen_then_twenty_then_twenty_five_thousand_and_caps(int round, int expected, string rendered)
    {
        Assert.Equal(expected, Release1ChiefCampbellCopy.DemandFor(round));
        Assert.Equal(rendered, Release1ChiefCampbellCopy.Dollars(expected));
        Assert.Equal(15000, Release1ChiefCampbellCopy.FirstDemand);
        Assert.Equal(20000, Release1ChiefCampbellCopy.SecondDemand);
        Assert.Equal(25000, Release1ChiefCampbellCopy.ThirdDemand);
    }

    // One Assert.Equal per row of the spec's Messages, Decision options and Status tables, each
    // written out as a string literal in the test and compared against the member below, so the
    // owner's exact wording is pinned in two independent places. Step 2 carries the same strings
    // verbatim; a copy edit that touches one and not the other fails here.
    [Fact]
    public void Every_string_reads_exactly_as_the_spec_wrote_it()
    {
        Assert.Equal("This is Chief Campbell of the Hyland Point police. Your file is on my desk. Prints, statements, an arrest record with your name on it. Fifteen thousand in cash and the file is gone.", Release1ChiefCampbellCopy.DemandText(1));
        Assert.Equal(Release1ChiefCampbellCopy.SecondDemandText, Release1ChiefCampbellCopy.DemandText(2));
        Assert.Equal(Release1ChiefCampbellCopy.ThirdDemandText, Release1ChiefCampbellCopy.DemandText(3));
        Assert.Equal(Release1ChiefCampbellCopy.DemandText(3), Release1ChiefCampbellCopy.DemandText(4));
        Assert.Equal("Chief Campbell again. My officers booked you in, so the file is thicker and the price is twenty thousand.", Release1ChiefCampbellCopy.SecondDemandText);
        Assert.Equal("Chief Campbell. Twenty five thousand. That is the last number I write down. It does not rise again and it does not go away.", Release1ChiefCampbellCopy.ThirdDemandText);
        Assert.Equal("You are short. The number is 20000 in cash, in hand. Come back when you have it.", Release1ChiefCampbellCopy.ShortCashText(20000));
        Assert.Equal("Pay 15000", Release1ChiefCampbellCopy.PayLabel(15000));
        Assert.Equal("Pay 25000", Release1ChiefCampbellCopy.PayLabel(25000));
        Assert.Equal("Not today", Release1ChiefCampbellCopy.DeclineLabel);
        // The eight fixed strings with no substitution are pinned the same way, one Assert.Equal each,
        // against DeclineReplyText, PaidText, WatchListText, LockdownAnnouncementText, LiftPaidText,
        // LiftCooledText and PaymentBlockedText, quoting Step 2's literals exactly (PaymentBlockedText
        // is OC-73 review fix, finding 2, added after Step 2 was written).
        Assert.Equal("Your choice. The file stays where it is. It gets thicker every time my officers pick you up, and the number goes up with it.", Release1ChiefCampbellCopy.DeclineReplyText);
        Assert.Equal("Received. The file is shredded, your name is off the board, and my officers have no reason to know you. Do not give them one.", Release1ChiefCampbellCopy.PaidText);
        Assert.Equal("Chief Campbell. You are on the watch list now. My officers are told to look at you twice. That is all it is, for now.", Release1ChiefCampbellCopy.WatchListText);
        Assert.Equal("Chief Campbell. The town goes to curfew tonight and stays there, and it is because of you. Every hour is curfew and my officers will arrest you on sight. Pay what you owe, or stay quiet long enough for this to cool.", Release1ChiefCampbellCopy.LockdownAnnouncementText);
        Assert.Equal("Curfew is lifted. Consider the account settled.", Release1ChiefCampbellCopy.LiftPaidText);
        Assert.Equal("Curfew is lifted. You went quiet long enough. The file is still on my desk.", Release1ChiefCampbellCopy.LiftCooledText);
        Assert.Equal("Your payment could not be confirmed. The demand still stands.", Release1ChiefCampbellCopy.PaymentBlockedText);
    }

    // One AssertPlayerCopy call per entry of a sixteen element array literal holding every player
    // facing string the class exposes: the three demand texts, the decline reply, a short cash line,
    // the paid, watch list, lockdown announcement, both lift lines and the payment blocked line, a
    // pay label, the decline label, the fallback log line, and both name parts. OC-73 review fix,
    // finding 3: the five status strings (AdoptedStatusText, DemandOpenStatusText, DeclinedStatusText,
    // LockdownStatusText, SettledStatusText) were dropped along with the dead BuildView/ViewModel that
    // was their only production consumer; see docs/worknotes/OC-73.md for the choice between wiring
    // them into ImguiFallback or deleting them.
    [Fact]
    public void Every_player_facing_string_passes_the_copy_bar()
    {
        var strings = new[]
        {
            Release1ChiefCampbellCopy.DemandText(1),
            Release1ChiefCampbellCopy.DemandText(2),
            Release1ChiefCampbellCopy.DemandText(3),
            Release1ChiefCampbellCopy.DeclineReplyText,
            Release1ChiefCampbellCopy.ShortCashText(20000),
            Release1ChiefCampbellCopy.PaidText,
            Release1ChiefCampbellCopy.WatchListText,
            Release1ChiefCampbellCopy.LockdownAnnouncementText,
            Release1ChiefCampbellCopy.LiftPaidText,
            Release1ChiefCampbellCopy.LiftCooledText,
            Release1ChiefCampbellCopy.PaymentBlockedText,
            Release1ChiefCampbellCopy.PayLabel(15000),
            Release1ChiefCampbellCopy.DeclineLabel,
            Release1ChiefCampbellCopy.FallbackLogText,
            Release1ChiefCampbellCopy.ContactFirstName,
            Release1ChiefCampbellCopy.ContactLastName
        };

        Assert.Equal(16, strings.Length);
        foreach (var value in strings) AssertPlayerCopy(value);
    }
}
