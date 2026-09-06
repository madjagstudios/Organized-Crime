using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using OrganizedCrime.PropertyProbe.Runtime;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyInventoryProbe
{
    private readonly IPropertyRuntimeAdapter _adapter;

    public PropertyInventoryProbe(IPropertyRuntimeAdapter adapter)
    {
        _adapter = adapter;
    }

    public IReadOnlyList<PropertySnapshot> Run()
    {
        ProbeLog.Info($"Running property inventory with {_adapter.AdapterName}...");

        var snapshots = _adapter.CaptureProperties();
        var text = PropertySnapshotFormatter.FormatText(snapshots);
        var json = PropertySnapshotFormatter.FormatJson(snapshots);

        ProbeLog.WriteFile("properties.txt", text);
        ProbeLog.WriteFile("properties.json", json);

        ProbeLog.Info($"Captured {snapshots.Count} properties.");
        PrintTopCandidates("Warehouse", snapshots, s => s.Warehouse);
        PrintTopCandidates("Dock", snapshots, s => s.Dock);
        PrintTopCandidates("Safehouse", snapshots, s => s.Safehouse);
        PrintTopCandidates("Casino", snapshots, s => s.Casino);

        return snapshots;
    }

    private static void PrintTopCandidates(
        string label,
        IEnumerable<PropertySnapshot> snapshots,
        Func<PropertyCapabilityScore, int> selector)
    {
        var top = snapshots
            .Select(property => new
            {
                Property = property,
                Score = PropertyCandidateRanker.Score(property)
            })
            .OrderByDescending(x => selector(x.Score))
            .ThenBy(x => x.Property.PropertyCode, StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        ProbeLog.Info($"Top {label} candidates:");
        foreach (var candidate in top)
        {
            ProbeLog.Info(
                $"  {candidate.Property.PropertyName} " +
                $"[{candidate.Property.PropertyCode}] = {selector(candidate.Score)} " +
                $"@ {candidate.Property.Position.X:0.0}, " +
                $"{candidate.Property.Position.Y:0.0}, " +
                $"{candidate.Property.Position.Z:0.0}");
        }
    }
}
