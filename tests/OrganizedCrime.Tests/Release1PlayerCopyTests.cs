using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Unit tests for the two shared helpers OC-56 introduced on <see cref="Release1PlayerCopy"/>:
/// <see cref="Release1PlayerCopy.AttemptHeader{TMode}"/> (the label defect, spec section 3) and
/// <see cref="Release1PlayerCopy.Deadline"/> (the deadline defect, spec section 2.3). Each mission's
/// own presentation tests exercise these through the real assignment/mission types; this file pins
/// the helpers' own contract directly, across two distinct assignment-mode enums (Wrong Address and
/// Small Courtesy), to prove the generic implementation is not accidentally coupled to one mission's
/// enum.
/// </summary>
public sealed class Release1PlayerCopyTests
{
    [Theory]
    [InlineData(Release1WrongAddressAssignmentMode.Primary, 1, null)]
    [InlineData(Release1WrongAddressAssignmentMode.MakeGood, 2, "Make good, attempt 2")]
    [InlineData(Release1WrongAddressAssignmentMode.Recovery, 3, "Recovery, attempt 3")]
    public void AttemptHeader_covers_primary_make_good_and_recovery_for_wrong_address(
        Release1WrongAddressAssignmentMode mode, int attempt, string? expected)
    {
        Assert.Equal(expected, Release1PlayerCopy.AttemptHeader(mode, attempt));
    }

    [Theory]
    [InlineData(Release1SmallCourtesyAssignmentMode.Primary, 1, null)]
    [InlineData(Release1SmallCourtesyAssignmentMode.MakeGood, 2, "Make good, attempt 2")]
    [InlineData(Release1SmallCourtesyAssignmentMode.Recovery, 3, "Recovery, attempt 3")]
    public void AttemptHeader_covers_primary_make_good_and_recovery_for_small_courtesy(
        Release1SmallCourtesyAssignmentMode mode, int attempt, string? expected)
    {
        Assert.Equal(expected, Release1PlayerCopy.AttemptHeader(mode, attempt));
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData(24d, "one day")]
    [InlineData(12d, "12 hours")]
    [InlineData(72d, "72 hours")]
    [InlineData(48.5d, "48.5 hours")]
    public void Deadline_formats_null_as_none_exactly_24_as_one_day_and_everything_else_as_hours(
        double? hours, string expected)
    {
        Assert.Equal(expected, Release1PlayerCopy.Deadline(hours));
    }
}
