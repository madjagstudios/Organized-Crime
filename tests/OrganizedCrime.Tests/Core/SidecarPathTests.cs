using OrganizedCrime.Core.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148: unit tests for the shared SidecarPath validator. The per-service
// SavePath facades keep their own result/reason types, but the validation
// policy and both message wordings live here exactly once.
public sealed class SidecarPathTests
{
    [Fact]
    public void Valid_folder_builds_the_sidecar_triple()
    {
        Assert.True(SidecarPath.TryValidate(Path.GetTempPath(), "chapter3.json", SidecarPathWording.Short, out var path, out var failure));

        Assert.Equal(SidecarPathFailureReason.None, failure.Reason);
        Assert.Equal(string.Empty, failure.Message);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), path!.ActiveSaveFolder);
        Assert.Equal(Path.Combine(path.ActiveSaveFolder, "OrganizedCrime"), path.SidecarDirectory);
        Assert.Equal(Path.Combine(path.SidecarDirectory, "chapter3.json"), path.SidecarFilePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_folder_is_rejected_with_wording_messages(string? folder)
    {
        Assert.False(SidecarPath.TryValidate(folder, "x.json", SidecarPathWording.Short, out var shortPath, out var shortFailure));
        Assert.Null(shortPath);
        Assert.Equal(SidecarPathFailureReason.Empty, shortFailure.Reason);
        Assert.Equal("Active save folder was empty.", shortFailure.Message);

        Assert.False(SidecarPath.TryValidate(folder, "x.json", SidecarPathWording.ScheduleI, out var schedulePath, out var scheduleFailure));
        Assert.Null(schedulePath);
        Assert.Equal(SidecarPathFailureReason.Empty, scheduleFailure.Reason);
        Assert.Equal("Active Schedule I save folder was empty.", scheduleFailure.Message);
    }

    [Fact]
    public void Missing_folder_is_rejected_with_wording_messages()
    {
        var missing = Path.Combine(Path.GetTempPath(), "oc-148-no-such-folder-" + Guid.NewGuid().ToString("N"));

        Assert.False(SidecarPath.TryValidate(missing, "x.json", SidecarPathWording.Short, out _, out var shortFailure));
        Assert.Equal(SidecarPathFailureReason.Missing, shortFailure.Reason);
        Assert.Equal("Active save folder did not exist.", shortFailure.Message);

        Assert.False(SidecarPath.TryValidate(missing, "x.json", SidecarPathWording.ScheduleI, out _, out var scheduleFailure));
        Assert.Equal(SidecarPathFailureReason.Missing, scheduleFailure.Reason);
        Assert.Equal($"Active Schedule I save folder did not exist: {Path.GetFullPath(missing)}", scheduleFailure.Message);
    }

    [Fact]
    public void Unresolvable_folder_is_rejected_with_wording_messages()
    {
        var unresolvable = "\0";

        Assert.False(SidecarPath.TryValidate(unresolvable, "x.json", SidecarPathWording.Short, out _, out var shortFailure));
        Assert.Equal(SidecarPathFailureReason.Unresolvable, shortFailure.Reason);
        Assert.NotEqual(string.Empty, shortFailure.Message);

        Assert.False(SidecarPath.TryValidate(unresolvable, "x.json", SidecarPathWording.ScheduleI, out _, out var scheduleFailure));
        Assert.Equal(SidecarPathFailureReason.Unresolvable, scheduleFailure.Reason);
        Assert.StartsWith("Active Schedule I save folder could not be resolved: ", scheduleFailure.Message);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        Assert.True(SidecarPath.TryValidate("  " + Path.GetTempPath() + "  ", "x.json", SidecarPathWording.Short, out var path, out _));

        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), path!.ActiveSaveFolder);
    }

    [Fact]
    public void Null_file_name_throws()
    {
        Assert.Throws<ArgumentNullException>(() => SidecarPath.TryValidate(Path.GetTempPath(), null!, SidecarPathWording.Short, out _, out _));
    }
}
