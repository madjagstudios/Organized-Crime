using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetRuntimePrefabCollectionTests
{
    [Fact]
    public void Report_identifies_runtime_collection_without_mutation()
    {
        var snapshot = FishNetRuntimePrefabCollectionSnapshot.Test(
            runtimeCollectionPresent: true,
            bucketCount: 1,
            relevantMembers: new[] { "PrefabObjects::AddObject(NetworkObject, Boolean)" });

        var text = FishNetRuntimePrefabCollectionFormatter.FormatText(snapshot);

        Assert.Contains("RUNTIME_COLLECTION_PRESENT: True", text);
        Assert.Contains("BUCKET_COUNT: 1", text);
        Assert.Contains("AddObject(NetworkObject, Boolean)", text);
        Assert.Contains("INVOCATION_ATTEMPTED: False", text);
        Assert.Contains("MUTATION_ATTEMPTED: False", text);
    }
}
