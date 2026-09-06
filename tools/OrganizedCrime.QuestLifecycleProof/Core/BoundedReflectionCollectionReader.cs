using System.Collections;
using System.Reflection;

namespace OrganizedCrime.QuestLifecycleProof;

internal static class BoundedReflectionCollectionReader
{
    private const int MaximumCount = 64;
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    public static IReadOnlyList<object>? Read(object? collection)
    {
        if (collection is null)
            return null;

        try
        {
            var countProperty = FindProperty(
                collection.GetType(),
                property => property.Name == "Count" && property.GetIndexParameters().Length == 0);
            var itemProperty = FindProperty(
                collection.GetType(),
                property =>
                    property.Name == "Item" &&
                    property.GetIndexParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(int));

            if (countProperty is not null || itemProperty is not null)
            {
                if (countProperty is null || itemProperty is null)
                    return null;

                if (countProperty.GetValue(collection) is not int count ||
                    count < 0 ||
                    count > MaximumCount)
                    return null;

                var items = new List<object>(count);
                for (var index = 0; index < count; index++)
                {
                    var item = itemProperty.GetValue(collection, [index]);
                    if (item is null)
                        return null;
                    items.Add(item);
                }

                return items;
            }

            if (collection is not IEnumerable enumerable)
                return null;

            var fallbackItems = new List<object>();
            foreach (var item in enumerable)
            {
                if (item is null || fallbackItems.Count == MaximumCount)
                    return null;
                fallbackItems.Add(item);
            }

            return fallbackItems;
        }
        catch
        {
            return null;
        }
    }

    private static PropertyInfo? FindProperty(
        Type type,
        Func<PropertyInfo, bool> predicate)
    {
        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            var property = candidate
                .GetProperties(DeclaredInstanceMembers)
                .FirstOrDefault(predicate);
            if (property is not null)
                return property;
        }

        return null;
    }
}
