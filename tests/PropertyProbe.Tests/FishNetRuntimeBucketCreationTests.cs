using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetRuntimeBucketCreationTests
{
    [Fact]
    public void Report_separates_bucket_creation_from_prefab_registration()
    {
        var snapshot = FishNetRuntimeBucketCreationSnapshot.Test(
            collectionId: 65000,
            bucketCreated: true,
            beforeBucketCount: 0,
            afterBucketCount: 1);

        var text = FishNetRuntimeBucketCreationFormatter.FormatText(snapshot);

        Assert.Contains("COLLECTION_ID: 65000", text);
        Assert.Contains("BUCKET_CREATED: True", text);
        Assert.Contains("BEFORE_BUCKET_COUNT: 0", text);
        Assert.Contains("AFTER_BUCKET_COUNT: 1", text);
        Assert.Contains("ADD_OBJECT_ATTEMPTED: False", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
    }
}
