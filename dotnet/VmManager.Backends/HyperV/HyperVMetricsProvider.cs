using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.HyperV;

public sealed class HyperVMetricsProvider : IMetricsProvider
{
    private readonly HyperVWmiHelper _wmi;
    private readonly PowerShellRunner _ps;
    private readonly ILogger<HyperVMetricsProvider> _logger;

    public HyperVMetricsProvider(
        HyperVWmiHelper wmi,
        PowerShellRunner ps,
        ILogger<HyperVMetricsProvider> logger
    )
    {
        ArgumentNullException.ThrowIfNull(wmi);
        ArgumentNullException.ThrowIfNull(ps);
        ArgumentNullException.ThrowIfNull(logger);
        _wmi = wmi;
        _ps = ps;
        _logger = logger;
    }

    public async Task<HostMetrics> GetHostMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string output = await _ps.RunPsAsync(
                "Get-CimInstance Win32_OperatingSystem | Select-Object "
                    + "FreePhysicalMemory,TotalVisibleMemorySize,LastBootUpTime | ConvertTo-Json"
            );

            HostMetrics metrics = new HostMetrics
            {
                Hostname = Environment.MachineName,
                CollectedAt = DateTimeOffset.UtcNow,
            };

            if (!string.IsNullOrEmpty(output))
            {
                System.Text.Json.JsonElement json =
                    System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                        output
                    );
                long totalKb = json.GetProperty("TotalVisibleMemorySize").GetInt64();
                long freeKb = json.GetProperty("FreePhysicalMemory").GetInt64();
                metrics.MemoryTotalBytes = totalKb * 1024;
                metrics.MemoryUsedBytes = (totalKb - freeKb) * 1024;
            }

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get host metrics via WMI");
            return new HostMetrics { Hostname = Environment.MachineName };
        }
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
