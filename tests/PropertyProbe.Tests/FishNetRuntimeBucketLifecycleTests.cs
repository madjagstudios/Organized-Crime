using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetRuntimeBucketLifecycleTests
{
    [Fact]
    public void Report_records_retrieval_and_cleanup_without_registration()
    {
        var snapshot = FishNetRuntimeBucketLifecycleSnapshot.Test(
            createReturnedBucket: true,
            retrieveReturnedSameBucket: true,
            removalReturnedTrue: true,
            postRemovalReturnedBucket: false);

        var text = FishNetRuntimeBucketLifecycleFormatter.FormatText(snapshot);

        Assert.Contains("CREATE_RETURNED_BUCKET: True", text);
        Assert.Contains("RETRIEVE_RETURNED_SAME_BUCKET: True", text);
        Assert.Contains("REMOVAL_RETURNED_TRUE: True", text);
        Assert.Contains("POST_REMOVAL_RETURNED_BUCKET: False", text);
        Assert.Contains("ADD_OBJECT_ATTEMPTED: False", text);
    }
}
