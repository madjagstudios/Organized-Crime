using OrganizedCrime.PhoneCallProof;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class PhoneCallDefinitionInteropTests
{
    [Fact]
    public void Concrete_definition_binds_to_installed_phone_call_data_contract()
    {
        var constructor = typeof(OcPhoneCallDefinition).GetConstructor(new[] { typeof(string), typeof(string[]) });

        Assert.NotNull(constructor);
        Assert.True(typeof(S1API.PhoneCalls.PhoneCallDefinition).IsAbstract);
        Assert.Equal(
            "Il2CppScheduleOne.ScriptableObjects.PhoneCallData",
            typeof(OcPhoneCallDefinition).GetProperty(nameof(OcPhoneCallDefinition.Data))?.PropertyType.FullName);
        Assert.Equal(
            "S1API.PhoneCalls.PhoneCallDefinition",
            typeof(OcPhoneCallDefinition).BaseType?.FullName);
    }

    // This compiled call is the production construction gate. Invoking it requires
    // Unity/IL2CPP, which is intentionally reserved for the owner-run live proof.
    private static OcPhoneCallDefinition CompileTimeConstruction(string callerName, params string[] stages) =>
        new(callerName, stages);
}
