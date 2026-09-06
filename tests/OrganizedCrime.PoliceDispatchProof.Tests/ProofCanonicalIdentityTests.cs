using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class ProofCanonicalIdentityTests
{
    [Theory]
    [InlineData("C:\\Saves\\76561198000000000\\SaveGame_1", true, "76561198000000000")]
    [InlineData("C:\\Saves\\0\\SaveGame_1", false, null)]
    [InlineData("C:\\Saves\\76561198000000000\\slot", false, null)]
    public void Accepts_only_the_save_account_identity_contract(string path, bool expected, string? identity)
    {
        var accepted = ProofCanonicalIdentity.TryGet(path, out var actual);

        Assert.Equal(expected, accepted);
        Assert.Equal(identity, actual);
    }
}
