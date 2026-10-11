using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-130 structural pins: Arthur ships as a physical custom NPC with
/// no combat anywhere near him, and the proof harness stays free of
/// engine and S1API references so it remains test linkable.
/// </summary>
[Collection("OC-151 composition scope")]
public sealed class Chapter3ArthurReachabilityTests
{
    [Fact]
    public void Arthur_npc_uses_the_shipping_id_and_declares_itself_physical()
    {
        var root = FindRepositoryRoot();
        var arthur = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3ArthurNpc.cs"));

        Assert.Contains("public sealed class Chapter3ArthurNpc : NPC", arthur, StringComparison.Ordinal);
        Assert.Contains("public override bool IsPhysical => true;", arthur, StringComparison.Ordinal);
        Assert.Contains("protected override void ConfigurePrefab(NPCPrefabBuilder builder)", arthur, StringComparison.Ordinal);
        Assert.Contains("Chapter3ArthurIds.NpcId, Chapter3ArthurIds.FirstName, Chapter3ArthurIds.LastName", arthur, StringComparison.Ordinal);
        Assert.Contains("Chapter3ArthurAppearance.ApplyTo(builder)", arthur, StringComparison.Ordinal);
        Assert.DoesNotContain("OC_OWNER_SPIKES", arthur, StringComparison.Ordinal);

        // WithSpawnPosition writes a per type static dictionary with no
        // instance in scope, so it can never place Arthur relative to
        // the player; placement happens on the instance at come time.
        // Nell's contact identity is load bearing for shipped missions.
        foreach (var forbidden in new[] { "WithSpawnPosition", "Release1NellNpc", "oc_release1_nell", "oc_release1_arthur_spike" })
            Assert.DoesNotContain(forbidden, arthur, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_identity_matches_the_release_1_caller_spelling()
    {
        var root = FindRepositoryRoot();
        var contracts = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3ArthurContracts.cs"));

        Assert.Contains("public const string NpcId = \"oc_chapter3_arthur\";", contracts, StringComparison.Ordinal);
        Assert.Contains("public const string FirstName = \"Arthur\";", contracts, StringComparison.Ordinal);
        Assert.Contains("public const string LastName = \"Selby\";", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_look_is_defined_at_prefab_time_with_every_expected_path()
    {
        var root = FindRepositoryRoot();
        var look = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3ArthurAppearance.cs"));

        Assert.Contains("builder.WithAppearanceDefaults(appearance =>", look, StringComparison.Ordinal);
        Assert.Contains("appearance.HairPath = HairStyle.BowlCut;", look, StringComparison.Ordinal);
        // The eight S1API constant entries, kept in S1API's own convention.
        foreach (var constant in new[]
                 {
                     "HairStyle.BowlCut", "Face.Agitated", "Head.FlatCap", "Chest.Blazer",
                     "Feet.DressShoes", "Waist.Belt", "Neck.GoldChain", "FacialHairAccessory.Chevron"
                 })
            Assert.Contains(constant, look, StringComparison.Ordinal);
        // The three corrected bare layer paths, matching the game
        // resource container listing exactly. The chevron is not bare:
        // a lowercase accessory resolves but never renders.
        foreach (var path in new[]
                 {
                     "\"avatar/layers/face/oldpersonwrinkles\"",
                     "\"avatar/layers/top/buttonup\"",
                     "\"avatar/layers/bottom/jeans\""
                 })
            Assert.Contains(path, look, StringComparison.Ordinal);
        Assert.DoesNotContain("\"avatar/accessories/facialhair/chevron/chevron\"", look, StringComparison.Ordinal);
        Assert.Contains("public const string ChevronPath = FacialHairAccessory.Chevron;", look, StringComparison.Ordinal);
        // The categories the export got wrong, corrected.
        Assert.Contains("appearance.WithFaceLayer(OldPersonWrinklesPath,", look, StringComparison.Ordinal);
        Assert.Contains("appearance.WithBodyLayer(ButtonUpPath,", look, StringComparison.Ordinal);
        Assert.Contains("appearance.WithBodyLayer(JeansPath,", look, StringComparison.Ordinal);
        Assert.Contains("appearance.WithAccessoryLayer<FacialHairAccessory>(ChevronPath,", look, StringComparison.Ordinal);
        Assert.DoesNotContain("eyeshadow", look, StringComparison.Ordinal);
        // Every path is also listed once for the runtime verifier.
        Assert.Contains("public static readonly IReadOnlyList<string> ExpectedPaths", look, StringComparison.Ordinal);
    }

    [Fact]
    public void No_runtime_donor_clone_is_called_any_more()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var qaDir = Path.Combine(runtimeDir, "Qa");
        foreach (var file in new[]
                 {
                     "S1ApiChapter3ArthurRuntime.cs", "Chapter3ArthurVisitPump.cs",
                     "Chapter3ArthurContracts.cs", "Chapter3ArthurHarness.cs", "Chapter3ArthurNpc.cs"
                 })
        {
            var dir = file == "Chapter3ArthurHarness.cs" ? qaDir : runtimeDir;
            var text = File.ReadAllText(Path.Combine(dir, file));
            foreach (var forbidden in new[]
                     {
                         "TryApplyDonorAppearance", "ResolveDonor", "IsLiveDonor", "DonorNames",
                         "ApplyNakedAppearance", "ApplyWornObject", "RemoveOutfit", "_wornObjects",
                         "donor appearance", "appearance donor", "DonorId", "NPCRegistry", "GetNPC("
                     })
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }

        var runtime = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));
        Assert.Contains("TryVerifyAppearancePaths", runtime, StringComparison.Ordinal);
        Assert.Contains("TryGetAvatarObjectById", runtime, StringComparison.Ordinal);
        var pump = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3ArthurVisitPump.cs"));
        Assert.Contains("world.TryVerifyAppearancePaths(out var verifyReason)", pump, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_spawn_gate_uses_fishnet_and_touches_nothing_pending()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));

        // The spawned signal is FishNet's own NetworkObject.IsSpawned.
        Assert.Contains("using NativeNetworkObject = Il2CppFishNet.Object.NetworkObject;", runtime, StringComparison.Ordinal);
        Assert.Contains("GetComponent<NativeNetworkObject>()", runtime, StringComparison.Ordinal);
        Assert.Contains("netObject.IsSpawned", runtime, StringComparison.Ordinal);

        // The pre-spawn move warps and nothing else: no deactivate, no
        // visibility write, no destroy.
        var move = Slice(runtime, "public Chapter3ArthurMutationStatus TryMoveOutOfSight", "public Chapter3ArthurMutationStatus TryParkArthur");
        Assert.Contains("contact.Position = new Vector3(ParkingPoint.X, ParkingPoint.Y, ParkingPoint.Z);", move, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "SetActive(", "SetVisible(", "Destroy(", "Movement?.Stop", "movement.Stop" })
            Assert.DoesNotContain(forbidden, move, StringComparison.Ordinal);

        // Come, say, leave, face, the HQ place, the HQ face, the walk re-issue,
        // and the OC-135 animation trigger
        // refuse a pending spawn with the shared line; park names its
        // own refusal.
        Assert.Equal(8, Count(runtime, "\"Arthur isn't spawned yet.\""));
        Assert.Contains("Arthur isn't spawned yet; refusing to park a pending spawn.", runtime, StringComparison.Ordinal);

        // Dedupe only ever destroys spawned instances.
        var dedupe = Slice(runtime, "public Chapter3ArthurMutationStatus TryDedupeArthur", "public void Dispose()");
        Assert.Contains("matches.Where(IsLiveAndSpawned)", dedupe, StringComparison.Ordinal);
        Assert.Contains("none spawned yet; leaving them untouched.", dedupe, StringComparison.Ordinal);
        Assert.Contains("still pending.", dedupe, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_runtime_has_no_combat_damage_or_aggression_anywhere()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));

        foreach (var forbidden in new[]
                 {
                     "SetAndAttackTarget", "SetTarget", ".Attack()", "EndCombat",
                     "RequestGameSave", ".Damage(", ".Kill(", ".KnockOut(", ".Heal(", ".Revive(", ".RestoreHealth(",
                     "Aggressiveness", "GiveUpRange", "GiveUpTime",
                     "DefaultWeaponAssetPath", "SetCurrentWeapon", "CombatBehaviour",
                     "Il2CppScheduleOne.Combat", "HarmonyPatch", "GUI."
                 })
            Assert.DoesNotContain(forbidden, runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_park_come_and_hq_place_force_visible()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));

        Assert.Equal(1, Count(runtime, "SetVisible(true, networked: true)"));
        Assert.Equal(1, Count(runtime, "SetVisible(false, networked: true)"));
        Assert.Equal(4, Count(runtime, "SetVisible("));
        Assert.Contains("GetComponent<NativeNpc>()", runtime, StringComparison.Ordinal);
        Assert.Contains("ParkingPoint = (0f, -10000f, 0f)", runtime, StringComparison.Ordinal);
        Assert.Contains("NavMesh.SamplePosition(", runtime, StringComparison.Ordinal);
        Assert.Contains("Chapter3ArthurPlacement.SelectComeCandidate(", runtime, StringComparison.Ordinal);
        Assert.Contains("Chapter3ArthurPlacement.SelectLeaveCandidate(", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_say_uses_the_world_text_surface()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));

        Assert.Equal(1, Count(runtime, "ShowWorldText(text, SaySeconds)"));
        Assert.DoesNotContain("SendTextMessage", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Arthur_harness_and_pump_hold_no_unity_and_no_s1api_reference()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var qaDir = Path.Combine(runtimeDir, "Qa");
        foreach (var file in new[] { "Chapter3ArthurHarness.cs", "Chapter3ArthurVisitPump.cs", "Chapter3ArthurPlacement.cs", "Chapter3ArthurContracts.cs", "Chapter3ArthurHqPresence.cs", "Chapter3ArthurHqPlacer.cs" })
        {
            var dir = file == "Chapter3ArthurHarness.cs" ? qaDir : runtimeDir;
            var text = File.ReadAllText(Path.Combine(dir, file));
            foreach (var forbidden in new[]
                     { "UnityEngine", "S1API", "Il2Cpp", "MelonLoader", "Vector3", "HarmonyPatch", "GUI.", "RequestGameSave" })
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Arthur_qa_wiring_runs_through_the_chapter_3_service()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3RuntimeService.cs"));
        var melon = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));
        var fanout = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs"));

        Assert.Contains("PollArthurVisit()", service, StringComparison.Ordinal);
        Assert.Contains("_ctx.ArthurPump.BeginAfterLoad();", service, StringComparison.Ordinal);
        Assert.Contains("new S1ApiChapter3ArthurRuntime(warn, hasAuthority)", melon, StringComparison.Ordinal);
        Assert.Contains("Chapter3?.PollArthurVisit()", fanout, StringComparison.Ordinal);
        Assert.Contains("((S1ApiChapter3ArthurRuntime)world).Dispose()", melon, StringComparison.Ordinal);

        using var scope = new CompositionScope();
        scope.Root.Teardown();
        Assert.Contains("dispose:Arthur", scope.Engine.Trace);
    }

    [Fact]
    public void Arthur_hq_path_never_warps_and_restores_before_walking()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));

        // The HQ place delegates to the placer: no Position setter
        // (a Movement warp) and no warp anywhere on that path.
        var place = Slice(runtime, "public Chapter3ArthurMutationStatus TryPlaceAtHq", "public Chapter3ArthurMutationStatus TryResumeWalk");
        Assert.Contains("Chapter3ArthurHqPlacer.TryPlace(", place, StringComparison.Ordinal);
        Assert.DoesNotContain("contact.Position", place, StringComparison.Ordinal);
        Assert.DoesNotContain(".Warp(", place, StringComparison.Ordinal);

        // The HQ face delegates to the direct face: no agent FacePoint.
        var faceHq = Slice(runtime, "public Chapter3ArthurMutationStatus TryFacePlayerAtHq", "public Chapter3ArthurMutationStatus TryPlaceAtHq");
        Assert.Contains("Chapter3ArthurHqPlacer.TryFacePlayer(this, out reason)", faceHq, StringComparison.Ordinal);
        Assert.DoesNotContain("FacePoint(", faceHq, StringComparison.Ordinal);

        // The seam: stop-then-disable through the native movement's
        // own switch, gravity held through its multiplier, direct
        // transform sets, direct yaw facing with no FacePoint, and an
        // agent-state read-back.
        var agent = Slice(runtime, "IChapter3ArthurHqBody.TrySetAgentEnabled", "IChapter3ArthurHqBody.TrySetPhysicsHeld");
        Assert.Contains("contact!.Movement?.Stop()", agent, StringComparison.Ordinal);
        Assert.Contains("movement!.SetAgentEnabled(enabled)", agent, StringComparison.Ordinal);
        Assert.Contains("GetComponentsInChildren<NavMeshAgent>(true)", agent, StringComparison.Ordinal);
        Assert.Contains("movement._agent", agent, StringComparison.Ordinal);
        Assert.Contains("using NativeNpcMovement = Il2CppScheduleOne.NPCs.NPCMovement;", runtime, StringComparison.Ordinal);
        var physics = Slice(runtime, "IChapter3ArthurHqBody.TrySetPhysicsHeld", "IChapter3ArthurHqBody.TrySetTransform");
        Assert.Contains("movement!.SetGravityMultiplier(held ? 0f : 1f)", physics, StringComparison.Ordinal);
        var transform = Slice(runtime, "IChapter3ArthurHqBody.TrySetTransform", "IChapter3ArthurHqBody.TrySetVisible");
        Assert.Contains("body!.transform.position = new Vector3(position.X, position.Y, position.Z)", transform, StringComparison.Ordinal);
        Assert.Contains("Quaternion.Euler(0f, yawDegrees, 0f)", transform, StringComparison.Ordinal);
        var faceDirect = Slice(runtime, "IChapter3ArthurHqBody.TryFacePlayerDirect", "IChapter3ArthurHqBody.TryReadPlacement");
        Assert.Contains("MathF.Atan2(offset.x, offset.z)", faceDirect, StringComparison.Ordinal);
        Assert.DoesNotContain("FacePoint(", faceDirect, StringComparison.Ordinal);
        var read = Slice(runtime, "IChapter3ArthurHqBody.TryReadPlacement", "private void RestoreMovementForWalk");
        Assert.Contains("body.GetComponentsInChildren<NavMeshAgent>(true)", read, StringComparison.Ordinal);
        Assert.Contains("var anyOn = false;", read, StringComparison.Ordinal);

        // Leaving the HQ restores the agent before any warp or Goto.
        var park = Slice(runtime, "public Chapter3ArthurMutationStatus TryParkArthur", "public Chapter3ArthurMutationStatus TryVerifyAppearancePaths");
        Assert.True(
            park.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) >= 0 &&
            park.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) < park.IndexOf("contact.Position = new Vector3(ParkingPoint", StringComparison.Ordinal),
            "park restores movement before the warp");
        var come = Slice(runtime, "public Chapter3ArthurMutationStatus TryComeArthur", "public Chapter3ArthurMutationStatus TrySayArthur");
        Assert.True(
            come.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) >= 0 &&
            come.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) < come.IndexOf("contact.Goto(", StringComparison.Ordinal),
            "come restores movement before the Goto");
        var leave = Slice(runtime, "public Chapter3ArthurMutationStatus TryLeaveArthur", "public Chapter3ArthurMutationStatus TryFacePlayer");
        Assert.True(
            leave.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) >= 0 &&
            leave.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) < leave.IndexOf("contact.Goto(", StringComparison.Ordinal),
            "leave restores movement before the Goto");
        var resume = Slice(runtime, "public Chapter3ArthurMutationStatus TryResumeWalk", "IChapter3ArthurHqBody.TryActivateBody");
        Assert.True(
            resume.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) >= 0 &&
            resume.IndexOf("RestoreMovementForWalk()", StringComparison.Ordinal) < resume.IndexOf("contact.Goto(", StringComparison.Ordinal),
            "resume restores movement before the Goto");
        var contracts = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3ArthurContracts.cs"));
        // The hq here tuning path: capture converts the player into
        // the local frame, refuses outside the HQ, stores a session
        // override the place consults, and the QA dispatch routes here.
        Assert.Contains("TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)", runtime, StringComparison.Ordinal);
        Assert.Contains("TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason);", contracts, StringComparison.Ordinal);
        Assert.Contains("_hqSpotOverride", runtime, StringComparison.Ordinal);
        Assert.Contains("Chapter3ArthurHqSpot.ToLocal(", runtime, StringComparison.Ordinal);
        Assert.Contains("Chapter3ArthurHqSpot.IsInsideFrame(captured)", runtime, StringComparison.Ordinal);
        Assert.Contains("the player is not inside the HQ.", runtime, StringComparison.Ordinal);
        var service = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3RuntimeService.cs"));
        var qa = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3QaCommand.cs"));
        Assert.Contains("QaArthurHqHere()", service, StringComparison.Ordinal);
        Assert.Contains("QaArthurHqHere();", qa, StringComparison.Ordinal);
        Assert.Contains("hq <on | off | auto | here>", qa, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TryMoveOutOfSight", "TryParkArthur", "NPC.Get(")]
    [InlineData("TryParkArthur", "TryVerifyAppearancePaths", "NPC.Get(")]
    [InlineData("TryDedupeArthur", "private static bool IsSpawned", "NPC.All")]
    public void Destructive_adapter_entry_points_reject_clients_before_native_access(
        string method, string nextMethod, string nativeAccess)
    {
        var runtime = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "S1ApiChapter3ArthurRuntime.cs"));
        var start = "public Chapter3ArthurMutationStatus " + method;
        var end = nextMethod.StartsWith("private", StringComparison.Ordinal)
            ? nextMethod
            : "public Chapter3ArthurMutationStatus " + nextMethod;
        var body = Slice(runtime, start, end);
        var authorityGuard = body.IndexOf("if (!HasAuthority)", StringComparison.Ordinal);

        Assert.True(authorityGuard >= 0 && authorityGuard < body.IndexOf(nativeAccess, StringComparison.Ordinal));
        Assert.Contains("return Chapter3ArthurMutationStatus.Rejected;", body, StringComparison.Ordinal);
    }

    private static string Slice(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"slice {from}..{to} not found");
        return text.Substring(start, end - start);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
