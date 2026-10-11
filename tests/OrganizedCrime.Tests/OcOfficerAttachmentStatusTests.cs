using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcOfficerAttachmentStatusTests
{
    [Fact]
    public void Officers_status_reads_current_stale_counts_for_each_live_clone()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "attachments");
        roster.TryRequest("checkpoint", new(0, 0, 0), out _);
        roster.TryRequest("patrol", new(1, 0, 0), out _);
        roster.Update();
        roster.Update();
        world.Counts[1] = 0;
        world.Counts[2] = 4;
        var lines = roster.QaStatus();
        Assert.Contains(lines, line => line.Contains("oc_officer_attachments_1") && line.EndsWith("stale attachments: 0."));
        Assert.Contains(lines, line => line.Contains("oc_officer_attachments_2") && line.EndsWith("stale attachments: 4."));
        world.Counts[2] = 0;
        Assert.Equal(2, roster.QaStatus().Count(line => line.EndsWith("stale attachments: 0.")));
    }

    [Fact]
    public void A_failed_read_is_unknown_even_if_the_adapter_returns_zero()
    {
        var world = new World { ReadResult = OcOfficerWorldStatus.InvalidHandle };
        using var roster = Spawn(world);
        Assert.Contains(roster.QaStatus(), line => line.EndsWith("stale attachments: unknown."));
        Assert.DoesNotContain(roster.QaStatus(), line => line.EndsWith("stale attachments: 0."));
    }

    [Fact]
    public void A_throwing_diagnostic_preserves_the_roster_status_and_reports_unknown()
    {
        var world = new World { ThrowRead = true };
        using var roster = Spawn(world);
        var lines = roster.QaStatus();
        Assert.Contains(lines, line => line.Contains("OC officers: 1/"));
        Assert.Contains(lines, line => line.EndsWith("stale attachments: unknown."));
    }

    private static OcOfficerRosterService Spawn(World world)
    {
        var roster = new OcOfficerRosterService(world, "attachments");
        roster.TryRequest("checkpoint", new(0, 0, 0), out _);
        roster.Update();
        return roster;
    }

    private sealed class World : IOcOfficerWorld, IOcOfficerAttachmentDiagnostics
    {
        private int _serial;
        public Dictionary<int, int> Counts { get; } = new();
        public OcOfficerWorldStatus ReadResult { get; init; } = OcOfficerWorldStatus.Ok;
        public bool ThrowRead { get; init; }
        public bool IsHost => true;
        public OcOfficerWorldStatus TryReadStaleAttachmentCount(OcOfficerCloneRef clone, out int count)
        {
            if (ThrowRead) throw new InvalidOperationException("unreadable avatar");
            count = Counts.GetValueOrDefault(clone.Serial);
            return ReadResult;
        }
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail)
        { _serial++; clone = new(new object(), _serial, _serial); detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes)
        { minutes = 10; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail)
        { status = new(true, true, 120, id); detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut)
        { knockedOut = false; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position)
        { position = new(0, 0, 0); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
        { activeBehaviour = "checkpoint"; checkpointActive = true; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state)
        { state = new(true, true, OcCheckpointKind.Ours, null); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics)
        { diagnostics = new(Array.Empty<OcOfficerBehaviourInfo>(), "checkpoint", new(true, true, "Western", OcCheckpointKind.Ours)); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? standPoint, out string detail)
        { detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        { detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
        { detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryClearSwapMap(out string detail)
        { detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail)
        { detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail)
        { leaked = false; detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
        { status = new(true, true, 120, "template"); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts)
        { counts = new(10, 20); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees)
        { position = new(0, 0, 0); yawDegrees = 0; return OcOfficerWorldStatus.Ok; }
    }
}
