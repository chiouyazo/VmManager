namespace VmManager.Contracts.Interfaces;

public interface IVmIpResolver
{
    Task<string?> ResolveIpAsync(string vmName, CancellationToken cancellationToken = default);
}
