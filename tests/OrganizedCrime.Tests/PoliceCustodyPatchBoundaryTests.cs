using System.Reflection;
using Il2CppScheduleOne.PlayerScripts;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class PoliceCustodyPatchBoundaryTests
{
    [Fact]
    public void Targets_only_the_authoritative_server_logic_method()
    {
        var target = PoliceCustodyPatch.ResolveTarget();

        Assert.Equal(typeof(Player), target.DeclaringType);
        Assert.Equal("RpcLogic___Arrest_Server_2166136261", target.Name);
        Assert.Empty(target.GetParameters());
        Assert.Equal(typeof(void), target.ReturnType);
    }

    [Fact]
    public void Declared_patch_targets_contain_no_release_completion_or_cause_specific_hook()
    {
        var targets = PoliceCustodyPatch.DeclaredTargetNames;

        Assert.Single(targets);
        Assert.Equal("RpcLogic___Arrest_Server_2166136261", targets[0]);
        Assert.DoesNotContain(targets, name => name.Contains("Free", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, name => name.Contains("ClearCrimes", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, name => name.Contains("ProcessCrimeList", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, name => name.Contains("OnDie", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, name => name.Contains("Knockout", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, name => name.Contains("Surrender", StringComparison.Ordinal));
    }

    [Fact]
    public void Prefix_and_postfix_are_static_and_carry_the_same_state_type()
    {
        var prefix = typeof(PoliceCustodyPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
        var postfix = typeof(PoliceCustodyPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(prefix);
        Assert.NotNull(postfix);
        Assert.Contains(prefix!.GetParameters(), parameter => parameter.Name == "__state");
        Assert.Contains(postfix!.GetParameters(), parameter => parameter.Name == "__state");
        var prefixStateType = prefix.GetParameters().Single(parameter => parameter.Name == "__state").ParameterType;
        var postfixStateType = postfix.GetParameters().Single(parameter => parameter.Name == "__state").ParameterType;
        Assert.Equal(
            prefixStateType.IsByRef ? prefixStateType.GetElementType() : prefixStateType,
            postfixStateType.IsByRef ? postfixStateType.GetElementType() : postfixStateType);
    }
}
