using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OrganizedCrime.Model;

public enum Release1SmallCourtesyAssignmentMode
{
    Primary,
    MakeGood,
    Recovery
}

public enum Release1SmallCourtesyAssignmentSelectionStatus
{
    Selected,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    InvalidInput
}

public sealed record Release1SmallCourtesyProductCandidate(
    string ProductId,
    string ProductName,
    double AskingPrice,
    bool IsDiscovered)
{
    public void Validate()
    {
        Release1SmallCourtesyAssignment.ValidateStableId(ProductId, nameof(ProductId));
        Release1SmallCourtesyAssignment.ValidatePresentation(ProductName, 256, nameof(ProductName));
        Release1SmallCourtesyAssignment.ValidateNonNegativeFinite(AskingPrice, nameof(AskingPrice));
    }
}

public sealed record Release1SmallCourtesyDropCandidate(
    string Guid,
    string Name,
    string Description,
    double X,
    double Y,
    double Z,
    bool IsEmpty)
{
    public void Validate()
    {
        Release1SmallCourtesyAssignment.ValidateStableId(Guid, nameof(Guid));
        Release1SmallCourtesyAssignment.ValidatePresentation(Name, 256, nameof(Name));
        Release1SmallCourtesyAssignment.ValidatePresentation(Description, 1024, nameof(Description));
        Release1SmallCourtesyAssignment.ValidateFinite(X, nameof(X));
        Release1SmallCourtesyAssignment.ValidateFinite(Y, nameof(Y));
        Release1SmallCourtesyAssignment.ValidateFinite(Z, nameof(Z));
    }
}

public sealed record Release1SmallCourtesyAssignment
{
    private string _missionKey = string.Empty;
    private int _attempt;
    private Release1SmallCourtesyAssignmentMode _mode;
    private string _authorizationCorrelationId = string.Empty;
    private string _productId = string.Empty;
    private string _productName = string.Empty;
    private double _selectionAskingPrice;
    private string _packagingId = string.Empty;
    private string _packagingName = string.Empty;
    private string _deadDropGuid = string.Empty;
    private string _deadDropName = string.Empty;
    private string _deadDropDescription = string.Empty;
    private double _deadDropX;
    private double _deadDropY;
    private double _deadDropZ;
    private double _rewardMultiplier;

    public Release1SmallCourtesyAssignment(
        string MissionKey,
        int Attempt,
        Release1SmallCourtesyAssignmentMode Mode,
        string AuthorizationCorrelationId,
        string ProductId,
        string ProductName,
        double SelectionAskingPrice,
        string PackagingId,
        string PackagingName,
        string DeadDropGuid,
        string DeadDropName,
        string DeadDropDescription,
        double DeadDropX,
        double DeadDropY,
        double DeadDropZ,
        double RewardMultiplier)
    {
        this.MissionKey = MissionKey;
        this.Attempt = Attempt;
        this.Mode = Mode;
        this.AuthorizationCorrelationId = AuthorizationCorrelationId;
        this.ProductId = ProductId;
        this.ProductName = ProductName;
        this.SelectionAskingPrice = SelectionAskingPrice;
        this.PackagingId = PackagingId;
        this.PackagingName = PackagingName;
        this.DeadDropGuid = DeadDropGuid;
        this.DeadDropName = DeadDropName;
        this.DeadDropDescription = DeadDropDescription;
        this.DeadDropX = DeadDropX;
        this.DeadDropY = DeadDropY;
        this.DeadDropZ = DeadDropZ;
        this.RewardMultiplier = RewardMultiplier;
        Validate();
    }

    public string MissionKey
    {
        get => _missionKey;
        init
        {
            if (!string.Equals(value, Release1MissionCatalog.SmallCourtesy, StringComparison.Ordinal))
                throw new ArgumentException("Assignment mission must be Small Courtesy.", nameof(MissionKey));
            _missionKey = value;
        }
    }

    public int Attempt
    {
        get => _attempt;
        init
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
            _attempt = value;
            ValidateAuthorizationIfPresent();
        }
    }

    public Release1SmallCourtesyAssignmentMode Mode
    {
        get => _mode;
        init
        {
            if (!Enum.IsDefined(value)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
            _mode = value;
            ValidateAuthorizationIfPresent();
        }
    }

    public string AuthorizationCorrelationId
    {
        get => _authorizationCorrelationId;
        init
        {
            ValidateAuthorization(value, Attempt, Mode);
            _authorizationCorrelationId = value;
        }
    }

    public string ProductId { get => _productId; init { ValidateStableId(value, nameof(ProductId)); _productId = value; } }
    public string ProductName { get => _productName; init { ValidatePresentation(value, 256, nameof(ProductName)); _productName = value; } }
    public double SelectionAskingPrice { get => _selectionAskingPrice; init { ValidateNonNegativeFinite(value, nameof(SelectionAskingPrice)); _selectionAskingPrice = value; } }
    public string PackagingId { get => _packagingId; init { ValidateStableId(value, nameof(PackagingId)); _packagingId = value; } }
    public string PackagingName { get => _packagingName; init { ValidatePresentation(value, 256, nameof(PackagingName)); _packagingName = value; } }
    public string DeadDropGuid { get => _deadDropGuid; init { ValidateStableId(value, nameof(DeadDropGuid)); _deadDropGuid = value; } }
    public string DeadDropName { get => _deadDropName; init { ValidatePresentation(value, 256, nameof(DeadDropName)); _deadDropName = value; } }
    public string DeadDropDescription { get => _deadDropDescription; init { ValidatePresentation(value, 1024, nameof(DeadDropDescription)); _deadDropDescription = value; } }
    public double DeadDropX { get => _deadDropX; init { ValidateFinite(value, nameof(DeadDropX)); _deadDropX = value; } }
    public double DeadDropY { get => _deadDropY; init { ValidateFinite(value, nameof(DeadDropY)); _deadDropY = value; } }
    public double DeadDropZ { get => _deadDropZ; init { ValidateFinite(value, nameof(DeadDropZ)); _deadDropZ = value; } }
    public double RewardMultiplier
    {
        get => _rewardMultiplier;
        init
        {
            if (!double.IsFinite(value) || value != 1.25d)
                throw new ArgumentOutOfRangeException(nameof(RewardMultiplier), "Small Courtesy reward multiplier must be 1.25.");
            _rewardMultiplier = value;
        }
    }

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.SmallCourtesy, StringComparison.Ordinal))
            throw new ArgumentException("Assignment mission must be Small Courtesy.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
        ValidateAuthorization(AuthorizationCorrelationId, Attempt, Mode);
        ValidateStableId(ProductId, nameof(ProductId));
        ValidatePresentation(ProductName, 256, nameof(ProductName));
        ValidateNonNegativeFinite(SelectionAskingPrice, nameof(SelectionAskingPrice));
        ValidateStableId(PackagingId, nameof(PackagingId));
        ValidatePresentation(PackagingName, 256, nameof(PackagingName));
        ValidateStableId(DeadDropGuid, nameof(DeadDropGuid));
        ValidatePresentation(DeadDropName, 256, nameof(DeadDropName));
        ValidatePresentation(DeadDropDescription, 1024, nameof(DeadDropDescription));
        ValidateFinite(DeadDropX, nameof(DeadDropX));
        ValidateFinite(DeadDropY, nameof(DeadDropY));
        ValidateFinite(DeadDropZ, nameof(DeadDropZ));
        if (!double.IsFinite(RewardMultiplier) || RewardMultiplier != 1.25d)
            throw new ArgumentOutOfRangeException(nameof(RewardMultiplier));
    }

    internal static void ValidateStableId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('/'))
            throw new ArgumentException("Stable IDs must be nonblank, bounded, whitespace-free single segments.", parameterName);
    }

    internal static void ValidatePresentation(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException("Presentation text must be nonblank, bounded, and control-character-free.", parameterName);
    }

    internal static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
    }

    internal static void ValidateNonNegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and non-negative.");
    }

    private void ValidateAuthorizationIfPresent()
    {
        if (_authorizationCorrelationId.Length != 0)
            ValidateAuthorization(_authorizationCorrelationId, Attempt, Mode);
    }

    private static void ValidateAuthorization(string? value, int attempt, Release1SmallCourtesyAssignmentMode mode)
    {
        var expected = mode switch
        {
            Release1SmallCourtesyAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1SmallCourtesyAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1SmallCourtesyAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
        };
        if (!Release1LogicalCorrelation.TryParse(value, out var correlation) ||
            correlation.MissionKey != Release1MissionCatalog.SmallCourtesy ||
            correlation.Attempt != attempt || correlation.TransitionKind != expected)
            throw new ArgumentException("Assignment authorization correlation did not match mission, attempt, and mode.", nameof(AuthorizationCorrelationId));
    }
}

public static class Release1SmallCourtesyAssignmentSelector
{
    public static bool TrySelect(
        IReadOnlyList<Release1SmallCourtesyProductCandidate>? products,
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        Release1SmallCourtesyAssignmentMode mode,
        int attempt,
        string? authorizationCorrelationId,
        string? packagingId,
        string? packagingName,
        out Release1SmallCourtesyAssignment? assignment,
        out Release1SmallCourtesyAssignmentSelectionStatus status)
    {
        assignment = null;
        status = Release1SmallCourtesyAssignmentSelectionStatus.InvalidInput;
        if (products is null || drops is null) return false;

        try
        {
            foreach (var candidate in products)
                (candidate ?? throw new ArgumentException("Product candidates cannot be null.")).Validate();
            foreach (var candidate in drops)
                (candidate ?? throw new ArgumentException("Drop candidates cannot be null.")).Validate();

            var product = products
                .Where(candidate => candidate.IsDiscovered)
                .OrderByDescending(candidate => candidate.AskingPrice)
                .ThenBy(candidate => candidate.ProductId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (product is null)
            {
                status = Release1SmallCourtesyAssignmentSelectionStatus.NoDiscoveredProduct;
                return false;
            }

            var emptyDrops = drops
                .Where(candidate => candidate.IsEmpty)
                .OrderBy(candidate => candidate.Guid, StringComparer.Ordinal)
                .ToArray();
            if (emptyDrops.Length == 0)
            {
                status = Release1SmallCourtesyAssignmentSelectionStatus.NoEmptyDeadDrop;
                return false;
            }

            var correlationBytes = Encoding.UTF8.GetBytes(authorizationCorrelationId ?? string.Empty);
            var digest = SHA256.HashData(correlationBytes);
            var index = (int)(BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, sizeof(ulong))) % (ulong)emptyDrops.Length);
            var drop = emptyDrops[index];
            assignment = new Release1SmallCourtesyAssignment(
                Release1MissionCatalog.SmallCourtesy,
                attempt,
                mode,
                authorizationCorrelationId!,
                product.ProductId,
                product.ProductName,
                product.AskingPrice,
                packagingId!,
                packagingName!,
                drop.Guid,
                drop.Name,
                drop.Description,
                drop.X,
                drop.Y,
                drop.Z,
                1.25d);
            status = Release1SmallCourtesyAssignmentSelectionStatus.Selected;
            return true;
        }
        catch (ArgumentException)
        {
            assignment = null;
            status = Release1SmallCourtesyAssignmentSelectionStatus.InvalidInput;
            return false;
        }
    }
}
