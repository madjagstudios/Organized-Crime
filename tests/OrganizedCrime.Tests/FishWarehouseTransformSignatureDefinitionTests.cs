using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseTransformSignatureDefinitionTests
{
    [Fact]
    public void Accepts_position_rotation_and_unit_scale_within_tolerance()
    {
        var anchor = Signature(new(10f, 20f, 30f), new(0f, 359.95f, 10f), new(1.0005f, 0.9995f, 1f));
        var propertyRoot = Signature(new(10.009f, 20f, 30f), new(0f, 0.04f, 10f), new(0.9995f, 1.0005f, 1f));

        Assert.True(FishWarehouseTransformSignatureDefinition.IsCoincident(anchor, propertyRoot));
    }

    [Theory]
    [MemberData(nameof(OutOfToleranceCases))]
    public void Rejects_each_transform_tolerance_independently(
        FishWarehouseTransformSignature anchor,
        FishWarehouseTransformSignature propertyRoot)
    {
        Assert.False(FishWarehouseTransformSignatureDefinition.IsCoincident(anchor, propertyRoot));
    }

    public static IEnumerable<object[]> OutOfToleranceCases()
    {
        yield return new object[]
        {
            Signature(new(10.011f, 20f, 30f), new(0f, 0f, 0f), new(1f, 1f, 1f)),
            Signature(new(10f, 20f, 30f), new(0f, 0f, 0f), new(1f, 1f, 1f))
        };
        yield return new object[]
        {
            Signature(new(10f, 20f, 30f), new(0f, 0.101f, 0f), new(1f, 1f, 1f)),
            Signature(new(10f, 20f, 30f), new(0f, 0f, 0f), new(1f, 1f, 1f))
        };
        yield return new object[]
        {
            Signature(new(10f, 20f, 30f), new(0f, 0f, 0f), new(1.0011f, 1f, 1f)),
            Signature(new(10f, 20f, 30f), new(0f, 0f, 0f), new(1f, 1f, 1f))
        };
    }

    private static FishWarehouseTransformSignature Signature(
        FishWarehouseTransformVector position,
        FishWarehouseTransformVector rotation,
        FishWarehouseTransformVector scale) =>
        new(position, rotation, scale);
}
