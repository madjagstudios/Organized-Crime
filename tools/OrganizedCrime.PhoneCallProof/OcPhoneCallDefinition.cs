using Il2CppScheduleOne.ScriptableObjects;
using S1API.PhoneCalls;

namespace OrganizedCrime.PhoneCallProof;

public sealed class OcPhoneCallDefinition : PhoneCallDefinition
{
    public OcPhoneCallDefinition(string callerName, params string[] stages)
        : base(callerName)
    {
        if (string.IsNullOrWhiteSpace(callerName))
            throw new ArgumentException("Caller name is required.", nameof(callerName));
        ArgumentNullException.ThrowIfNull(stages);

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
