using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface IMetricsProvider
{
    Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken = default);
    Task<List<VmMetrics>> GetVmMetricsAsync(CancellationToken cancellationToken = default);
    Task<List<StorageMetrics>> GetStorageMetricsAsync(
        CancellationToken cancellationToken = default
    );
    Task<List<DiskHealthInfo>> GetDiskHealthAsync(CancellationToken cancellationToken = default);
    Task<VmShutdownReason> GetShutdownReasonAsync(
        string vmName,
        CancellationToken cancellationToken = default
    );
}
