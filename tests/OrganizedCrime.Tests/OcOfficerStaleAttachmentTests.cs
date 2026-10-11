using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcOfficerStaleAttachmentTests
{
    private sealed record Attachment(string Path, bool Owned, bool Alive, object? Parent);
    private sealed record Parent(bool Alive);

    private static IReadOnlyList<Attachment> Select(params Attachment[] attachments) =>
        OcOfficerStaleAttachments.Select(attachments, a => a.Owned, a => a.Alive,
            a => a.Parent is Parent { Alive: true });

    [Fact]
    public void Null_and_destroyed_parents_are_stale()
    {
        var unparented = new Attachment("head", true, true, null);
        var destroyed = new Attachment("hips", true, true, new Parent(false));
        Assert.Equal(new[] { unparented, destroyed }, Select(unparented, destroyed));
    }

    [Fact]
    public void Identical_paths_keep_the_initialized_set_and_select_only_the_stale_set()
    {
        var stale = new[] { "head", "spine", "hips", "hips" }
            .Select(path => new Attachment(path, true, true, null)).ToArray();
        var live = stale.Select(a => a with { Parent = new Parent(true) }).ToArray();
        Assert.Equal(stale, Select(stale.Concat(live).ToArray()));
    }

    [Fact]
    public void Duplicate_paths_with_live_parents_are_all_preserved() =>
        Assert.Empty(Select(new("hips", true, true, new Parent(true)),
            new("hips", true, true, new Parent(true))));

    [Fact]
    public void Native_and_foreign_attachments_never_reach_parent_reads()
    {
        var native = new Attachment("head", false, true, null);
        var foreign = new Attachment("spine", false, true, new Parent(false));
        var selected = OcOfficerStaleAttachments.Select(new[] { native, foreign },
            a => a.Owned, _ => throw new Exception("native liveness"),
            _ => throw new Exception("native parent"));
        Assert.Empty(selected);
    }

    [Fact]
    public void Dead_attachments_never_reach_parent_reads()
    {
        var dead = new Attachment("head", true, false, null);
        Assert.Empty(OcOfficerStaleAttachments.Select(new[] { dead }, a => a.Owned,
            a => a.Alive, _ => throw new Exception("destroyed attachment")));
    }

    [Fact]
    public void A_unique_path_is_still_stale_when_its_parent_is_missing() =>
        Assert.Single(Select(new Attachment("unique", true, true, null)));

    [Fact]
    public void Selection_is_a_snapshot_for_destruction_and_a_second_pass_is_empty()
    {
        var attachments = new List<Attachment> { new("head", true, true, null),
            new("spine", true, true, new Parent(true)) };
        var selected = OcOfficerStaleAttachments.Select(attachments, a => a.Owned,
            a => a.Alive, a => a.Parent is Parent { Alive: true });
        foreach (var attachment in selected) attachments.Remove(attachment);
        Assert.Single(attachments);
        Assert.Empty(Select(attachments.ToArray()));
    }
}
