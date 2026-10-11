using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-138: the staff-return announcer behind the banner sender, so the
/// replay never depends on the live HUD. Feeds employee replay results
/// into the banner model and shows, updates, or hides the game's
/// top-screen text as decided. Never breaks the replay: a declining or
/// throwing sender logs one warning per failed send and the replay
/// keeps going. Reset starts a new load and hides a banner left up.
/// </summary>
public sealed class FishWarehouseStaffReturnAnnouncer
{
    private readonly IFishWarehouseStaffReturnBannerSender _sender;
    private readonly Action<string> _log;
    private readonly FishWarehouseStaffReturnBanner _banner = new();

    public FishWarehouseStaffReturnAnnouncer(IFishWarehouseStaffReturnBannerSender sender, Action<string>? log = null)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _log = log ?? (_ => { });
    }

    public void Observe(bool navigationReady, FishWarehouseEmployeeReplayResult result)
    {
        var decision = _banner.Observe(navigationReady, result);
        if (decision.Op == FishWarehouseStaffReturnBannerOp.None)
            return;

        try
        {
            bool sent = decision.Op == FishWarehouseStaffReturnBannerOp.Show
                ? _sender.TryShow(decision.Text!)
                : _sender.TryHideOwn();
            if (!sent)
                _log("Fish Warehouse staff return banner declined: the banner sender was unavailable.");
        }
        catch (Exception ex)
        {
            _log($"Fish Warehouse staff return banner failed safely: {ex.Message}");
        }
    }

    public void Reset()
    {
        _banner.Reset();
        try
        {
            _sender.Reset();
        }
        catch (Exception ex)
        {
            _log($"Fish Warehouse staff return banner reset failed safely: {ex.Message}");
        }
    }
}
