using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyCandidateRanker
{
    public static PropertyCapabilityScore Score(PropertySnapshot property)
    {
        var reasons = new List<string>();

        var safehouse = 20;
        if (property.HasContentsContainer) safehouse += 25;
        if (property.HasBoundingBox) safehouse += 20;
        if (property.GridCount > 0) safehouse += 15;
        if (property.EmployeeCapacity > 0) safehouse += 10;
        if (property.LoadingDockCount == 0) safehouse += 10;

        var warehouse = 0;
        if (property.HasContentsContainer) warehouse += 20;
        if (property.GridCount > 0) warehouse += Math.Min(20, property.GridCount * 10);
        if (property.EmployeeCapacity > 0) warehouse += Math.Min(20, property.EmployeeCapacity * 2);
        if (property.EmployeeIdlePointCount >= property.EmployeeCapacity && property.EmployeeCapacity > 0) warehouse += 10;
        if (property.HasNpcSpawnPoint) warehouse += 5;
        if (property.HasEmployeeContainer) warehouse += 5;
        if (property.ConfigurableCount > 0) warehouse += Math.Min(10, property.ConfigurableCount * 2);
        if (property.LoadingDockCount > 0) warehouse += Math.Min(20, property.LoadingDockCount * 10);

        var dock = 0;
        if (property.LoadingDockCount > 0)
        {
            dock += 45;
            dock += Math.Min(20, (property.LoadingDockCount - 1) * 10);
            reasons.Add($"{property.LoadingDockCount} loading dock(s)");
        }
        if (property.HasContentsContainer) dock += 10;
        if (property.GridCount > 0) dock += 10;
        if (property.EmployeeCapacity > 0) dock += 10;
        if (property.HasBoundingBox) dock += 5;

        var casino = 10;
        if (property.RuntimeType.Contains("Business", StringComparison.OrdinalIgnoreCase))
        {
            casino += 45;
            reasons.Add("runtime type is Business");
        }
        if (property.HasContentsContainer) casino += 10;
        if (property.ConfigurableCount > 0) casino += 10;
        if (property.HasBoundingBox) casino += 10;
        if (property.EmployeeCapacity > 0) casino += 10;

        if (property.EmployeeCapacity > 0)
            reasons.Add($"employee capacity {property.EmployeeCapacity}");
        if (property.GridCount > 0)
            reasons.Add($"{property.GridCount} build grid(s)");
        if (property.HasContentsContainer)
            reasons.Add("has property contents container");

        return new PropertyCapabilityScore(
            Safehouse: Clamp(safehouse),
            Warehouse: Clamp(warehouse),
            Dock: Clamp(dock),
            Casino: Clamp(casino),
            Reasons: reasons);
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
