using System.Reflection;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidCommandTests
{
    public class Target : DispatchProxy, IChapter3RaidQaTarget
    {
        internal int Calls;
        internal string? Code;
        public IReadOnlyList<string> RaidPreview(string? code) { Calls++; Code = code; return new[] { "preview" }; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Unexpected QA action.");
    }
    [Theory]
    [InlineData(null)] [InlineData("oc_fishwarehouse")]
    public void Preview_routes_optional_site_code(string? code)
    {
        var target = DispatchProxy.Create<IChapter3QaTarget, Target>();
        var args = code is null ? new[] { "raid", "preview" } : new[] { "raid", "preview", code };
        Assert.Equal(new[] { "preview" }, Chapter3QaCommand.Execute(target, args, true));
        Assert.Equal(1, ((Target)target).Calls);
        Assert.Equal(code, ((Target)target).Code);
    }
    [Fact]
    public void Disabled_and_invalid_commands_do_not_call_preview()
    {
        var target = DispatchProxy.Create<IChapter3QaTarget, Target>();
        Assert.Contains("disabled", Chapter3QaCommand.Execute(target, new[] { "raid", "preview" }, false).Single());
        Assert.Contains("Usage", Chapter3QaCommand.Execute(target, new[] { "raid", "seize" }, true).Single());
        Assert.Contains("Usage", Chapter3QaCommand.Execute(target, new[] { "raid", "preview", "a", "b" }, true).Single());
        Assert.Equal(0, ((Target)target).Calls);
    }
}
