using OrganizedCrime.Runtime;
using Xunit;
using static OrganizedCrime.Tests.Oc182CleanupCharacterizationTests;

namespace OrganizedCrime.Tests;

public sealed class Oc182CleanupRegressionTests
{
    [Fact]
    public void Destroyed_property_and_null_lookup_are_removed_despite_Unity_equality()
    {
        var property = new Target(7, Live: false);
        Assert.True(Same(null, property));
        Assert.False(RuntimePropertyCleanup.IsRegistered(property, () => null, Alive, Same));
    }

    [Fact]
    public void Destroyed_property_is_removed_even_when_collection_still_holds_its_wrapper()
    {
        var property = new Target(7, Live: false);
        Assert.False(RuntimePropertyCleanup.IsRegistered(property, () => property, Alive, Same));
    }

    [Fact]
    public void Destroyed_property_does_not_touch_an_unloading_registry()
    {
        Assert.False(RuntimePropertyCleanup.IsRegistered(new Target(7, Live: false),
            () => throw new InvalidOperationException("unloading registry"), Alive, Same));
    }

    [Fact]
    public void Dead_lookup_is_not_compared_to_a_live_property()
    {
        Assert.False(RuntimePropertyCleanup.IsRegistered(new Target(7),
            () => new Target(7, Live: false), Alive, (_, _) => throw new Exception()));
    }

    [Fact]
    public void Live_registered_native_identity_still_means_partial_cleanup()
    {
        var registered = RuntimePropertyCleanup.IsRegistered(new Target(7), () => new Target(7), Alive, Same);
        var cleanupPassed = !registered;
        Assert.True(registered);
        Assert.False(cleanupPassed);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("bucket")]
    [InlineData("manager")]
    public void Destroyed_cleanup_target_is_clean_without_native_access(string target)
    {
        Assert.True(RuntimePropertyCleanup.Clean(new Target(7, Live: false), Alive,
            _ => throw new InvalidOperationException(target + " already destroyed")));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Nested_bucket_cleanup_skips_a_dead_bucket_or_manager(bool bucketLive, bool managerLive)
    {
        Assert.True(RuntimePropertyCleanup.Clean(new Target(7, bucketLive), Alive,
            _ => RuntimePropertyCleanup.Clean(new Target(8, managerLive), Alive,
                _ => throw new Exception("unloading native collection"))));
    }

    [Fact]
    public void Live_bucket_removal_failure_still_means_partial_cleanup()
    {
        Assert.False(RuntimePropertyCleanup.Clean(new Target(7), Alive,
            _ => RuntimePropertyCleanup.Clean(new Target(8), Alive, _ => false)));
    }
}
