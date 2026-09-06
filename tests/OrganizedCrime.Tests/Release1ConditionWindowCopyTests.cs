using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ConditionWindowCopyTests
{
    [Fact]
    public void Both_slots_are_optional_because_no_shipped_condition_fills_both()
    {
        // Keep the Lights Off ships a confirmed message and a breach quest line but no breach message;
        // A Room With No Name ships a confirmed message and no breach surface at all. A condition that
        // wants neither slot is legal too.
        Release1WindowCopy.None.Validate();
        new Release1WindowCopy(null, null).Validate();
        new Release1WindowCopy("Everything is out. Now nothing moves for one day.", null).Validate();
        new Release1WindowCopy(null, Release1KeepTheLightsOffPresentation.BreachStatusText).Validate();
    }

    [Fact]
    public void A_filled_slot_carries_the_text_it_was_given()
    {
        var copy = new Release1WindowCopy("Confirmed.", "Breached.");

        Assert.Equal("Confirmed.", copy.ConfirmedText);
        Assert.Equal("Breached.", copy.BreachedText);
        Assert.Null(Release1WindowCopy.None.ConfirmedText);
        Assert.Null(Release1WindowCopy.None.BreachedText);
    }

    // Every banned character is written as an escape sequence, the same way
    // Release1PlayerCopy.Normalize writes them, so no test file in this repository carries a literal
    // dash, a literal double hyphen, or a literal curly quote.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("An em dash \u2014 here")]
    [InlineData("An en dash \u2013 here")]
    [InlineData("A spaced em dash \u2014 and more")]
    [InlineData("A double hyphen \u002D\u002D here")]
    [InlineData("A curly \u2018quote\u2019 here")]
    [InlineData("A percent 50% here")]
    public void A_slot_that_is_empty_or_carries_banned_punctuation_is_refused(string text)
    {
        Assert.Throws<ArgumentException>(() => new Release1WindowCopy(text, null).Validate());
        Assert.Throws<ArgumentException>(() => new Release1WindowCopy(null, text).Validate());
    }

    [Fact]
    public void The_two_shipped_confirmed_lines_would_pass_the_slot_validation_unchanged()
    {
        // The shipped strings themselves stay owned by Release1PresentationPlan, which this ticket does
        // not touch. This pins that neither would have to change if a future condition carried it
        // through the slot instead.
        new Release1WindowCopy("Everything is out. Now nothing moves for one day.", null).Validate();
        new Release1WindowCopy("Good. It stays in the room until I call. One day. Fuck off until then.", null).Validate();
        new Release1WindowCopy(null, "It is back. Clear it again before the day resets.").Validate();
    }
}
