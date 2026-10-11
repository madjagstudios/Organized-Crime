// OC-168: each class arranges and clears the production static checkpoint map.
// Serialize the complete tests, not just individual dictionary operations.
using Xunit;

namespace OrganizedCrime.Tests;

[CollectionDefinition("OC-168 checkpoint swap map", DisableParallelization = true)]
public sealed class OcCheckpointSwapStateCollection
{
}
