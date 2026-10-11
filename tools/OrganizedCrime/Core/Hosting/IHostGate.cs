namespace OrganizedCrime.Core.Hosting;

/// <summary>The injectable host-check seam behind the FishNet server read.</summary>
public interface IHostGate
{
    bool IsHost { get; }
}
