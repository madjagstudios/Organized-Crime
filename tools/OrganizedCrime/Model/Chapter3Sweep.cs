namespace OrganizedCrime.Model;

// Durable timing only. Officers, routes and leased cars remain D5 runtime state.
public sealed record Chapter3Sweep(
    string Id, string TargetCode, double PlannedStartMinutes, bool Small,
    string? WarningReceipt = null, double? WarningMinutes = null,
    double? ActiveStartMinutes = null, double? ActiveEndMinutes = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(TargetCode) ||
            !ValidMinutes(PlannedStartMinutes))
            throw new ArgumentException("Invalid sweep identity, target or planned start.");
        if ((WarningReceipt is null) != (WarningMinutes is null) ||
            (WarningReceipt is not null && (string.IsNullOrWhiteSpace(WarningReceipt) || !Small)) ||
            (WarningMinutes is { } warning && (!ValidMinutes(warning) || PlannedStartMinutes < warning + 1440d)))
            throw new ArgumentException("Invalid sweep warning receipt or delay.");
        if ((ActiveStartMinutes is null) != (ActiveEndMinutes is null) ||
            (ActiveStartMinutes is { } start &&
             (!ValidMinutes(start) || ActiveEndMinutes != start + 1440d || start < PlannedStartMinutes ||
              (Small && (WarningMinutes is null || start < WarningMinutes + 1440d)))))
            throw new ArgumentException("Invalid sweep active interval.");
    }

    internal static bool ValidMinutes(double value) => double.IsFinite(value) && value >= 0d;
}
