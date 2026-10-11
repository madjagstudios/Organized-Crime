using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface IProductionActivityWorld
{
    /// <summary>One read of every owned property, its employees and its stations. Read only, one call per pass,
    /// and the only native surface this mission has. Grow containers, customers and dealers are never read.</summary>
    Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity);
}
