namespace OrganizedCrime.Model;

public sealed record Release1PresentationReceipt(string CorrelationId, long Revision)
{
    public void Validate()
    {
        if (!Release1LogicalCorrelation.TryParse(CorrelationId, out _))
            throw new ArgumentException("Presentation receipt correlation was not canonical.", nameof(CorrelationId));
        if (Revision < 0)
            throw new ArgumentException("Presentation receipt revision cannot be negative.", nameof(Revision));
    }
}
