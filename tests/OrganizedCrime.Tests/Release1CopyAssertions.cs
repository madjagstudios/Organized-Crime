using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Shared assertions for Nell's player-facing copy (OC-56 spec, section 6). Generalises the
/// six-line cap that used to live only on Small Courtesy's status body
/// (<c>AssertStatusFirstAndCompact</c>) so every mission's terms and active bodies can be checked
/// against the same rule (section 2.1: at most six lines, one fact per line). The Envelope's terms
/// body is the one exception this ticket knowingly leaves over the cap: its copy is frozen pending
/// the OC-61 closet-version rewrite, so only its structural header/deadline mechanism changed here,
/// not its line count.
/// </summary>
internal static class Release1CopyAssertions
{
    public static void AssertAtMostSixLines(string body) =>
        Assert.True(
            body.Split('\n').Length <= 6,
            $"Expected at most six lines, but found {body.Split('\n').Length}: {body}");

    /// <summary>
    /// Verifies the OC-56 spec's amended section 2.1 shape: the six-line cap applies to the fact
    /// lines only, and the attempt header built by <c>Release1PlayerCopy.AttemptHeader</c> is an
    /// extra first line present only on make-good and recovery. Pass the expected header text (or
    /// null for a primary offer, which gets no header at all) alongside the full terms body, and this
    /// checks both the header placement and the fact-line cap for every mode in one call.
    /// </summary>
    public static void AssertAttemptHeaderAndFactLineCap(string body, string? headerLine)
    {
        var lines = body.Split('\n');
        if (headerLine is null)
        {
            Assert.DoesNotContain("attempt", lines[0], StringComparison.Ordinal);
            AssertAtMostSixLines(body);
            return;
        }

        Assert.Equal(headerLine, lines[0]);
        var factLineCount = lines.Length - 1;
        Assert.True(
            factLineCount <= 6,
            $"Expected at most six fact lines after the header, but found {factLineCount}: {body}");
    }
}
