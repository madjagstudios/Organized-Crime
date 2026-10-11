namespace OrganizedCrime.Model;

public sealed record Release1SmallCourtesyPackagingCandidate(string PackagingId, string PackagingName)
{
    public void Validate()
    {
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
        Release1SmallCourtesyAssignment.ValidatePresentation(PackagingName, 256, nameof(PackagingName));
    }
}

public sealed record Release1SmallCourtesySlotSnapshot(
    int SlotIndex,
    string? ProductId,
    string? PackagingId,
    int Quantity,
    bool IsPackaged,
    float MonetaryValue)
{
    public void Validate()
    {
        if (SlotIndex < 0 || Quantity < 0 || !float.IsFinite(MonetaryValue) || MonetaryValue < 0f)
            throw new ArgumentException("Slot snapshot values were invalid.");
        if (Quantity == 0) return;
        Release1SmallCourtesyAssignment.ValidateStableId(ProductId, nameof(ProductId));
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
    }
}
