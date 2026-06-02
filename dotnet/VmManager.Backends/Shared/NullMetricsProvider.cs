using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Shared;

public sealed class NullMetricsProvider : IMetricsProvider
{
    public Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HostMetrics());
    }

    public Task<List<VmMetrics>> GetVmMetricsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<VmMetrics>());
    }

    public Task<List<StorageMetrics>> GetStorageMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new List<StorageMetrics>());
    }

    public Task<List<DiskHealthInfo>> GetDiskHealthAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new List<DiskHealthInfo>());
    }

    public Task<VmShutdownReason> GetShutdownReasonAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(VmShutdownReason.Unknown);
    }
}
