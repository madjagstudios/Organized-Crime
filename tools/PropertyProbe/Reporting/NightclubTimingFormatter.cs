using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class NightclubTimingFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(
        NightclubProbeEvidence evidence,
        NightclubProbeAssessment assessment)
    {
        var builder = new StringBuilder();
        builder.AppendLine("NIGHTCLUB DOOR AND MENU TIMING DIAGNOSTIC");
        builder.AppendLine("Observation only: no interaction was invoked, no Harmony patch was installed, and no vanilla world/save state was written.");
        builder.AppendLine("Temporary onInteractStart listener registration is the sole authorized runtime mutation and is removed during teardown.");
        builder.AppendLine("CAVEAT: Unity InstanceIds are session-local and are not durable identities across a restart.");
        builder.AppendLine("CAVEAT: NotObserved is not proof of absence within this bounded observation window.");
        builder.AppendLine("CAVEAT: an observed NPCSummonMenu is scene-global and not proven door-caused.");
        builder.AppendLine("CAVEAT: receipt files overwrite on a re-run; copy them before starting another run.");
        builder.AppendLine($"RUN_ID: {evidence.RunId}");
        builder.AppendLine($"SCENE: {evidence.SceneName}");
        builder.AppendLine($"AUTHORITY: {evidence.Authority}");
        builder.AppendLine($"EXPECTED_SHELL_PATH: {NightclubProbeContract.ExpectedShellPath}");
        builder.AppendLine($"DOOR_CANDIDATE_COUNT: {evidence.DoorCandidates.Count}");

        foreach (var candidate in evidence.DoorCandidates.OrderBy(candidate => candidate.HierarchyPath, StringComparer.Ordinal))
        {
            builder.AppendLine($"DOOR_PATH: {candidate.HierarchyPath}");
            builder.AppendLine($"DOOR_TYPE: {candidate.RuntimeType}");
            builder.AppendLine($"DOOR_STABLE_IDENTITY: {candidate.StableIdentity}");
            builder.AppendLine($"DOOR_INTERACTABLE_PATH: {candidate.InteractablePath}");
            builder.AppendLine($"DOOR_BUILDING_PATH: {candidate.BuildingPath}");
            builder.AppendLine($"DOOR_BUILDING_NAME: {candidate.BuildingName}");
            builder.AppendLine($"DOOR_ACCESS_POINT_PATH: {candidate.AccessPointPath}");
            builder.AppendLine($"DOOR_COMPONENT_TYPES: {string.Join(", ", candidate.ComponentTypes)}");
            builder.AppendLine($"DOOR_FIELD_AVAILABILITY: {string.Join(", ", candidate.FieldAvailability.Select(field => $"{field.Key}={field.Value}"))}");
        }

        builder.AppendLine($"MENU_OUTCOME: {evidence.MenuOutcome}");
        builder.AppendLine($"MENU_SURFACE_OBSERVED: {evidence.MenuSurfaceObserved}");
        builder.AppendLine($"DUPLICATE_INTERACTION_TESTED: {evidence.DuplicateInteractionTested}");
        builder.AppendLine($"DURATION_SECONDS: {evidence.DurationSeconds:0.###}");
        builder.AppendLine($"TEARDOWN_OBSERVED: {evidence.TeardownObserved}");
        builder.AppendLine($"EXACT_SHELL_VALIDATED: {evidence.ExactShellValidated}");
        builder.AppendLine("CALLBACK_ORDER: UNKNOWN (polling edges do not prove callback ownership or order)");
        builder.AppendLine($"VANILLA_INVARIANTS_UNCHANGED: {evidence.VanillaInvariantsUnchanged}");
        builder.AppendLine($"MUTATION_ATTEMPTED: {evidence.MutationAttempted}");
        builder.AppendLine($"HARMONY_USED: {evidence.HarmonyUsed}");
        builder.AppendLine($"SCENE_CHANGED: {evidence.SceneChanged}");
        builder.AppendLine($"DECISION: {assessment.Decision.ToString().ToUpperInvariant()}");
        foreach (var reason in assessment.Reasons)
            builder.AppendLine($"REASON: {reason}");

        builder.AppendLine();
        builder.AppendLine("OBSERVATIONS:");
        foreach (var observation in evidence.Observations.OrderBy(observation => observation.TimestampSeconds))
            builder.AppendLine($"  {observation.TimestampSeconds:0.###}s {observation.EventKind} subject={observation.SubjectPath} detail={observation.Detail}");

        builder.AppendLine();
        builder.AppendLine("VANILLA_BEFORE:");
        AppendVanilla(builder, evidence.VanillaBefore);
        builder.AppendLine("VANILLA_AFTER:");
        AppendVanilla(builder, evidence.VanillaAfter);
        return builder.ToString();
    }

    public static string FormatJson(
        NightclubProbeEvidence evidence,
        NightclubProbeAssessment assessment) =>
        JsonSerializer.Serialize(new { evidence, assessment }, JsonOptions);

    private static void AppendVanilla(StringBuilder builder, NightclubVanillaFingerprint fingerprint)
    {
        builder.AppendLine($"  DOOR_STATE: {fingerprint.DoorState}");
        builder.AppendLine($"  INTERACTABLE_STATE: {fingerprint.InteractableState}");
        builder.AppendLine($"  BUILDING_STATE: {fingerprint.BuildingState}");
        builder.AppendLine($"  ACCESS_POINT_STATE: {fingerprint.AccessPointState}");
        builder.AppendLine($"  MENU_STATE: {fingerprint.MenuState}");
        builder.AppendLine($"  NPC_STATE: {fingerprint.NpcState}");
    }
}
