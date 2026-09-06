namespace OrganizedCrime.PropertyProbe.Model;

public sealed record PropertyCapabilityScore(
    int Safehouse,
    int Warehouse,
    int Dock,
    int Casino,
    IReadOnlyList<string> Reasons);
