using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PresentationModeTests
{
    [Theory]
    [InlineData("ImguiFallback", Release1PresentationMode.ImguiFallback)]
    [InlineData("imguifallback", Release1PresentationMode.ImguiFallback)]
    [InlineData("IMGUIFALLBACK", Release1PresentationMode.ImguiFallback)]
    [InlineData("ImGuiFallback", Release1PresentationMode.ImguiFallback)]
    public void Parse_maps_imguifallback_ordinal_ignore_case_to_imguifallback(string value, Release1PresentationMode expected)
    {
        Assert.Equal(expected, Release1PresentationModeParser.Parse(value));
    }

    [Theory]
    [InlineData("Native")]
    [InlineData("native")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData(" ImguiFallback")]
    public void Parse_maps_everything_else_to_native(string? value)
    {
        Assert.Equal(Release1PresentationMode.Native, Release1PresentationModeParser.Parse(value));
    }

    [Fact]
    public void Parse_maps_unknown_value_to_native()
    {
        Assert.Equal(Release1PresentationMode.Native, Release1PresentationModeParser.Parse("UnknownValue"));
    }
}
