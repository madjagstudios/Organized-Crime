using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc182CleanupCharacterizationTests
{
    [Fact]
    public void Null_property_does_not_query_registration()
    {
        Assert.False(RuntimePropertyCleanup.IsRegistered<Target>(null,
            () => throw new Exception(), Alive, Same));
    }

    [Fact]
    public void Live_property_registration_uses_native_identity()
    {
        var target = new Target(7);
        Assert.True(RuntimePropertyCleanup.IsRegistered(target, () => new Target(7), Alive, Same));
        Assert.False(RuntimePropertyCleanup.IsRegistered(target, () => new Target(8), Alive, Same));
        Assert.False(RuntimePropertyCleanup.IsRegistered(target, () => null, Alive, Same));
    }

    [Fact]
    public void Null_cleanup_target_is_already_clean()
    {
        Assert.True(RuntimePropertyCleanup.Clean<Target>(null, Alive, _ => throw new Exception()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Live_cleanup_preserves_result_and_target(bool result)
    {
        var target = new Target(7);
        var calls = 0;
        Assert.Equal(result, RuntimePropertyCleanup.Clean(target, Alive, value =>
        {
            Assert.Same(target, value); calls++; return result;
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Live_cleanup_exception_remains_visible_to_host()
    {
        var error = new InvalidOperationException("live failure");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            RuntimePropertyCleanup.Clean(new Target(7), Alive, _ => throw error)));
    }

    internal sealed record Target(int Id, bool Live = true);
    internal static bool Alive(Target? target) => target?.Live == true;
    // Simulate Unity equality: both a destroyed wrapper and null have ID zero.
    internal static bool Same(Target? left, Target? right) =>
        (Alive(left) ? left!.Id : 0) == (Alive(right) ? right!.Id : 0);
}
