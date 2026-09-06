using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryScenarioTests
{
    private const string Player = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Clean_path_survives_restart_without_duplicate_awards()
    {
        using var folder = TempFolder.Create();
        Assert.True(Release1StorySavePath.TryCreate(folder.Path, out var path, out _));
        var store = new Release1StoryStateStore(path!);
        var first = NewService(store);
        Assert.Equal(Release1StoryCommandStatus.Accepted, first.TryExecute(Intro()).Status);
        CompleteThrough(first, 3);
        first.OnSaveStart(); first.OnSaveComplete(); first.Dispose();

        var second = NewService(store);
        CompleteThrough(second, 6);
        second.OnSaveStart(); second.OnSaveComplete();
        var restored = NewService(store);
        Assert.Equal(80, restored.State!.Standing);
        Assert.True(restored.State.Release1Recognized);
        Assert.All(restored.State.Missions, mission => Assert.Equal(Release1MissionState.Satisfied, mission.State));
        Assert.Equal(6, restored.State.Missions.Count(mission => mission.RewardAuthorizationReceiptId is not null));
    }

    [Fact]
    public void Recovery_and_prepared_reward_survive_restart_without_replay()
    {
        using var folder = TempFolder.Create();
        Assert.True(Release1StorySavePath.TryCreate(folder.Path, out var path, out _));
        var store = new Release1StoryStateStore(path!);
        var service = NewService(store);
        service.TryExecute(Intro());
        var mission = service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "accept", terms: "v1")).Status);
        mission = service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, "activate")).Status);
        mission = service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.RequiredFailure, "required-failure")).Status);
        mission = service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MakeGoodAccepted, "make-good-accept")).Status);
        mission = service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MakeGoodFailed, "make-good-failure")).Status);
        mission = service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.RecoveryAccepted, "recovery-accept")).Status);
        mission = service.State!.Missions[0];
        var effect = new Release1NativeEffectJournalEntry("effect-reward", mission.MissionKey, mission.Attempt, "Reward", "oc", "native", "cargo", Release1NativeEffectPhase.Prepared, null, 0);
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, "recovery-complete", timing: Release1CompletionTiming.OnTime, reward: "reward-auth", effect: effect)).Status);
        service.OnSaveStart(); service.OnSaveComplete(); service.Dispose();

        var restored = NewService(store);
        Assert.Equal(Release1MissionState.Satisfied, restored.State!.Missions[0].State);
        Assert.Equal(0, restored.State.Missions[0].StandingPenaltyApplied);
        Assert.Single(restored.State.NativeEffects, e => e.Phase == Release1NativeEffectPhase.Prepared);
        var replay = restored.TryExecute(Command(restored.State.Missions[0], Release1TransitionKind.MissionCompleted, "recovery-complete", timing: Release1CompletionTiming.OnTime, reward: "reward-auth"));
        Assert.NotEqual(Release1StoryCommandStatus.Accepted, replay.Status);
    }

    private static Release1StoryRuntimeService NewService(Release1StoryStateStore store)
    {
        var context = new Context(store.SavePath.ActiveSaveFolder);
        var service = new Release1StoryRuntimeService(context, new Release1StoryStateStoreRepository(store));
        service.OnPreLoad(); service.OnLoadComplete();
        return service;
    }

    private static void CompleteThrough(Release1StoryRuntimeService service, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var mission = service.State!.Missions[i];
            if (mission.State == Release1MissionState.Satisfied) continue;
            Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, $"accept-{i}", terms: "v1")).Status);
            mission = service.State!.Missions[i];
            Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, $"activate-{i}")).Status);
            mission = service.State!.Missions[i];
            Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, $"complete-{i}", timing: Release1CompletionTiming.OnTime, reward: $"reward-{i}", recognition: i == 5 ? "recognize-6" : null)).Status);
        }
    }

    private static Release1StoryCommand Intro() => new(Session, 1, Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro", "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro");
    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, Release1CompletionTiming? timing = null, string? reward = null, string? recognition = null, Release1NativeEffectJournalEntry? effect = null) => new(Session, 1, Player, mission.MissionKey, mission.Attempt, kind, receipt, Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, kind, receipt).Value, terms, null, null, timing, reward, null, recognition, effect);

    private sealed class Context : IRelease1StoryHostContext
    {
        private readonly string _folder;
        public Context(string folder) => _folder = folder;
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = new(Session, 1, Player, _folder); return Release1StoryHostContextReadStatus.Ready; }
    }
    private sealed class TempFolder : IDisposable
    {
        private TempFolder(string path) => Path = path;
        public string Path { get; }
        public static TempFolder Create() { var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrganizedCrimeTests", "Release1Scenario", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return new(path); }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
