// OC-151: tests that build the composition (which assigns OrganizedCrimeLog and
// Chapter3QaAccess) or assign OrganizedCrimeLog themselves run sequentially inside
// this collection. Without it, parallel xUnit workers interleave static assignment
// with assertions and the suite flakes.
// OC-162/OC-144: exclude other collections too; indirect runtime log emitters
// otherwise write into the active scope even without assigning a sink.
using Xunit;

namespace OrganizedCrime.Tests;

[CollectionDefinition("OC-151 composition scope", DisableParallelization = true)]
public sealed class CompositionScopeCollection
{
}
