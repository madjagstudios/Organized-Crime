using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

public enum Release1ShortNoticeAssignmentMode
{
    Primary,
    MakeGood,
    Recovery
}

public enum Release1ShortNoticeSelectionStatus
{
    Selected,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    InvalidInput
}

/// <summary>
/// One immutable Short Notice stage assignment, frozen in the same durable story revision as the
/// acceptance transition that authorizes it. There is no source drop: the player supplies the
/// manifest from their own stock, so the only drop is the handoff. The required quantity is fixed
/// by the stage, and the monetary value convention is frozen here so a later change to
/// <see cref="Release1ShortNoticeValueRule.Observed"/> cannot change the reward basis of an attempt
/// already under way.
/// </summary>
public sealed record Release1ShortNoticeAssignment
{
    public const int PrimaryQuantity = 3;
    public const int MakeGoodQuantity = 2;
    public const int RecoveryQuantity = 1;
    public const double TimedStageGameMinutes = 720d;
    public const double ShortNoticeRewardMultiplier = 1.75d;

    private string _missionKey = string.Empty;
    private int _attempt;
    private Release1ShortNoticeAssignmentMode _mode;
    private string _authorizationCorrelationId = string.Empty;
    private string _productId = string.Empty;
    private string _productName = string.Empty;
    private string _packagingId = string.Empty;
    private string _packagingName = string.Empty;
    private int _requiredQuantity;
    private string _handoffDropGuid = string.Empty;
    private string _handoffDropName = string.Empty;
    private string _handoffDropDescription = string.Empty;
    private double _handoffDropX;
    private double _handoffDropY;
    private double _handoffDropZ;
    private double? _deadlineGameMinutes;
    private double _rewardMultiplier;
    private Release1ShortNoticeValueConvention _valueConvention;

    public Release1ShortNoticeAssignment(
        string MissionKey,
        int Attempt,
        Release1ShortNoticeAssignmentMode Mode,
        string AuthorizationCorrelationId,
        string ProductId,
        string ProductName,
        string PackagingId,
        string PackagingName,
        int RequiredQuantity,
        string HandoffDropGuid,
        string HandoffDropName,
        string HandoffDropDescription,
        double HandoffDropX,
        double HandoffDropY,
        double HandoffDropZ,
        double? DeadlineGameMinutes,
        double RewardMultiplier,
        Release1ShortNoticeValueConvention ValueConvention)
    {
        this.MissionKey = MissionKey;
        this.Attempt = Attempt;
        this.Mode = Mode;
        this.AuthorizationCorrelationId = AuthorizationCorrelationId;
        this.ProductId = ProductId;
        this.ProductName = ProductName;
        this.PackagingId = PackagingId;
        this.PackagingName = PackagingName;
        this.RequiredQuantity = RequiredQuantity;
        this.HandoffDropGuid = HandoffDropGuid;
        this.HandoffDropName = HandoffDropName;
        this.HandoffDropDescription = HandoffDropDescription;
        this.HandoffDropX = HandoffDropX;
        this.HandoffDropY = HandoffDropY;
        this.HandoffDropZ = HandoffDropZ;
        this.DeadlineGameMinutes = DeadlineGameMinutes;
        this.RewardMultiplier = RewardMultiplier;
        this.ValueConvention = ValueConvention;
        Validate();
    }

    public string MissionKey
    {
        get => _missionKey;
        init
        {
            if (!string.Equals(value, Release1MissionCatalog.ShortNotice, StringComparison.Ordinal))
                throw new ArgumentException("Assignment mission must be Short Notice.", nameof(MissionKey));
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

    public Release1ShortNoticeAssignmentMode Mode
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

    public string ProductId { get => _productId; init { Release1SmallCourtesyAssignment.ValidateStableId(value, nameof(ProductId)); _productId = value; } }
    public string ProductName { get => _productName; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 256, nameof(ProductName)); _productName = value; } }
    public string PackagingId { get => _packagingId; init { Release1SmallCourtesyAssignment.ValidateStableId(value, nameof(PackagingId)); _packagingId = value; } }
    public string PackagingName { get => _packagingName; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 256, nameof(PackagingName)); _packagingName = value; } }

    public int RequiredQuantity
    {
        get => _requiredQuantity;
        init
        {
            if (value != QuantityFor(Mode))
                throw new ArgumentOutOfRangeException(nameof(RequiredQuantity), "Short Notice quantity must match the stage.");
            _requiredQuantity = value;
        }
    }

    public string HandoffDropGuid { get => _handoffDropGuid; init { Release1SmallCourtesyAssignment.ValidateStableId(value, nameof(HandoffDropGuid)); _handoffDropGuid = value; } }
    public string HandoffDropName { get => _handoffDropName; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 256, nameof(HandoffDropName)); _handoffDropName = value; } }
    public string HandoffDropDescription { get => _handoffDropDescription; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 1024, nameof(HandoffDropDescription)); _handoffDropDescription = value; } }
    public double HandoffDropX { get => _handoffDropX; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(HandoffDropX)); _handoffDropX = value; } }
    public double HandoffDropY { get => _handoffDropY; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(HandoffDropY)); _handoffDropY = value; } }
    public double HandoffDropZ { get => _handoffDropZ; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(HandoffDropZ)); _handoffDropZ = value; } }

    public double? DeadlineGameMinutes
    {
        get => _deadlineGameMinutes;
        init
        {
            ValidateDeadline(value, Mode);
            _deadlineGameMinutes = value;
        }
    }

    public double RewardMultiplier
    {
        get => _rewardMultiplier;
        init
        {
            if (!double.IsFinite(value) || value != ShortNoticeRewardMultiplier)
                throw new ArgumentOutOfRangeException(nameof(RewardMultiplier), "Short Notice reward multiplier must be 1.75.");
            _rewardMultiplier = value;
        }
    }

    public Release1ShortNoticeValueConvention ValueConvention
    {
        get => _valueConvention;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentException("Value convention is not defined.", nameof(ValueConvention));
            _valueConvention = value;
        }
    }

    public static int QuantityFor(Release1ShortNoticeAssignmentMode mode) => mode switch
    {
        Release1ShortNoticeAssignmentMode.Primary => PrimaryQuantity,
        Release1ShortNoticeAssignmentMode.MakeGood => MakeGoodQuantity,
        Release1ShortNoticeAssignmentMode.Recovery => RecoveryQuantity,
        _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
    };

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.ShortNotice, StringComparison.Ordinal))
            throw new ArgumentException("Assignment mission must be Short Notice.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
        ValidateAuthorization(AuthorizationCorrelationId, Attempt, Mode);
        Release1SmallCourtesyAssignment.ValidateStableId(ProductId, nameof(ProductId));
        Release1SmallCourtesyAssignment.ValidatePresentation(ProductName, 256, nameof(ProductName));
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
        Release1SmallCourtesyAssignment.ValidatePresentation(PackagingName, 256, nameof(PackagingName));
        if (RequiredQuantity != QuantityFor(Mode)) throw new ArgumentOutOfRangeException(nameof(RequiredQuantity));
        Release1SmallCourtesyAssignment.ValidateStableId(HandoffDropGuid, nameof(HandoffDropGuid));
        Release1SmallCourtesyAssignment.ValidatePresentation(HandoffDropName, 256, nameof(HandoffDropName));
        Release1SmallCourtesyAssignment.ValidatePresentation(HandoffDropDescription, 1024, nameof(HandoffDropDescription));
        Release1SmallCourtesyAssignment.ValidateFinite(HandoffDropX, nameof(HandoffDropX));
        Release1SmallCourtesyAssignment.ValidateFinite(HandoffDropY, nameof(HandoffDropY));
        Release1SmallCourtesyAssignment.ValidateFinite(HandoffDropZ, nameof(HandoffDropZ));
        ValidateDeadline(DeadlineGameMinutes, Mode);
        if (!double.IsFinite(RewardMultiplier) || RewardMultiplier != ShortNoticeRewardMultiplier)
            throw new ArgumentOutOfRangeException(nameof(RewardMultiplier));
        if (!Enum.IsDefined(ValueConvention))
            throw new ArgumentException("Value convention is not defined.", nameof(ValueConvention));
    }

    private static void ValidateDeadline(double? value, Release1ShortNoticeAssignmentMode mode)
    {
        if (mode == Release1ShortNoticeAssignmentMode.Recovery)
        {
            if (value is not null)
                throw new ArgumentException("Recovery has no deadline.", nameof(DeadlineGameMinutes));
            return;
        }
        if (value is null || !double.IsFinite(value.Value) || value.Value != TimedStageGameMinutes)
            throw new ArgumentOutOfRangeException(nameof(DeadlineGameMinutes), "Timed Short Notice stages run for 720 game minutes.");
    }

    private void ValidateAuthorizationIfPresent()
    {
        if (_authorizationCorrelationId.Length != 0)
            ValidateAuthorization(_authorizationCorrelationId, Attempt, Mode);
    }

    private static void ValidateAuthorization(string? value, int attempt, Release1ShortNoticeAssignmentMode mode)
    {
        var expected = mode switch
        {
            Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1ShortNoticeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
        };
        if (!Release1LogicalCorrelation.TryParse(value, out var correlation) ||
            correlation.MissionKey != Release1MissionCatalog.ShortNotice ||
            correlation.Attempt != attempt || correlation.TransitionKind != expected)
            throw new ArgumentException("Assignment authorization correlation did not match mission, attempt, and mode.", nameof(AuthorizationCorrelationId));
    }
}

/// <summary>
/// The Short Notice fields that change during play. They are written in memory through the story
/// runtime and never persisted immediately, so they revert with the native save exactly as the
/// vanilla dead drop slot contents do. There is no staged flag and no custody flag: nothing in this
/// mission is OC's to stage or take custody of. Unlike Room With No Name, ObservedQuantity and
/// LastShortfallNoticed are not monotonic, because the player may take units back out of the drop
/// before the manifest is complete; only SpreadNoticed cannot regress within an attempt.
/// </summary>
public sealed record Release1ShortNoticeProgress(
    string MissionKey,
    int Attempt,
    int ObservedQuantity,
    int? LastShortfallNoticed,
    bool SpreadNoticed)
{
    public static Release1ShortNoticeProgress Fresh(int attempt) =>
        new(Release1MissionCatalog.ShortNotice, attempt, 0, null, false);

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.ShortNotice, StringComparison.Ordinal))
            throw new ArgumentException("Progress mission must be Short Notice.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (ObservedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(ObservedQuantity));
        if (LastShortfallNoticed is not null && LastShortfallNoticed.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(LastShortfallNoticed), "A noticed shortfall is at least one unit.");
    }
}

public static class Release1ShortNoticeAssignmentSelector
{
    public static bool TrySelect(
        IReadOnlyList<Release1SmallCourtesyProductCandidate>? products,
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        Release1ShortNoticeAssignmentMode mode,
        int attempt,
        string? authorizationCorrelationId,
        string? packagingId,
        string? packagingName,
        Release1ShortNoticeValueConvention valueConvention,
        out Release1ShortNoticeAssignment? assignment,
        out Release1ShortNoticeSelectionStatus status)
    {
        assignment = null;
        status = Release1ShortNoticeSelectionStatus.InvalidInput;
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
                status = Release1ShortNoticeSelectionStatus.NoDiscoveredProduct;
                return false;
            }

            if (!Release1DeadDropSelection.TryChooseSingle(drops, authorizationCorrelationId, out var handoff))
            {
                status = Release1ShortNoticeSelectionStatus.NoEmptyDeadDrop;
                return false;
            }

            assignment = new Release1ShortNoticeAssignment(
                Release1MissionCatalog.ShortNotice,
                attempt,
                mode,
                authorizationCorrelationId!,
                product.ProductId,
                product.ProductName,
                packagingId!,
                packagingName!,
                Release1ShortNoticeAssignment.QuantityFor(mode),
                handoff!.Guid,
                handoff.Name,
                handoff.Description,
                handoff.X,
                handoff.Y,
                handoff.Z,
                mode == Release1ShortNoticeAssignmentMode.Recovery
                    ? null
                    : Release1ShortNoticeAssignment.TimedStageGameMinutes,
                Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
                valueConvention);
            status = Release1ShortNoticeSelectionStatus.Selected;
            return true;
        }
        catch (ArgumentException)
        {
            assignment = null;
            status = Release1ShortNoticeSelectionStatus.InvalidInput;
            return false;
        }
    }
}
