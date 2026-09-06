namespace OrganizedCrime.Model;

public enum Release1RoomWithNoNameAssignmentMode
{
    Primary,
    MakeGood,
    Recovery
}

public enum Release1RoomWithNoNameSelectionStatus
{
    Selected,
    NoDiscoveredProduct,
    InsufficientEmptyDeadDrops,
    InvalidInput
}

/// <summary>
/// One immutable Room With No Name stage assignment, frozen in the same durable story revision as
/// the acceptance transition that authorizes it. The source drop is where Nell stages the
/// consignment; the handoff drop is where it is released after the hold. The hold room is never
/// selected: it is always the nine OC-48 HQ closets, and the assignment carries the expected closet
/// count so a later layout change is detected and the hold check holds instead of failing the
/// player.
/// </summary>
public sealed record Release1RoomWithNoNameAssignment
{
    public const string SyndicateHqRoomKey = "syndicate-hq";
    public const int SyndicateHqClosetCount = 9;
    public const double OneInGameDayMinutes = 1_440d;
    public const double RoomRewardMultiplier = 1.5d;

    private string _missionKey = string.Empty;
    private int _attempt;
    private Release1RoomWithNoNameAssignmentMode _mode;
    private string _authorizationCorrelationId = string.Empty;
    private string _productId = string.Empty;
    private string _productName = string.Empty;
    private string _packagingId = string.Empty;
    private string _packagingName = string.Empty;
    private int _packageQuantity;
    private string _sourceDropGuid = string.Empty;
    private string _sourceDropName = string.Empty;
    private string _sourceDropDescription = string.Empty;
    private double _sourceDropX;
    private double _sourceDropY;
    private double _sourceDropZ;
    private string _handoffDropGuid = string.Empty;
    private string _handoffDropName = string.Empty;
    private string _handoffDropDescription = string.Empty;
    private double _handoffDropX;
    private double _handoffDropY;
    private double _handoffDropZ;
    private string _holdRoomKey = string.Empty;
    private int _expectedClosetCount;
    private double _holdDurationGameMinutes;
    private double _rewardMultiplier;

    public Release1RoomWithNoNameAssignment(
        string MissionKey,
        int Attempt,
        Release1RoomWithNoNameAssignmentMode Mode,
        string AuthorizationCorrelationId,
        string ProductId,
        string ProductName,
        string PackagingId,
        string PackagingName,
        int PackageQuantity,
        string SourceDropGuid,
        string SourceDropName,
        string SourceDropDescription,
        double SourceDropX,
        double SourceDropY,
        double SourceDropZ,
        string HandoffDropGuid,
        string HandoffDropName,
        string HandoffDropDescription,
        double HandoffDropX,
        double HandoffDropY,
        double HandoffDropZ,
        string HoldRoomKey,
        int ExpectedClosetCount,
        double HoldDurationGameMinutes,
        double RewardMultiplier)
    {
        this.MissionKey = MissionKey;
        this.Attempt = Attempt;
        this.Mode = Mode;
        this.AuthorizationCorrelationId = AuthorizationCorrelationId;
        this.ProductId = ProductId;
        this.ProductName = ProductName;
        this.PackagingId = PackagingId;
        this.PackagingName = PackagingName;
        this.PackageQuantity = PackageQuantity;
        this.SourceDropGuid = SourceDropGuid;
        this.SourceDropName = SourceDropName;
        this.SourceDropDescription = SourceDropDescription;
        this.SourceDropX = SourceDropX;
        this.SourceDropY = SourceDropY;
        this.SourceDropZ = SourceDropZ;
        this.HandoffDropGuid = HandoffDropGuid;
        this.HandoffDropName = HandoffDropName;
        this.HandoffDropDescription = HandoffDropDescription;
        this.HandoffDropX = HandoffDropX;
        this.HandoffDropY = HandoffDropY;
        this.HandoffDropZ = HandoffDropZ;
        this.HoldRoomKey = HoldRoomKey;
        this.ExpectedClosetCount = ExpectedClosetCount;
        this.HoldDurationGameMinutes = HoldDurationGameMinutes;
        this.RewardMultiplier = RewardMultiplier;
        Validate();
    }

    public string MissionKey
    {
        get => _missionKey;
        init
        {
            if (!string.Equals(value, Release1MissionCatalog.RoomWithNoName, StringComparison.Ordinal))
                throw new ArgumentException("Assignment mission must be A Room With No Name.", nameof(MissionKey));
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

    public Release1RoomWithNoNameAssignmentMode Mode
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

    public int PackageQuantity
    {
        get => _packageQuantity;
        init
        {
            if (value != 1) throw new ArgumentOutOfRangeException(nameof(PackageQuantity), "Room With No Name consignments are exactly one unit.");
            _packageQuantity = value;
        }
    }

    public string SourceDropGuid { get => _sourceDropGuid; init { Release1SmallCourtesyAssignment.ValidateStableId(value, nameof(SourceDropGuid)); _sourceDropGuid = value; } }
    public string SourceDropName { get => _sourceDropName; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 256, nameof(SourceDropName)); _sourceDropName = value; } }
    public string SourceDropDescription { get => _sourceDropDescription; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 1024, nameof(SourceDropDescription)); _sourceDropDescription = value; } }
    public double SourceDropX { get => _sourceDropX; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(SourceDropX)); _sourceDropX = value; } }
    public double SourceDropY { get => _sourceDropY; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(SourceDropY)); _sourceDropY = value; } }
    public double SourceDropZ { get => _sourceDropZ; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(SourceDropZ)); _sourceDropZ = value; } }

    public string HandoffDropGuid
    {
        get => _handoffDropGuid;
        init
        {
            Release1SmallCourtesyAssignment.ValidateStableId(value, nameof(HandoffDropGuid));
            if (string.Equals(_sourceDropGuid, value, StringComparison.Ordinal))
                throw new ArgumentException("The pick up and the hand off must be different drops.", nameof(HandoffDropGuid));
            _handoffDropGuid = value;
        }
    }
    public string HandoffDropName { get => _handoffDropName; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 256, nameof(HandoffDropName)); _handoffDropName = value; } }
    public string HandoffDropDescription { get => _handoffDropDescription; init { Release1SmallCourtesyAssignment.ValidatePresentation(value, 1024, nameof(HandoffDropDescription)); _handoffDropDescription = value; } }
    public double HandoffDropX { get => _handoffDropX; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(HandoffDropX)); _handoffDropX = value; } }
    public double HandoffDropY { get => _handoffDropY; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(HandoffDropY)); _handoffDropY = value; } }
    public double HandoffDropZ { get => _handoffDropZ; init { Release1SmallCourtesyAssignment.ValidateFinite(value, nameof(HandoffDropZ)); _handoffDropZ = value; } }

    public string HoldRoomKey
    {
        get => _holdRoomKey;
        init
        {
            if (!string.Equals(value, SyndicateHqRoomKey, StringComparison.Ordinal))
                throw new ArgumentException("The hold room is always the Syndicate HQ storage room.", nameof(HoldRoomKey));
            _holdRoomKey = value;
        }
    }

    public int ExpectedClosetCount
    {
        get => _expectedClosetCount;
        init
        {
            if (value != SyndicateHqClosetCount)
                throw new ArgumentOutOfRangeException(nameof(ExpectedClosetCount), "The hold room has exactly nine closets.");
            _expectedClosetCount = value;
        }
    }

    public double HoldDurationGameMinutes
    {
        get => _holdDurationGameMinutes;
        init
        {
            if (!double.IsFinite(value) || value != OneInGameDayMinutes)
                throw new ArgumentOutOfRangeException(nameof(HoldDurationGameMinutes), "The hold is exactly one in-game day.");
            _holdDurationGameMinutes = value;
        }
    }

    public double RewardMultiplier
    {
        get => _rewardMultiplier;
        init
        {
            if (!double.IsFinite(value) || value != RoomRewardMultiplier)
                throw new ArgumentOutOfRangeException(nameof(RewardMultiplier), "Room With No Name reward multiplier must be 1.5.");
            _rewardMultiplier = value;
        }
    }

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.RoomWithNoName, StringComparison.Ordinal))
            throw new ArgumentException("Assignment mission must be A Room With No Name.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
        ValidateAuthorization(AuthorizationCorrelationId, Attempt, Mode);
        Release1SmallCourtesyAssignment.ValidateStableId(ProductId, nameof(ProductId));
        Release1SmallCourtesyAssignment.ValidatePresentation(ProductName, 256, nameof(ProductName));
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
        Release1SmallCourtesyAssignment.ValidatePresentation(PackagingName, 256, nameof(PackagingName));
        if (PackageQuantity != 1) throw new ArgumentOutOfRangeException(nameof(PackageQuantity));
        Release1SmallCourtesyAssignment.ValidateStableId(SourceDropGuid, nameof(SourceDropGuid));
        Release1SmallCourtesyAssignment.ValidatePresentation(SourceDropName, 256, nameof(SourceDropName));
        Release1SmallCourtesyAssignment.ValidatePresentation(SourceDropDescription, 1024, nameof(SourceDropDescription));
        Release1SmallCourtesyAssignment.ValidateFinite(SourceDropX, nameof(SourceDropX));
        Release1SmallCourtesyAssignment.ValidateFinite(SourceDropY, nameof(SourceDropY));
        Release1SmallCourtesyAssignment.ValidateFinite(SourceDropZ, nameof(SourceDropZ));
        Release1SmallCourtesyAssignment.ValidateStableId(HandoffDropGuid, nameof(HandoffDropGuid));
        Release1SmallCourtesyAssignment.ValidatePresentation(HandoffDropName, 256, nameof(HandoffDropName));
        Release1SmallCourtesyAssignment.ValidatePresentation(HandoffDropDescription, 1024, nameof(HandoffDropDescription));
        Release1SmallCourtesyAssignment.ValidateFinite(HandoffDropX, nameof(HandoffDropX));
        Release1SmallCourtesyAssignment.ValidateFinite(HandoffDropY, nameof(HandoffDropY));
        Release1SmallCourtesyAssignment.ValidateFinite(HandoffDropZ, nameof(HandoffDropZ));
        if (string.Equals(SourceDropGuid, HandoffDropGuid, StringComparison.Ordinal))
            throw new ArgumentException("The pick up and the hand off must be different drops.", nameof(HandoffDropGuid));
        if (!string.Equals(HoldRoomKey, SyndicateHqRoomKey, StringComparison.Ordinal))
            throw new ArgumentException("The hold room is always the Syndicate HQ storage room.", nameof(HoldRoomKey));
        if (ExpectedClosetCount != SyndicateHqClosetCount) throw new ArgumentOutOfRangeException(nameof(ExpectedClosetCount));
        if (!double.IsFinite(HoldDurationGameMinutes) || HoldDurationGameMinutes != OneInGameDayMinutes)
            throw new ArgumentOutOfRangeException(nameof(HoldDurationGameMinutes));
        if (!double.IsFinite(RewardMultiplier) || RewardMultiplier != RoomRewardMultiplier)
            throw new ArgumentOutOfRangeException(nameof(RewardMultiplier));
    }

    private void ValidateAuthorizationIfPresent()
    {
        if (_authorizationCorrelationId.Length != 0)
            ValidateAuthorization(_authorizationCorrelationId, Attempt, Mode);
    }

    private static void ValidateAuthorization(string? value, int attempt, Release1RoomWithNoNameAssignmentMode mode)
    {
        var expected = mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1RoomWithNoNameAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
        };
        if (!Release1LogicalCorrelation.TryParse(value, out var correlation) ||
            correlation.MissionKey != Release1MissionCatalog.RoomWithNoName ||
            correlation.Attempt != attempt || correlation.TransitionKind != expected)
            throw new ArgumentException("Assignment authorization correlation did not match mission, attempt, and mode.", nameof(AuthorizationCorrelationId));
    }
}

/// <summary>
/// The Room With No Name flags that change during play. They are written in memory through the
/// story runtime and never persisted immediately, so they revert with the native save exactly as
/// the vanilla dead drop and HQ closet slot contents do. Ordering is monotonic within one attempt:
/// nothing is stowed before it is in custody, and nothing is in custody before it was staged.
/// </summary>
public sealed record Release1RoomWithNoNameProgress(
    string MissionKey,
    int Attempt,
    bool Staged,
    bool Custody,
    bool Stowed,
    bool HoldSatisfied,
    double? StowedAtGameMinutes,
    string? HoldingClosetGuid,
    double? MissingSincePassGameMinutes)
{
    public static Release1RoomWithNoNameProgress Fresh(int attempt) =>
        new(Release1MissionCatalog.RoomWithNoName, attempt, false, false, false, false, null, null, null);

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.RoomWithNoName, StringComparison.Ordinal))
            throw new ArgumentException("Progress mission must be A Room With No Name.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (Custody && !Staged)
            throw new ArgumentException("Custody cannot be recorded before the consignment was staged.", nameof(Custody));
        if (Stowed && !Custody)
            throw new ArgumentException("A stow cannot be recorded before custody.", nameof(Stowed));
        if (HoldSatisfied && !Stowed)
            throw new ArgumentException("The hold cannot be satisfied before the consignment was stowed.", nameof(HoldSatisfied));
        if (Stowed && StowedAtGameMinutes is null)
            throw new ArgumentException("A stow must record when it was first observed.", nameof(StowedAtGameMinutes));
        if (Stowed && string.IsNullOrWhiteSpace(HoldingClosetGuid))
            throw new ArgumentException("A stow must record the holding closet.", nameof(HoldingClosetGuid));
        if (!Stowed && (StowedAtGameMinutes is not null || HoldingClosetGuid is not null))
            throw new ArgumentException("Stow details cannot exist before a stow was observed.", nameof(StowedAtGameMinutes));
        if (HoldingClosetGuid is not null)
            Release1SmallCourtesyAssignment.ValidateStableId(HoldingClosetGuid, nameof(HoldingClosetGuid));
        ValidateGameMinutes(StowedAtGameMinutes, nameof(StowedAtGameMinutes));
        ValidateGameMinutes(MissingSincePassGameMinutes, nameof(MissingSincePassGameMinutes));
    }

    private static void ValidateGameMinutes(double? value, string parameterName)
    {
        if (value is null) return;
        if (!double.IsFinite(value.Value) || value.Value < 0d)
            throw new ArgumentOutOfRangeException(parameterName, "Game minutes must be finite and non-negative.");
    }
}

public static class Release1RoomWithNoNameAssignmentSelector
{
    public static bool TrySelect(
        IReadOnlyList<Release1SmallCourtesyProductCandidate>? products,
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        Release1RoomWithNoNameAssignmentMode mode,
        int attempt,
        string? authorizationCorrelationId,
        string? packagingId,
        string? packagingName,
        out Release1RoomWithNoNameAssignment? assignment,
        out Release1RoomWithNoNameSelectionStatus status)
    {
        assignment = null;
        status = Release1RoomWithNoNameSelectionStatus.InvalidInput;
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
                status = Release1RoomWithNoNameSelectionStatus.NoDiscoveredProduct;
                return false;
            }

            if (!Release1DeadDropSelection.TryChoosePair(drops, authorizationCorrelationId, out var source, out var handoff))
            {
                status = Release1RoomWithNoNameSelectionStatus.InsufficientEmptyDeadDrops;
                return false;
            }

            assignment = new Release1RoomWithNoNameAssignment(
                Release1MissionCatalog.RoomWithNoName,
                attempt,
                mode,
                authorizationCorrelationId!,
                product.ProductId,
                product.ProductName,
                packagingId!,
                packagingName!,
                1,
                source!.Guid,
                source.Name,
                source.Description,
                source.X,
                source.Y,
                source.Z,
                handoff!.Guid,
                handoff.Name,
                handoff.Description,
                handoff.X,
                handoff.Y,
                handoff.Z,
                Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
                Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
                Release1RoomWithNoNameAssignment.OneInGameDayMinutes,
                Release1RoomWithNoNameAssignment.RoomRewardMultiplier);
            status = Release1RoomWithNoNameSelectionStatus.Selected;
            return true;
        }
        catch (ArgumentException)
        {
            assignment = null;
            status = Release1RoomWithNoNameSelectionStatus.InvalidInput;
            return false;
        }
    }
}
