using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface IGameTimeWorld
{
    Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context);
    Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes);
}
