using System.Reflection;

namespace OrganizedCrime.QuestLifecycleProof;

public sealed record QuestPresentationSnapshot(
    string? Title,
    string? QuestState,
    IReadOnlyList<string>? ObjectiveStates)
{
    public string? SoleObjectiveState => ObjectiveStates?.Count == 1
        ? ObjectiveStates[0]
        : null;
}

public static class QuestPresentationObserver
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    public static QuestPresentationSnapshot Observe(object? wrapperQuest) =>
        new(
            ReadPropertyString(wrapperQuest, "Title"),
            ReadPropertyString(wrapperQuest, "QuestState"),
            ReadNativeObjectiveStates(wrapperQuest));

    public static object? ReadNativeQuest(object? wrapperQuest) =>
        ReadFieldValue(wrapperQuest, "S1Quest");

    private static IReadOnlyList<string>? ReadNativeObjectiveStates(object? wrapperQuest)
    {
        try
        {
            var nativeQuest = ReadNativeQuest(wrapperQuest);
            var entries = BoundedReflectionCollectionReader.Read(
                ReadPropertyValue(nativeQuest, "Entries"));
            if (entries is null)
                return null;

            var states = new List<string>();
            foreach (var entry in entries)
            {
                var state = ReadPropertyString(entry, "State");
                if (state is null)
                    return null;
                states.Add(state);
            }

            return states;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadPropertyString(object? instance, string propertyName)
    {
        try
        {
            var value = ReadPropertyValue(instance, propertyName);
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadPropertyValue(object? instance, string propertyName)
    {
        if (instance is null)
            return null;

        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty(propertyName, DeclaredInstanceMembers);
            if (property is not null)
                return property.GetValue(instance);
        }

        return null;
    }

    private static object? ReadFieldValue(object? instance, string fieldName)
    {
        if (instance is null)
            return null;

        try
        {
            for (var type = instance.GetType(); type is not null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, DeclaredInstanceMembers);
                if (field is not null)
                    return field.GetValue(instance);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
