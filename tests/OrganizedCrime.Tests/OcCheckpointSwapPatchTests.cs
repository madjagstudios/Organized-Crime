using HarmonyLib;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-141. The swap patch's native lookups live in an adapter the behavior
/// tests fake, so this file resolves every one of them against the referenced
/// f9 interop assemblies through the same AccessTools calls Apply uses, and
/// pins the Mod wiring that installs the patch. A wrong member name, a
/// reordered signature, or a missing Apply fails here, not in game.
/// </summary>
[Collection("OC-151 composition scope")]
public sealed class OcCheckpointSwapPatchTests
{
    [Fact]
    public void Composition_applies_the_swap_patch_once()
    {
        using var scope = new CompositionScope();

        Assert.Single(scope.Engine.Trace, entry => entry == "apply:CheckpointSwap");
    }

    [Fact]
    public void Production_adapters_apply_the_swap_patch_once()
    {
        Assert.Equal(1, Count(ReadMelonSource(), "OcCheckpointSwapPatch.Apply("));
    }

    [Fact]
    public void Activate_and_resume_overrides_resolve_with_void_signatures()
    {
        var activate = AccessTools.DeclaredMethod(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour), "Activate", Type.EmptyTypes);
        Assert.NotNull(activate);
        Assert.Equal(typeof(void), activate!.ReturnType);
        Assert.Equal(typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour), activate.DeclaringType);

        var resume = AccessTools.DeclaredMethod(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour), "Resume", Type.EmptyTypes);
        Assert.NotNull(resume);
        Assert.Equal(typeof(void), resume!.ReturnType);
        Assert.Equal(typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour), resume.DeclaringType);
    }

    [Fact]
    public void Swap_members_exist_with_the_shapes_the_patch_uses()
    {
        var npc = typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour).GetProperty("Npc");
        Assert.NotNull(npc);
        Assert.Equal(typeof(Il2CppScheduleOne.NPCs.NPC), npc!.PropertyType);

        var assigned = typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour).GetProperty("AssignedCheckpoint");
        Assert.NotNull(assigned);
        Assert.Equal(
            typeof(Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation), assigned!.PropertyType);

        var western = typeof(Il2CppScheduleOne.Law.CheckpointManager).GetProperty("WesternCheckpoint");
        Assert.NotNull(western);
        Assert.True(western!.CanRead);
        Assert.True(western.CanWrite);
        Assert.Equal(typeof(Il2CppScheduleOne.Police.RoadCheckpoint), western.PropertyType);

        Assert.Equal(0, (int)Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation.Western);
    }

    [Fact]
    public void Bind_sequence_members_exist_with_the_shapes_the_world_uses()
    {
        var setCheckpoint = AccessTools.Method(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour), "SetCheckpoint",
            new[] { typeof(Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation) });
        Assert.NotNull(setCheckpoint);
        Assert.Equal(typeof(void), setCheckpoint!.ReturnType);

        var enable = AccessTools.Method(typeof(Il2CppScheduleOne.NPCs.Behaviour.Behaviour), "Enable", Type.EmptyTypes);
        Assert.NotNull(enable);
        Assert.Equal(typeof(void), enable!.ReturnType);

        var disableNetworked = AccessTools.Method(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.Behaviour), "Disable_Networked",
            new[] { typeof(Il2CppFishNet.Connection.NetworkConnection) });
        Assert.NotNull(disableNetworked);
        Assert.Equal(typeof(void), disableNetworked!.ReturnType);

        Assert.NotNull(typeof(Il2CppScheduleOne.NPCs.Behaviour.ScheduleBehaviour));
        var stack = typeof(Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour).GetProperty("behaviourStack");
        Assert.NotNull(stack);
        var enabled = typeof(Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour).GetProperty("enabledBehaviours");
        Assert.NotNull(enabled);

        var standPoint = typeof(Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour).GetProperty("standPoint");
        Assert.NotNull(standPoint);
        Assert.Equal(typeof(UnityEngine.Transform), standPoint!.PropertyType);

        var priority = typeof(Il2CppScheduleOne.NPCs.Behaviour.Behaviour).GetProperty("Priority");
        Assert.NotNull(priority);
        Assert.Equal(typeof(int), priority!.PropertyType);

        var name = typeof(Il2CppScheduleOne.NPCs.Behaviour.Behaviour).GetProperty("Name");
        Assert.NotNull(name);
        Assert.Equal(typeof(string), name!.PropertyType);
    }

    [Fact]
    public void Checkpoint_dialogue_members_exist_with_the_shapes_the_world_uses()
    {
        // Native AssignToCheckpoint sets DialogueHandler's DialogueController
        // OverrideContainer to PoliceOfficer.CheckpointDialogue (f9 ISIL).
        var dialogue = typeof(Il2CppScheduleOne.Police.PoliceOfficer).GetProperty("CheckpointDialogue");
        Assert.NotNull(dialogue);
        var handler = typeof(Il2CppScheduleOne.NPCs.NPC).GetProperty("DialogueHandler");
        Assert.NotNull(handler);
        Assert.Equal(typeof(Il2CppScheduleOne.Dialogue.DialogueHandler), handler!.PropertyType);
        var overrideContainer = typeof(Il2CppScheduleOne.Dialogue.DialogueController).GetProperty("OverrideContainer");
        Assert.NotNull(overrideContainer);
        Assert.True(overrideContainer!.CanWrite);
        Assert.True(overrideContainer.PropertyType.IsAssignableFrom(dialogue!.PropertyType));
    }

    [Fact]
    public void Clone_wake_covers_the_voice_emitter_the_npc_resolves_in_children()
    {
        // NPC.GetAndValidateReferences resolves VoiceOverEmitter with an
        // active-only GetComponentInChildren<VOEmitter> (f9 ISIL), so the clone
        // wake must activate the VOEmitter type the property is declared as.
        var voice = typeof(Il2CppScheduleOne.NPCs.NPC).GetProperty("VoiceOverEmitter");
        Assert.NotNull(voice);
        Assert.Equal(typeof(Il2CppScheduleOne.VoiceOver.VOEmitter), voice!.PropertyType);
        Assert.True(typeof(UnityEngine.Component).IsAssignableFrom(voice.PropertyType));
    }

    [Fact]
    public void Round2_bind_unbind_and_leak_members_exist_with_the_shapes_the_world_uses()
    {
        // The bind ensures the OC checkpoint is enabled with stand points.
        var activation = typeof(Il2CppScheduleOne.Police.RoadCheckpoint).GetProperty("ActivationState");
        Assert.NotNull(activation);
        Assert.Equal(typeof(Il2CppScheduleOne.Police.RoadCheckpoint.ECheckpointState), activation!.PropertyType);
        Assert.True(activation.CanWrite);
        Assert.True(Enum.IsDefined(typeof(Il2CppScheduleOne.Police.RoadCheckpoint.ECheckpointState), "Enabled"));

        var stands = typeof(Il2CppScheduleOne.Police.RoadCheckpoint).GetProperty("StandPoints");
        Assert.NotNull(stands);
        Assert.Equal(
            typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform>),
            stands!.PropertyType);

        var applyState = AccessTools.Method(typeof(Il2CppScheduleOne.Police.RoadCheckpoint), "ApplyState", Type.EmptyTypes);
        Assert.NotNull(applyState);
        Assert.Equal(typeof(void), applyState!.ReturnType);

        // No direct schedule accessor: the bind scans the stack for one.
        Assert.Equal(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.Behaviour),
            typeof(Il2CppScheduleOne.NPCs.Behaviour.ScheduleBehaviour).BaseType);
        Assert.Null(typeof(Il2CppScheduleOne.Police.PoliceOfficer).GetProperty("ScheduleBehaviour"));

        // The leak check reads the asserted native's roster.
        var getCheckpoint = AccessTools.Method(
            typeof(Il2CppScheduleOne.Law.CheckpointManager),
            "GetCheckpoint",
            new[] { typeof(Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation) });
        Assert.NotNull(getCheckpoint);
        Assert.Equal(typeof(Il2CppScheduleOne.Police.RoadCheckpoint), getCheckpoint!.ReturnType);

        var docks = typeof(Il2CppScheduleOne.Law.CheckpointManager).GetProperty("DocksCheckpoint");
        Assert.NotNull(docks);
        Assert.Equal(typeof(Il2CppScheduleOne.Police.RoadCheckpoint), docks!.PropertyType);
        Assert.Equal(1, (int)Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation.Docks);

        var assigned = typeof(Il2CppScheduleOne.Police.RoadCheckpoint).GetProperty("AssignedNPCs");
        Assert.NotNull(assigned);
        Assert.Equal(
            typeof(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC>),
            assigned!.PropertyType);

        // The clone's officer IS its NPC: one native object, one pointer.
        Assert.Equal(
            typeof(Il2CppScheduleOne.NPCs.NPC),
            typeof(Il2CppScheduleOne.Police.PoliceOfficer).BaseType);
    }

    private static string ReadMelonSource() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "Mod.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root from the test output.");
    }
}
