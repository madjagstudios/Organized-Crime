using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class SafehouseCensusTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Census_trigger_accepts_an_accessible_f6_key_or_the_existing_f17_key(
        bool f6Pressed,
        bool f17Pressed)
    {
        Assert.True(SafehouseCensusTrigger.IsPressed(f6Pressed, f17Pressed));
    }

    [Fact]
    public void Storage_stable_identity_includes_path_scene_id_and_meaningful_object_id()
    {
        var storage = SafehouseStorageSnapshot.Test("seweroffice", "World/Sewer/Storage");

        Assert.Equal("World/Sewer/Storage|scene:1|object:1", storage.StableIdentity);
    }

    [Fact]
    public void Formatter_sorts_registered_properties_and_storage_by_stable_path()
    {
        var snapshot = SafehouseCensusSnapshot.Test(
            new SafehousePropertySnapshot("z-code", "Z", "World/Z", false, new[]
            {
                SafehouseStorageSnapshot.Test("z-code", "World/Z/Storage-B"),
                SafehouseStorageSnapshot.Test("z-code", "World/Z/Storage-A")
            }),
            new SafehousePropertySnapshot("a-code", "A", "World/A", true, Array.Empty<SafehouseStorageSnapshot>()));

        var text = SafehouseCensusFormatter.FormatText(snapshot);

        Assert.True(text.IndexOf("PROPERTY_CODE: a-code", StringComparison.Ordinal) <
                    text.IndexOf("PROPERTY_CODE: z-code", StringComparison.Ordinal));
        Assert.True(text.IndexOf("STORAGE_PATH: World/Z/Storage-A", StringComparison.Ordinal) <
                    text.IndexOf("STORAGE_PATH: World/Z/Storage-B", StringComparison.Ordinal));
    }

    [Fact]
    public void Assessment_stops_if_the_census_attempted_mutation()
    {
        var before = SafehouseCensusSnapshot.Test(
            SafehousePropertySnapshot.Test("seweroffice", owned: false, storagePath: "World/Sewer/Storage"));
        var after = before with { MutationAttempted = true };

        var result = SafehouseCensusContract.Assess(before, after, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("STOP", result.Outcome);
        Assert.Contains("mutation", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assessment_is_inconclusive_until_identity_access_and_round_trip_are_observed()
    {
        var before = SafehouseCensusSnapshot.Test(
            SafehousePropertySnapshot.Test("seweroffice", owned: false, storagePath: "World/Sewer/Storage", itemCount: 2));
        var after = SafehouseCensusSnapshot.Test(
            SafehousePropertySnapshot.Test("seweroffice", owned: false, storagePath: "World/Sewer/Storage", itemCount: 1));

        var result = SafehouseCensusContract.Assess(before, after, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("INCONCLUSIVE", result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("ownership", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Reasons, reason => reason.Contains("contents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_passes_only_for_same_native_property_and_storage_identity_with_round_trip()
    {
        var before = SafehouseCensusSnapshot.Test(
            SafehousePropertySnapshot.Test("seweroffice", owned: true, storagePath: "World/Sewer/Storage", itemCount: 2));
        var after = before with { CapturePhase = "full-reload" };

        var result = SafehouseCensusContract.Assess(before, after, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("PASS", result.Outcome);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Assessment_is_inconclusive_for_same_path_with_different_scene_id_after_full_reload()
    {
        var before = SafehouseCensusSnapshot.Test(
            SafehousePropertySnapshot.Test("seweroffice", owned: true, storagePath: "World/Sewer/Storage"));
        var afterStorage = before.Properties.Single().Storages.Single() with { SceneId = 2 };
        var after = before with
        {
            CapturePhase = "full-reload",
            Properties = new[] { before.Properties.Single() with { Storages = new[] { afterStorage } } }
        };

        var result = SafehouseCensusContract.Assess(before, after, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("INCONCLUSIVE", result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_is_inconclusive_for_same_path_with_different_object_id_after_full_reload()
    {
        var before = SafehouseCensusSnapshot.Test(
            SafehousePropertySnapshot.Test("seweroffice", owned: true, storagePath: "World/Sewer/Storage"));
        var afterStorage = before.Properties.Single().Storages.Single() with { ObjectId = 2 };
        var after = before with
        {
            CapturePhase = "full-reload",
            Properties = new[] { before.Properties.Single() with { Storages = new[] { afterStorage } } }
        };

        var result = SafehouseCensusContract.Assess(before, after, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("INCONCLUSIVE", result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_is_inconclusive_when_native_storage_identity_is_unavailable()
    {
        var beforeStorage = SafehouseStorageSnapshot.Test("seweroffice", "World/Sewer/Storage") with
        {
            SceneId = 0,
            ObjectId = 0
        };
        var property = SafehousePropertySnapshot.Test("seweroffice", owned: true) with
        {
            Storages = new[] { beforeStorage }
        };
        var before = SafehouseCensusSnapshot.Test(property);
        var after = before with { CapturePhase = "full-reload" };

        var result = SafehouseCensusContract.Assess(before, after, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("INCONCLUSIVE", result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_stops_on_duplicate_registered_property_identity()
    {
        var first = SafehousePropertySnapshot.Test("seweroffice", owned: true, storagePath: "World/Sewer/Storage");
        var duplicate = SafehousePropertySnapshot.Test("seweroffice", owned: true, storagePath: "World/Sewer/Storage-2");
        var snapshot = SafehouseCensusSnapshot.Test(first, duplicate);

        var result = SafehouseCensusContract.Assess(snapshot, snapshot with { CapturePhase = "full-reload" }, "seweroffice", "World/Sewer/Storage");

        Assert.Equal("STOP", result.Outcome);
        Assert.Contains(result.Reasons, reason => reason.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }
}
