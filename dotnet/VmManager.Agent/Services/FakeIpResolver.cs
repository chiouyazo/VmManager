namespace VmManager.Agent.Services;

public sealed class FakeIpResolver : IVmIpResolver
{
    public Task<string?> ResolveIpAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<string?>("10.0.0.1");
    }
}
