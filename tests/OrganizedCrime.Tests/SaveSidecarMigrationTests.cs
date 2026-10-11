using System.Text.Json;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-177: historical backups preserve supported data and intended migration contracts.
public sealed class SaveSidecarMigrationTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var file in Directory.GetFiles(FixtureDirectory, "*.json").OrderBy(Path.GetFileName))
            yield return new object[] { Path.GetFileName(file) };
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Current_codec_migrates_defaults_round_trips_and_preserves_original_fields(string fixture)
    {
        var original = File.ReadAllText(Path.Combine(FixtureDirectory, fixture));
        var current = MigrateAndSerialize(fixture, original);
        var reloaded = MigrateAndSerialize(fixture, current);
        Assert.Equal(current, reloaded);

        using var before = JsonDocument.Parse(original);
        using var after = JsonDocument.Parse(current);
        var expectedSchema = fixture.StartsWith("chapter3-") ? 20
            : fixture.StartsWith("release1-story-") ? 14 : 2;
        Assert.Equal(expectedSchema, after.RootElement.GetProperty("schemaVersion").GetInt32());
        AssertNewFeatureDefaults(fixture, before.RootElement, after.RootElement);
        var retiredKtlo = fixture.StartsWith("release1-story-") &&
            before.RootElement.GetProperty("schemaVersion").GetInt32() < 10;
        if (retiredKtlo)
        {
            // Release1StorySaveCodec.TryDeserialize accepts the retired v9 shape;
            // FromDto empties both collections for the new v10 condition.
            Assert.Empty(after.RootElement.GetProperty("story").GetProperty("keepTheLightsOffAssignments").EnumerateArray());
            Assert.Empty(after.RootElement.GetProperty("story").GetProperty("keepTheLightsOffProgress").EnumerateArray());
        }
        AssertPreserved(before.RootElement, after.RootElement, "$", fixture, retiredKtlo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void V9_active_KTLO_restores_the_new_condition_and_completes_once(bool withEmployee)
    {
        // No real v9 backup is Active. This explicitly derived fixture rolls the sanitized
        // v9 sample back before KTLO completion, retaining its real legacy assignment,
        // timing, attempt and acceptance/activation correlations. See Fixtures/Migration/README.md.
        using var save = new MigrationSave("release1-story-v9-derived-active.json");
        var loaded = save.StoryRepository.Load();
        Assert.True(loaded.Succeeded, loaded.Message);
        Assert.Equal(Release1StoryStoreLoadStatus.Loaded, loaded.Status);
        var migrated = loaded.Envelope!.Story!;
        var originalMission = migrated.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        Assert.Equal(Release1MissionState.Active, originalMission.State);
        Assert.Empty(migrated.KeepTheLightsOffAssignments);
        Assert.Empty(migrated.KeepTheLightsOffProgress);

        using var story = save.LoadStory();
        var world = new Release1KeepTheLightsOffHarness.FakeWorld
        {
            Context = save.Context.Snapshot,
            TotalMinutes = originalMission.AcceptedGameTimeHours!.Value * 60d + 1d,
            Activity = Release1KeepTheLightsOffHarness.ProductionWorld(2, working: false, withEmployee: withEmployee)
        };
        using var mission = new Release1KeepTheLightsOffMissionService(story, world);
        mission.OnLoadComplete(); // Normal load-time reconcile; no QA restore command.

        var assignment = Assert.Single(story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Primary, assignment.Mode);
        Assert.Equal(originalMission.Attempt, assignment.Attempt);
        Assert.Equal(2, assignment.ExpectedOwnedPropertyCount);
        Assert.Equal(Release1KeepTheLightsOffAssignment.WindowGameMinutes, assignment.WindowDurationGameMinutes);
        Assert.Contains(assignment.AuthorizationCorrelationId, originalMission.AcceptedLogicalCorrelations);
        Assert.Equal(originalMission, story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)]);
        Assert.Equal(migrated.Standing, story.State.Standing);
        Assert.Equal(migrated.NativeEffects, story.State.NativeEffects);
        Assert.Equal(world.TotalMinutes, Assert.Single(story.State.KeepTheLightsOffProgress).ClearConfirmedAtGameMinutes);
        Assert.Equal(Release1StoryRuntimePhase.Active, story.Phase);
        SaveStory(story, mission);
        Assert.Single(save.StoryRepository.Load().Envelope!.Story!.KeepTheLightsOffAssignments);

        var cash = world.CashBalance;
        world.TotalMinutes += assignment.WindowDurationGameMinutes;
        mission.Update(); // New shutdown condition really completes through the ordinary pass.
        var completed = story.State!;
        Assert.Equal(Release1MissionState.Satisfied,
            completed.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)].State);
        Assert.Equal(migrated.Standing + 10, completed.Standing);
        Assert.Equal(migrated.NativeEffects, completed.NativeEffects);
        Assert.Equal(cash, world.CashBalance);
        Assert.Equal(0, world.CashChanges);
        mission.Update();
        Assert.Equal(completed.Revision, story.State!.Revision);

        // Reload through the disk store and reconcile again: no duplicated assignment,
        // completion, Standing gain or cash/native reward after an upgrade/save/reload.
        SaveStory(story, mission);
        using var reloaded = save.LoadStory();
        using var reloadedMission = new Release1KeepTheLightsOffMissionService(reloaded, world);
        reloadedMission.OnLoadComplete();
        reloadedMission.Update();
        Assert.Equal(completed.Standing, reloaded.State!.Standing);
        Assert.Equal(completed.Revision, reloaded.State.Revision);
        Assert.Single(reloaded.State.KeepTheLightsOffAssignments);
        Assert.Equal(completed.NativeEffects, reloaded.State.NativeEffects);
        Assert.Equal(0, world.CashChanges);
    }

    [Theory]
    [InlineData("release1-story-v9.json")]
    [InlineData("release1-story-v11-oc70-gate-run.json")]
    public void Release1_without_Chapter3_starts_at_the_normal_Envelope_day_trigger(string fixture)
    {
        using var save = new MigrationSave(fixture);
        Assert.False(File.Exists(save.ChapterPath.SidecarFilePath));
        Assert.Equal(Chapter3StoreLoadStatus.Empty, save.ChapterRepository.Load().Status);
        using var story = save.LoadStory();
        var gate = new Chapter3StoryGate(() => story.State);
        Assert.True(gate.IsEnvelopeSatisfied());
        var clock = new MigrationClock { ElapsedDays = 30 };
        using var chapter = new Chapter3RuntimeService(save.Context, save.ChapterRepository, gate, clock);
        chapter.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnLoadComplete().RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Active, chapter.Phase);
        Assert.Null(chapter.State!.StartElapsedDay);
        Assert.False(File.Exists(save.ChapterPath.SidecarFilePath));

        clock.RaiseDay(); // The same event subscription used by the mod, not TryQaStartNow.
        Assert.Equal(30, chapter.State.StartElapsedDay);
        Assert.Equal(30, chapter.State.LastProcessedElapsedDay);
        Assert.Equal(0, chapter.State.ChapterDayAt(30));
        Assert.NotNull(chapter.State.JobBoard.OpenOffer);
        var offer = chapter.State.JobBoard.OpenOffer!;
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnSaveStart().RejectReason);
        // Simulate the native save deleting OC sidecars, inside this isolated scratch save only.
        File.Delete(save.ChapterPath.SidecarFilePath);
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnSaveComplete().RejectReason);
        using var written = JsonDocument.Parse(File.ReadAllText(save.ChapterPath.SidecarFilePath));
        Assert.Equal(20, written.RootElement.GetProperty("schemaVersion").GetInt32());

        using var resumed = new Chapter3RuntimeService(save.Context, save.ChapterRepository, gate, clock);
        resumed.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, resumed.OnLoadComplete().RejectReason);
        Assert.Equal(30, resumed.State!.StartElapsedDay);
        Assert.Equal(offer.OfferId, resumed.State.JobBoard.OpenOffer!.OfferId);
        Assert.Equal(offer.Reward, resumed.State.JobBoard.OpenOffer.Reward);
        chapter.Dispose(); // Only the resumed service consumes the next clock event.
        clock.ElapsedDays = 31;
        clock.RaiseDay();
        Assert.Equal(30, resumed.State.StartElapsedDay);
        Assert.Equal(31, resumed.State.LastProcessedElapsedDay);
        Assert.Equal(1, resumed.State.ChapterDayAt(31));
    }

    [Fact]
    public void Release1_before_Envelope_does_not_start_Chapter3_on_a_day_event()
    {
        using var save = new MigrationSave("release1-story-v9-oc57-pre-callin.json");
        using var story = save.LoadStory();
        var gate = new Chapter3StoryGate(() => story.State);
        Assert.False(gate.IsEnvelopeSatisfied());
        var clock = new MigrationClock { ElapsedDays = 30 };
        using var chapter = new Chapter3RuntimeService(save.Context, save.ChapterRepository, gate, clock);
        chapter.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnLoadComplete().RejectReason);
        clock.RaiseDay();
        clock.ElapsedDays++;
        clock.RaiseDay();
        Assert.Null(chapter.State!.StartElapsedDay);
        Assert.Null(chapter.State.JobBoard.OpenOffer);
        Assert.False(File.Exists(save.ChapterPath.SidecarFilePath));
    }

    [Fact]
    public void V9_satisfied_KTLO_keeps_Standing_and_the_existing_reward_without_reissuing()
    {
        using var save = new MigrationSave("release1-story-v9.json");
        using var story = save.LoadStory();
        var original = story.State!;
        var world = new Release1KeepTheLightsOffHarness.FakeWorld
        {
            Context = save.Context.Snapshot,
            TotalMinutes = 22_000d,
            Activity = Release1KeepTheLightsOffHarness.ProductionWorld(2, working: false, withEmployee: true)
        };
        using var mission = new Release1KeepTheLightsOffMissionService(story, world);
        mission.OnLoadComplete();
        mission.Update();
        SaveStory(story, mission);
        using var resumed = save.LoadStory();
        using var resumedMission = new Release1KeepTheLightsOffMissionService(resumed, world);
        resumedMission.OnLoadComplete();
        resumedMission.Update();
        Assert.Equal(Release1MissionState.Satisfied,
            resumed.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)].State);
        Assert.Equal(original.Standing, resumed.State.Standing);
        Assert.Equal(original.Revision, resumed.State.Revision);
        Assert.Equal(original.NativeEffects, resumed.State.NativeEffects);
        Assert.Empty(resumed.State.KeepTheLightsOffAssignments);
        Assert.Empty(resumed.State.KeepTheLightsOffProgress);
        Assert.Equal(0, world.CashChanges);
    }

    [Fact]
    public void Real_schema15_Chapter3_resumes_to20_without_restarting_or_replaying_rewards()
    {
        using var save = new MigrationSave("release1-story-v14.json", "chapter3-v15.json");
        var loaded = save.ChapterRepository.Load();
        Assert.True(loaded.Succeeded, loaded.Message);
        Assert.Equal(Chapter3StoreLoadStatus.Loaded, loaded.Status);
        var migrated = loaded.Envelope!.State!;
        using var story = save.LoadStory();
        var gate = new Chapter3StoryGate(() => story.State);
        var clock = new MigrationClock { ElapsedDays = migrated.LastProcessedElapsedDay };
        using var chapter = new Chapter3RuntimeService(save.Context, save.ChapterRepository, gate, clock);
        chapter.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnLoadComplete().RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Active, chapter.Phase);
        Assert.Equal(SerializeChapter(migrated), SerializeChapter(chapter.State!));
        clock.RaiseDay();
        Assert.Equal(SerializeChapter(migrated), SerializeChapter(chapter.State!));
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnSaveStart().RejectReason);
        // Simulate the native save deleting OC sidecars, inside this isolated scratch save only.
        File.Delete(save.ChapterPath.SidecarFilePath);
        Assert.Equal(Chapter3RuntimeRejectReason.None, chapter.OnSaveComplete().RejectReason);
        var written = File.ReadAllText(save.ChapterPath.SidecarFilePath);
        using var json = JsonDocument.Parse(written);
        Assert.Equal(20, json.RootElement.GetProperty("schemaVersion").GetInt32());
        using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "chapter3-v15.json")));
        AssertPreserved(original.RootElement, json.RootElement, "$", "chapter3-v15.json", false);
        AssertNewFeatureDefaults("chapter3-v15.json", original.RootElement, json.RootElement);

        using var resumed = new Chapter3RuntimeService(save.Context, save.ChapterRepository, gate, clock);
        resumed.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, resumed.OnLoadComplete().RejectReason);
        Assert.Equal(SerializeChapter(migrated), SerializeChapter(resumed.State!));
        chapter.Dispose();
        clock.ElapsedDays++;
        clock.RaiseDay();
        Assert.Equal(migrated.StartElapsedDay, resumed.State!.StartElapsedDay);
        Assert.Equal(clock.ElapsedDays, resumed.State.LastProcessedElapsedDay);
        Assert.Equal(migrated.JobBoard.SuccessfulJobs, resumed.State.JobBoard.SuccessfulJobs);
        Assert.Equal(migrated.JobBoard.LastCompletedJob, resumed.State.JobBoard.LastCompletedJob);
        Assert.Equal(migrated.PresentationReceipts, resumed.State.PresentationReceipts);
    }

    private static void SaveStory(Release1StoryRuntimeService story, Release1KeepTheLightsOffMissionService mission)
    {
        Assert.Equal(Release1StoryRuntimeRejectReason.None, story.OnSaveStart().RejectReason);
        mission.OnSaveStart();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, story.OnSaveComplete().RejectReason);
        mission.OnSaveComplete();
    }

    private static string SerializeChapter(Chapter3State state)
    {
        Assert.True(Chapter3SaveCodec.TrySerialize(new Chapter3SaveEnvelope(20, state), out var json, out var result), result.Message);
        return json;
    }

    private sealed class MigrationSave : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "oc177-migration-" + Guid.NewGuid().ToString("N"));

        public MigrationSave(string storyFixture, string? chapterFixture = null)
        {
            Directory.CreateDirectory(_folder);
            Context = new MigrationContext(_folder);
            Assert.True(Release1StorySavePath.TryCreate(_folder, out var storyPath, out var storyResult), storyResult.Message);
            Assert.True(Chapter3SavePath.TryCreate(_folder, out var chapterPath, out var chapterResult), chapterResult.Message);
            ChapterPath = chapterPath!;
            Directory.CreateDirectory(storyPath!.SidecarDirectory);
            File.Copy(Path.Combine(FixtureDirectory, storyFixture), storyPath.SidecarFilePath);
            if (chapterFixture is not null)
                File.Copy(Path.Combine(FixtureDirectory, chapterFixture), ChapterPath.SidecarFilePath);
            StoryRepository = new(new Release1StoryStateStore(storyPath));
            ChapterRepository = new(new Chapter3StateStore(ChapterPath));
        }

        public MigrationContext Context { get; }
        public Chapter3SavePath ChapterPath { get; }
        public Release1StoryStateStoreRepository StoryRepository { get; }
        public Chapter3StateStoreRepository ChapterRepository { get; }

        public Release1StoryRuntimeService LoadStory()
        {
            var story = new Release1StoryRuntimeService(Context, StoryRepository);
            story.OnPreLoad();
            var loaded = story.OnLoadComplete();
            Assert.Equal(Release1StoryRuntimeRejectReason.None, loaded.RejectReason);
            Assert.Equal(Release1StoryRuntimePhase.Active, story.Phase);
            return story;
        }

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }

    private sealed class MigrationContext : IRelease1StoryHostContext
    {
        public MigrationContext(string folder) => Snapshot = new(Guid.NewGuid(), 1, "76561190000000001", folder);
        public Release1StoryHostContextSnapshot Snapshot { get; }
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class MigrationClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public event Action? DayPassed;
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int elapsedDays) { elapsedDays = ElapsedDays; return true; }
        public void RaiseDay() => DayPassed?.Invoke();
    }

    private static string MigrateAndSerialize(string fixture, string json)
    {
        if (fixture.StartsWith("chapter3-"))
        {
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var load),
                $"{fixture}: {load.Reason}: {load.Message}");
            Assert.True(Chapter3SaveCodec.TrySerialize(envelope, out var saved, out var write),
                $"{fixture}: {write.Reason}: {write.Message}");
            return saved;
        }
        if (fixture.StartsWith("release1-story-"))
        {
            Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var load),
                $"{fixture}: {load.Reason}: {load.Message}");
            Assert.True(Release1StorySaveCodec.TrySerialize(envelope, out var saved, out var write),
                $"{fixture}: {write.Reason}: {write.Message}");
            return saved;
        }
        if (fixture.StartsWith("local-pressure-"))
        {
            Assert.True(LocalPressureSaveCodec.TryDeserialize(json, out var envelope, out var load),
                $"{fixture}: {load.Reason}: {load.Message}");
            Assert.True(LocalPressureSaveCodec.TrySerialize(envelope, out var saved, out var write),
                $"{fixture}: {write.Reason}: {write.Message}");
            return saved;
        }
        Assert.StartsWith("fish-warehouse-", fixture);
        Assert.True(FishWarehouseSaveCodec.TryDeserialize(json, out var warehouse, out var failure),
            $"{fixture}: {failure}");
        return FishWarehouseSaveCodec.Serialize(warehouse);
    }

    private static void AssertNewFeatureDefaults(string fixture, JsonElement before, JsonElement after)
    {
        if (fixture.StartsWith("local-pressure-"))
        {
            foreach (var player in after.GetProperty("players").EnumerateArray())
                Assert.Equal(JsonValueKind.Null, player.GetProperty("lastAppliedRaidCoolingId").ValueKind);
        }
        if (!fixture.StartsWith("chapter3-")) return;
        var oldState = before.GetProperty("state");
        var state = after.GetProperty("state");
        foreach (var name in new[] { "sweep", "lastSweepStartMinutes", "raid", "lastRaidSuccessMinutes" })
            if (!oldState.TryGetProperty(name, out _))
                Assert.Equal(JsonValueKind.Null, state.GetProperty(name).ValueKind);
        foreach (var name in new[] { "openOffer", "activeJob", "lastCompletedJob" })
        {
            var oldJob = oldState.GetProperty("jobBoard").GetProperty(name);
            var job = state.GetProperty("jobBoard").GetProperty(name);
            if (oldJob.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal(JsonValueKind.Null, job.ValueKind);
                continue;
            }
            foreach (var feature in new[] { "storageRun", "cashMovement", "shutdown" })
                if (!oldJob.TryGetProperty(feature, out _) && job.TryGetProperty(feature, out var value))
                    Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    private static void AssertPreserved(JsonElement before, JsonElement after, string field, string fixture, bool retiredKtlo)
    {
        Assert.True(before.ValueKind == after.ValueKind,
            $"{fixture} {field}: expected {before.ValueKind}, migrated to {after.ValueKind}");
        switch (before.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in before.EnumerateObject())
                {
                    if (field == "$" && property.Name == "schemaVersion") continue;
                    if (retiredKtlo && field == "$.story" && property.Name is
                        ("keepTheLightsOffAssignments" or "keepTheLightsOffProgress")) continue;
                    Assert.True(after.TryGetProperty(property.Name, out var value),
                        $"{fixture} {field}.{property.Name}: existing field was lost");
                    AssertPreserved(property.Value, value, $"{field}.{property.Name}", fixture, retiredKtlo);
                }
                break;
            case JsonValueKind.Array:
                Assert.True(before.GetArrayLength() == after.GetArrayLength(),
                    $"{fixture} {field}: expected {before.GetArrayLength()} entries, migrated to {after.GetArrayLength()}");
                for (var i = 0; i < before.GetArrayLength(); i++)
                    AssertPreserved(before[i], after[i], $"{field}[{i}]", fixture, retiredKtlo);
                break;
            case JsonValueKind.Number:
                Assert.True(before.GetDecimal() == after.GetDecimal(),
                    $"{fixture} {field}: expected {before.GetRawText()}, migrated to {after.GetRawText()}");
                break;
            case JsonValueKind.String:
                Assert.True(before.GetString() == after.GetString(),
                    $"{fixture} {field}: existing string changed");
                break;
        }
    }

    private static string FixtureDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "tests", "OrganizedCrime.Tests", "Fixtures", "Migration");
        }
    }
}
