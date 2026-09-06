using System.Globalization;

namespace OrganizedCrime.Model;

public sealed record Release1SmallCourtesyCargoIdentity
{
    private const string Prefix = "sc-cargo-v1";
    private const char Delimiter = '|';
    private const int MaximumEncodedLength = 256;

    public Release1SmallCourtesyCargoIdentity(
        string productId,
        string packageId,
        int slotIndex,
        int preQuantity,
        int postQuantity,
        float depositedMonetaryValue)
    {
        ValidateId(productId, nameof(productId));
        ValidateId(packageId, nameof(packageId));
        if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (preQuantity < 1 || postQuantity < 0 || postQuantity >= preQuantity)
            throw new ArgumentException("Cargo quantities must describe at least one consumed unit.");
        if (!float.IsFinite(depositedMonetaryValue) || depositedMonetaryValue < 0f)
            throw new ArgumentOutOfRangeException(nameof(depositedMonetaryValue));

        ProductId = productId;
        PackageId = packageId;
        SlotIndex = slotIndex;
        PreQuantity = preQuantity;
        PostQuantity = postQuantity;
        DepositedMonetaryValue = depositedMonetaryValue;

        if (BuildEncoded().Length > MaximumEncodedLength)
            throw new ArgumentException("Cargo identity exceeds the native-effect field limit.");
    }

    public string ProductId { get; }
    public string PackageId { get; }
    public int SlotIndex { get; }
    public int PreQuantity { get; }
    public int PostQuantity { get; }
    public float DepositedMonetaryValue { get; }

    /// <summary>
    /// How many units this effect removes. Derived rather than encoded so the serialized form keeps
    /// its seven fields and its v1 prefix, which is what lets a sidecar written before OC-58 parse
    /// unchanged.
    /// </summary>
    public int ConsumedCount => PreQuantity - PostQuantity;

    public string Serialize() => BuildEncoded();

    public static bool TryParse(string? encoded, out Release1SmallCourtesyCargoIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumEncodedLength) return false;
        var fields = encoded.Split(Delimiter);
        if (fields.Length != 7 || !string.Equals(fields[0], Prefix, StringComparison.Ordinal) ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var slotIndex) ||
            !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var preQuantity) ||
            !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var postQuantity) ||
            !float.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var depositedValue))
            return false;

        try
        {
            identity = new(fields[1], fields[2], slotIndex, preQuantity, postQuantity, depositedValue);
            return string.Equals(identity.Serialize(), encoded, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            identity = null;
            return false;
        }
    }

    private string BuildEncoded() => string.Join(Delimiter,
        Prefix,
        ProductId,
        PackageId,
        SlotIndex.ToString(CultureInfo.InvariantCulture),
        PreQuantity.ToString(CultureInfo.InvariantCulture),
        PostQuantity.ToString(CultureInfo.InvariantCulture),
        DepositedMonetaryValue.ToString("R", CultureInfo.InvariantCulture));

    private static void ValidateId(string value, string parameterName)
    {
        Release1SmallCourtesyAssignment.ValidateStableId(value, parameterName);
        if (value.Contains(Delimiter))
            throw new ArgumentException("Native-effect identity IDs cannot contain the field delimiter.", parameterName);
    }
}

public sealed record Release1SmallCourtesyRewardIdentity
{
    private const string Prefix = "sc-reward-v1";
    private const char Delimiter = '|';
    private const int MaximumEncodedLength = 256;
    public const float FixedVerificationTolerance = 0.50f;

    public Release1SmallCourtesyRewardIdentity(float baselineCash, float wholeDollarAmount, float expectedCash)
    {
        ValidateMoney(baselineCash, nameof(baselineCash));
        ValidateMoney(wholeDollarAmount, nameof(wholeDollarAmount));
        ValidateMoney(expectedCash, nameof(expectedCash));
        if (wholeDollarAmount != MathF.Truncate(wholeDollarAmount))
            throw new ArgumentException("Reward amount must be a whole-dollar value.", nameof(wholeDollarAmount));
        var calculatedExpected = baselineCash + wholeDollarAmount;
        if (!float.IsFinite(calculatedExpected) || MathF.Abs(expectedCash - calculatedExpected) > FixedVerificationTolerance)
            throw new ArgumentException("Expected cash does not match baseline plus reward within tolerance.", nameof(expectedCash));

        BaselineCash = baselineCash;
        WholeDollarAmount = wholeDollarAmount;
        ExpectedCash = expectedCash;
    }

    public float BaselineCash { get; }
    public float WholeDollarAmount { get; }
    public float ExpectedCash { get; }
    public float VerificationTolerance => FixedVerificationTolerance;

    public string Serialize() => string.Join(Delimiter,
        Prefix,
        BaselineCash.ToString("R", CultureInfo.InvariantCulture),
        WholeDollarAmount.ToString("R", CultureInfo.InvariantCulture),
        ExpectedCash.ToString("R", CultureInfo.InvariantCulture),
        FixedVerificationTolerance.ToString("R", CultureInfo.InvariantCulture));

    public static bool TryParse(string? encoded, out Release1SmallCourtesyRewardIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumEncodedLength) return false;
        var fields = encoded.Split(Delimiter);
        if (fields.Length != 5 || !string.Equals(fields[0], Prefix, StringComparison.Ordinal) ||
            !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var baseline) ||
            !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ||
            !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var expected) ||
            !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) ||
            tolerance != FixedVerificationTolerance)
            return false;

        try
        {
            identity = new(baseline, amount, expected);
            return string.Equals(identity.Serialize(), encoded, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            identity = null;
            return false;
        }
    }

    private static void ValidateMoney(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Money values must be finite and non-negative.");
    }
}
