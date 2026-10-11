namespace OrganizedCrime.Runtime.Release1.World;

// Memberless unions retain one world argument and the same adapter instance.
public interface IDeadDropInventoryWorld : IProductCatalogWorld, IDeadDropWorld
{
}

public interface IDeliveryMissionWorld : IGameTimeWorld, IDeadDropInventoryWorld, IWalletWorld
{
}

public interface IRoomMissionWorld : IDeliveryMissionWorld, IHoldRoomWorld
{
}

public interface IEnvelopeMissionWorld : IGameTimeWorld, IHoldRoomWorld
{
}

public interface IProductionActivityMissionWorld : IGameTimeWorld, IProductionActivityWorld
{
}

public interface IChiefWorld : IGameTimeWorld, IWalletWorld, ILockdownWorld
{
}

public interface IChapter3DeliveryWorld : IGameTimeWorld, IDeadDropInventoryWorld
{
}

public interface IDeadDropCashWorld : IDeadDropInventoryWorld, IDeadDropCashSlotWorld
{
}

public interface IRelease1ProductionWorld : IRoomMissionWorld, IEnvelopeMissionWorld, IProductionActivityMissionWorld
{
}

public interface IChapter3World : IChapter3DeliveryWorld, IWalletWorld
{
}
