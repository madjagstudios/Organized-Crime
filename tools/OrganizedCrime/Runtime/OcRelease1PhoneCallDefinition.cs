using Il2CppScheduleOne.ScriptableObjects;
using S1API.PhoneCalls;

namespace OrganizedCrime.Runtime;

public sealed class OcRelease1PhoneCallDefinition : PhoneCallDefinition
{
    public OcRelease1PhoneCallDefinition(string callerName, params string[] stages)
        : base(callerName)
    {
        if (string.IsNullOrWhiteSpace(callerName))
            throw new ArgumentException("Caller name is required.", nameof(callerName));
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Length == 0)
            throw new ArgumentException("At least one phone stage is required.", nameof(stages));

        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage))
                throw new ArgumentException("Stage text is required.", nameof(stages));
            AddStage(stage);
        }
    }

    public PhoneCallData Data => S1PhoneCallData;

    public string CallerName => Data.CallerID?.Name ?? string.Empty;

    public IReadOnlyList<string> StageTexts =>
        Data.Stages is null
            ? Array.Empty<string>()
            : Data.Stages.Select(stage => stage?.Text ?? string.Empty).ToArray();
}
