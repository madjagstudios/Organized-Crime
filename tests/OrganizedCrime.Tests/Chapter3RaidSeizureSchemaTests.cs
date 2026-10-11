using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using System.Reflection;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidSeizureSchemaTests
{
    [Fact]
    public void Current_raid_schemas_are_19_and_2()
    {
        Assert.Equal(20, Chapter3SaveCodec.CurrentSchemaVersion);
        Assert.Equal(2, LocalPressureSaveCodec.CurrentSchemaVersion);
    }

    [Fact]
    public void Cooling_is_a_separate_narrow_internal_operation()
    {
        Assert.NotNull(typeof(LocalPressureTransitions).GetMethod("ApplyRaidCooling", BindingFlags.Static | BindingFlags.NonPublic));
    }
}
