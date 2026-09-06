using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqStorageContractTests
{
    private const string PlayerId = "76561190000000001";
    private const string SaveFolder = @"C:\Saves\76561190000000001\SaveGame_4";

    [Fact]
    public void Contract_names_the_complete_vanilla_item_and_grid_failure_honestly()
    {
        Assert.NotNull(typeof(SyndicateHqStorageDefinition).GetProperty("ItemId"));
        Assert.Null(typeof(SyndicateHqStorageDefinition).GetProperty("VisualItemId"));
        Assert.True(Enum.IsDefined(typeof(SyndicateHqStorageStatus), "GridUnavailable"));
        Assert.False(Enum.IsDefined(typeof(SyndicateHqStorageStatus), "TemplateUnavailable"));
    }

    [Fact]
    public void Hq_storage_is_nine_distinct_native_twenty_slot_closets()
    {
        var definitions = SyndicateHqStorageContract.Definitions;

        Assert.True(SyndicateHqStorageContract.IsValid(out var reason), reason);
        Assert.Equal(9, definitions.Count);
        Assert.All(definitions, definition =>
        {
            Assert.Equal("hugestoragecloset", definition.ItemId);
            Assert.Equal(20, definition.RequiredSlotCount);
            Assert.True(definition.LocalPosition.IsFinite);
            Assert.True(float.IsFinite(definition.YawDegrees));
        });
        Assert.Equal(180, definitions.Sum(definition => definition.RequiredSlotCount));
        Assert.Equal(9, definitions.Select(definition => definition.Guid).Distinct().Count());
        Assert.Equal(
            new[]
            {
                Guid.Parse("8ec9d63b-f0f7-4af9-86fb-f1c73c7af481"),
                Guid.Parse("dcf4ca2a-4d27-47c4-a10a-2819ea298fe3")
            },
            definitions.Take(2).Select(definition => definition.Guid));
    }

    [Fact]
    public void East_half_leaves_a_center_cross_aisle_into_the_wall_bank()
    {
        Assert.DoesNotContain(
            SyndicateHqInteriorDefinition.NativeProps,
            prop => prop.Position.X >= 3.8f && MathF.Abs(prop.Position.Z) <= 3.5f);

        var definitions = SyndicateHqStorageContract.Definitions;
        var innerBank = definitions.Where(item => item.YawDegrees == 90f).ToArray();
        var eastBank = definitions.Where(item => item.YawDegrees == 270f).ToArray();

        Assert.Equal(4, innerBank.Length);
        Assert.Equal(5, eastBank.Length);
        Assert.All(innerBank, item => Assert.Equal(2.4f, item.LocalPosition.X));
        Assert.All(eastBank, item => Assert.Equal(5.2f, item.LocalPosition.X));
        Assert.Equal(new[] { -3.6f, -1.8f, 1.8f, 3.6f }, innerBank.Select(item => item.LocalPosition.Z).OrderBy(value => value));
        Assert.Equal(new[] { -3.6f, -1.8f, 0f, 1.8f, 3.6f }, eastBank.Select(item => item.LocalPosition.Z).OrderBy(value => value));

        var origins = definitions
            .Select(item => new SyndicateHqStorageGridCoordinate(item.GridOriginX, item.GridOriginY))
            .ToArray();
        Assert.Equal(9, origins.Distinct().Count());
        Assert.All(origins, origin =>
        {
            Assert.InRange(origin.X, 0, SyndicateHqStorageGridContract.Width - 1);
            Assert.InRange(origin.Y, 0, SyndicateHqStorageGridContract.Depth - 1);
        });
    }

    [Fact]
    public void Grid_shape_classifier_accepts_only_the_exact_legacy_or_current_rectangle()
    {
        var legacy = Rectangle(SyndicateHqStorageGridContract.LegacyWidth, SyndicateHqStorageGridContract.LegacyDepth);
        var current = Rectangle(SyndicateHqStorageGridContract.Width, SyndicateHqStorageGridContract.Depth);

        Assert.Equal(SyndicateHqStorageGridShape.LegacyTwoCloset, SyndicateHqStorageGridContract.Classify(legacy));
        Assert.Equal(SyndicateHqStorageGridShape.Current, SyndicateHqStorageGridContract.Classify(current));
        Assert.Equal(SyndicateHqStorageGridShape.Invalid, SyndicateHqStorageGridContract.Classify(legacy.Skip(1)));
        Assert.Equal(SyndicateHqStorageGridShape.Invalid, SyndicateHqStorageGridContract.Classify(legacy.Append(legacy[0])));
    }

    [Fact]
    public void Operations_desks_are_consolidated_on_the_west_side()
    {
        var boss = SyndicateHqInteriorDefinition.NativeProps.Single(item => item.Name == "BossDeskNative");
        var enforcer = SyndicateHqInteriorDefinition.NativeProps.Single(item => item.Name == "EnforcerDeskNative");

        Assert.True(boss.Position.X < 0f);
        Assert.True(enforcer.Position.X < boss.Position.X);
        Assert.InRange(MathF.Abs(enforcer.Position.X - boss.Position.X), 2f, 3f);
    }

    [Fact]
    public void Matching_canonical_host_and_save_binding_are_ready()
    {
        var result = SyndicateHqStorageAdmission.Evaluate(new(true, PlayerId, SaveFolder, SaveFolder));

        Assert.Equal(SyndicateHqStorageStatus.Ready, result.Status);
    }

    [Theory]
    [InlineData(false, PlayerId, SaveFolder, SaveFolder, SyndicateHqStorageStatus.NotCanonicalHost)]
    [InlineData(true, "0", SaveFolder, SaveFolder, SyndicateHqStorageStatus.NotCanonicalHost)]
    [InlineData(true, "not-a-steam-id", SaveFolder, SaveFolder, SyndicateHqStorageStatus.NotCanonicalHost)]
    [InlineData(true, PlayerId, "not-a-save-folder", SaveFolder, SyndicateHqStorageStatus.SaveBindingChanged)]
    [InlineData(true, PlayerId, SaveFolder, @"C:\Saves\76561190000000001\SaveGame_3", SyndicateHqStorageStatus.SaveBindingChanged)]
    [InlineData(true, "76561197984645360", SaveFolder, SaveFolder, SyndicateHqStorageStatus.SaveBindingChanged)]
    public void Invalid_authority_or_save_binding_fails_closed(
        bool isCanonicalHost,
        string playerId,
        string boundSaveFolder,
        string activeSaveFolder,
        SyndicateHqStorageStatus expected)
    {
        var result = SyndicateHqStorageAdmission.Evaluate(new(
            isCanonicalHost,
            playerId,
            boundSaveFolder,
            activeSaveFolder));

        Assert.Equal(expected, result.Status);
    }

    private static SyndicateHqStorageGridCoordinate[] Rectangle(int width, int depth) =>
        Enumerable.Range(0, width)
            .SelectMany(x => Enumerable.Range(0, depth).Select(y => new SyndicateHqStorageGridCoordinate(x, y)))
            .ToArray();
}
