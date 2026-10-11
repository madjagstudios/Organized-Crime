using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-140 composition: the real Chapter 3 runtime service drives a real
/// roster service over a fake world, through the real QA command, exactly the
/// wiring Mod.cs builds with the production world.
/// </summary>
[Collection("OC-151 composition scope")]
public sealed class OcOfficerRosterCompositionTests
{
    [Fact]
    public void Officers_qa_refuses_while_unwired()
    {
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock());

        Assert.Equal(new[] { "officers: the roster is not wired." }, service.QaOfficersStatus());
        Assert.False(service.QaOfficersSpawn(2).Ok);
        Assert.False(service.QaOfficersClear().Ok);
    }

    [Fact]
    public void Officers_qa_flows_through_the_command_to_the_wired_roster()
    {
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock());
        service.OfficerRoster = new OcOfficerRosterService(new FakeOfficerWorld(), "wired");

        var spawned = Chapter3QaCommand.Execute(service, new[] { "officers", "spawn", "2" }, qaEnabled: true);

        Assert.Equal(new[] { "Chapter 3 QA officers spawn: 2 of 2 accepted (0 refused)." }, spawned);

        var status = Chapter3QaCommand.Execute(service, new[] { "officers", "status" }, qaEnabled: true);

        Assert.Contains(status, line => line.Contains("2/8", StringComparison.Ordinal));
        Assert.Contains(status, line => line.Contains("qa:0", StringComparison.Ordinal));

        var cleared = Chapter3QaCommand.Execute(service, new[] { "officers", "clear" }, qaEnabled: true);

        Assert.Equal(new[] { "Chapter 3 QA officers clear: released 2 slots." }, cleared);
        Assert.Equal(0, service.OfficerRoster.Count);
    }

    [Fact]
    public void Production_world_constructs_without_touching_native()
    {
        var world = new Il2CppOcOfficerWorld(_ => { });

        Assert.False(world.IsHost);
    }

    [Fact]
    public void Production_world_ops_fail_gracefully_without_a_game()
    {
        var world = new Il2CppOcOfficerWorld(_ => { });
        var clone = new OcOfficerCloneRef(new object(), 1L, 1);

        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadGameMinutes(out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryEnsureTemplate("id", out _, out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TrySpawnClone(new OcOfficerPosition(0f, 0f, 0f), "id", out _, out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadKnockedOut(clone, out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadClonePosition(clone, out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadBehaviourState(clone, out _, out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryRemoveClone(clone, out _));
        // Review fix: destroying the local template is ungated (pre-load runs before
        // the server is up), and with no template it is a clean no-op.
        Assert.Equal(OcOfficerWorldStatus.Ok, world.TryDestroyTemplate(out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadTemplateStatus(out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadNativeCounts(out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, world.TryReadPlayerPose(out _, out _));
    }

    [Fact]
    public void Production_world_rejects_a_non_checkpoint_without_a_game()
    {
        var world = new Il2CppOcOfficerWorld(_ => { });
        var clone = new OcOfficerCloneRef(new object(), 1L, 1);

        Assert.Equal(OcOfficerWorldStatus.Rejected, world.TryBindToCheckpoint(clone, new object(), null, out _));
        Assert.Equal(OcOfficerWorldStatus.Rejected, world.TryBindToCheckpoint(clone, null, null, out _));
    }

    [Fact]
    public void Composition_builds_the_roster_over_the_engine_officer_world()
    {
        using var scope = new CompositionScope();

        Assert.NotNull(scope.Root.OfficerRoster);
        Assert.Same(scope.Root.OfficerRoster, scope.Root.Chapter3!.OfficerRoster);
        Assert.Contains("step:Chapter3.Roster", scope.Engine.Trace);
    }

    [Fact]
    public void Production_adapters_build_the_Il2Cpp_officer_world()
    {
        var melon = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));

        Assert.Contains("new Il2CppOcOfficerWorld(", melon, StringComparison.Ordinal);
    }

    [Fact]
    public void Fanout_drives_the_roster_lifecycle_steps()
    {
        CompositionAssertions.AssertForRootRosterLifecycleNeedles();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "Mod.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new Release1StoryHostContextSnapshot(Guid.Empty, 1, "player", System.IO.Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository
    {
        public Chapter3StoreLoadResult Load() =>
            new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.");

        public Chapter3StoreUpdateResult Update(Chapter3State? state) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool IsEnvelopeSatisfied() => false;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = 0;
            return true;
        }
    }

    private sealed class FakeOfficerWorld : IOcOfficerWorld
    {
        private int _serial;

        public bool IsHost => true;

        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes)
        {
            minutes = 0d;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail)
        {
            status = new OcOfficerTemplateStatus(Present: true, Registered: true, PrefabId: 120, TemplateId: templateId);
            detail = "fake template";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail)
        {
            _serial++;
            clone = new OcOfficerCloneRef(new object(), _serial, _serial);
            detail = "fake spawned";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut)
        {
            knockedOut = false;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position)
        {
            position = new OcOfficerPosition(0f, 0f, 3f);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
        {
            activeBehaviour = "Checkpoint";
            checkpointActive = true;
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state)
        {
            state = new OcOfficerBoundState(true, true, OcCheckpointKind.Ours, null);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics)
        {
            diagnostics = new OcOfficerBehaviourDiagnostics(new[] { new OcOfficerBehaviourInfo("Checkpoint", 40, true, true) }, "Checkpoint", new OcOfficerCheckpointDetail(true, true, "Western", OcCheckpointKind.Ours));
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail)
        {
            detail = "fake bound";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        {
            detail = "fake removed";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
        {
            detail = "fake template destroyed";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryClearSwapMap(out string detail)
        {
            detail = "fake swap map cleared";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail)
        {
            detail = "fake swap entry cleared";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail)
        {
            leaked = false;
            detail = "fake no leak";
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
        {
            status = new OcOfficerTemplateStatus(Present: true, Registered: true, PrefabId: 120, TemplateId: "fake-template");
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts)
        {
            counts = new OcOfficerNativeCounts(Officers: 10, Registry: 110);
            return OcOfficerWorldStatus.Ok;
        }

        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees)
        {
            position = new OcOfficerPosition(0f, 0f, 0f);
            yawDegrees = 0f;
            return OcOfficerWorldStatus.Ok;
        }
    }
}
