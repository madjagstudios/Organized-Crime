namespace OrganizedCrime.Model;

/// <summary>
/// OC-138: what the staff-return banner should do about one replay
/// result: nothing, show (or refresh) the game's top-screen text, or
/// hide it again once the replay settles.
/// </summary>
public enum FishWarehouseStaffReturnBannerOp
{
    None,
    Show,
    Hide
}

/// <summary>
/// OC-138: one banner decision. Show carries the exact banner text;
/// Hide and None carry no text.
/// </summary>
public sealed record FishWarehouseStaffReturnBannerDecision(
    FishWarehouseStaffReturnBannerOp Op,
    string? Text);

/// <summary>
/// OC-138: decides when the "warehouse employees loading" banner shows,
/// refreshes, and hides. Pure and ordered: feed it every employee replay
/// result after navigation is ready. It shows on the first Pending with
/// staff to restore, refreshes only when the counts change, and hides
/// when the replay settles, whether it succeeded or failed. Nothing
/// shows before navigation is ready, with no staff, or when the show
/// never fired (the replay settled on its first pass). One instance per
/// load; <see cref="Reset"/> starts a new load.
/// </summary>
public sealed class FishWarehouseStaffReturnBanner
{
    /// <summary>
    /// The banner's fixed prefix. The sender matches this prefix to
    /// tell OC's own banner apart from another system's, so it never
    /// hides a foreign banner.
    /// </summary>
    public const string TextPrefix = "Organized Crime: warehouse employees loading";

    private bool _shown;
    private bool _done;
    private string? _lastText;

    public FishWarehouseStaffReturnBannerDecision Observe(bool navigationReady, FishWarehouseEmployeeReplayResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (_done)
            return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.None, null);
        if (!navigationReady)
            return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.None, null);

        if (result.State == FishWarehouseEmployeeReplayState.Pending)
        {
            if (result.SupportedCapturedEmployeeCount < 1)
                return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.None, null);
            var text = FormatText(result.ReplayedEmployeeCount, result.SupportedCapturedEmployeeCount);
            if (!_shown)
            {
                _shown = true;
                _lastText = text;
                return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.Show, text);
            }
            if (!string.Equals(text, _lastText, StringComparison.Ordinal))
            {
                _lastText = text;
                return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.Show, text);
            }
            return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.None, null);
        }

        _done = true;
        if (!_shown)
            return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.None, null);
        return new FishWarehouseStaffReturnBannerDecision(FishWarehouseStaffReturnBannerOp.Hide, null);
    }

    public void Reset()
    {
        _shown = false;
        _done = false;
        _lastText = null;
    }

    public static string FormatText(int replayedCount, int supportedCount) =>
        $"{TextPrefix} ({replayedCount}/{supportedCount})";
}
