using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-139: attaches the two room-frame player spawn points to the runtime Fish
/// Warehouse Property ahead of property registration, so an arrest (or death)
/// after visiting the warehouse respawns instead of hanging on black. The log
/// carries warnings only: a miss from the grounding ray keeps the room-frame
/// height with one warning, while a failed read-back fails the stage.
/// </summary>
public sealed class FishWarehousePlayerSpawnHost<TPoint> : IDisposable where TPoint : class
{
    private IFishWarehousePlayerSpawnTarget<TPoint>? _target;
    private TPoint? _exterior;
    private TPoint? _interior;
    private bool _disposed;

    public bool IsReady => !_disposed && _target is not null && _exterior is not null && _interior is not null;

    public TPoint? CreatedExterior => IsReady ? _exterior : null;

    public TPoint? CreatedInterior => IsReady ? _interior : null;

    public bool CreatedSpawnsRetained() =>
        IsReady && _target!.SpawnsRetained(_exterior!, _interior!);

    public bool TryAttach(IFishWarehousePlayerSpawnTarget<TPoint>? target, Action<string>? log = null)
    {
        if (IsReady)
            return true;
        if (target is null)
            return false;
        try
        {
            if (!FishWarehousePlayerSpawnDefinition.HasValidLayout())
                throw new InvalidOperationException("Fish Warehouse player spawn definition failed its room contract.");
            var exterior = FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn;
            var interior = FishWarehousePlayerSpawnDefinition.PlayerInteriorSpawn;
            var createdExterior = target.CreateSpawnPoint(exterior.Name, exterior.X, exterior.Y, exterior.Z, exterior.YawDegrees);
            var createdInterior = target.CreateSpawnPoint(interior.Name, interior.X, interior.Y, interior.Z, interior.YawDegrees);
            target.SetExteriorSpawn(createdExterior);
            target.SetInteriorSpawn(createdInterior);
            if (!target.TryGroundExterior(createdExterior))
                log?.Invoke("Fish Warehouse exterior player spawn found no ground below; keeping the room-frame height.");
            if (!target.SpawnsRetained(createdExterior, createdInterior))
                throw new InvalidOperationException("Fish Warehouse Property did not retain the player spawn references.");
            _target = target;
            _exterior = createdExterior;
            _interior = createdInterior;
            return true;
        }
        catch (Exception ex)
        {
            target.Teardown();
            log?.Invoke($"Fish Warehouse player spawns failed safely: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _target?.Teardown();
        }
        catch
        {
        }
        _target = null;
        _exterior = null;
        _interior = null;
    }
}
