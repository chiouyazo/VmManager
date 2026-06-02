using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Kvm;

public sealed class KvmMetricsProvider : IMetricsProvider
{
    private readonly ShellRunner _shell;
    private readonly ILogger<KvmMetricsProvider> _logger;

    public KvmMetricsProvider(ShellRunner shell, ILogger<KvmMetricsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(logger);
        _shell = shell;
        _logger = logger;
    }

    public async Task<HostMetrics> GetHostMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        HostMetrics metrics = new HostMetrics
        {
            Hostname = Environment.MachineName,
            CollectedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            string memInfo = await File.ReadAllTextAsync("/proc/meminfo", cancellationToken);
            foreach (string line in memInfo.Split('\n'))
            {
                if (line.StartsWith("MemTotal:"))
                    metrics.MemoryTotalBytes = ParseKbValue(line) * 1024;
                else if (line.StartsWith("MemAvailable:"))
                    metrics.MemoryUsedBytes = metrics.MemoryTotalBytes - ParseKbValue(line) * 1024;
            }

            string uptimeStr = await File.ReadAllTextAsync("/proc/uptime", cancellationToken);
            string[] parts = uptimeStr.Trim().Split(' ');
            if (
                parts.Length > 0
                && double.TryParse(
                    parts[0],
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double uptime
                )
            )
                metrics.UptimeSeconds = (long)uptime;

            string loadAvg = await File.ReadAllTextAsync("/proc/loadavg", cancellationToken);
            string[] loadParts = loadAvg.Trim().Split(' ');
            if (
                loadParts.Length > 0
                && double.TryParse(
                    loadParts[0],
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double load1
                )
            )
            {
                int cpuCount = Environment.ProcessorCount;
                metrics.CpuPercent = Math.Min(100, load1 / cpuCount * 100);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read host metrics from /proc");
        }

        return metrics;
    }

    public async Task<List<VmMetrics>> GetVmMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<VmMetrics> result = new List<VmMetrics>();
        try
        {
            string output = await _shell.RunBashAsync("virsh domstats --raw 2>/dev/null");
            VmMetrics? current = null;

            foreach (string line in output.Split('\n'))
            {
                if (line.StartsWith("Domain:"))
                {
                    if (current != null)
                        result.Add(current);
                    current = new VmMetrics
                    {
                        Name = line.Substring("Domain: ".Length).Trim().Trim('\''),
                        CollectedAt = DateTimeOffset.UtcNow,
                    };
                }
                else if (current != null && line.Contains('='))
                {
                    string[] kv = line.Trim().Split('=', 2);
                    if (kv.Length == 2 && long.TryParse(kv[1], out long val))
                    {
                        if (kv[0] == "balloon.current")
                            current.MemoryUsedBytes = val * 1024;
                        else if (kv[0] == "balloon.maximum")
                            current.MemoryAssignedBytes = val * 1024;
                        else if (kv[0] == "block.0.rd.bytes")
                            current.DiskReadBytesTotal = val;
                        else if (kv[0] == "block.0.wr.bytes")
                            current.DiskWriteBytesTotal = val;
                        else if (kv[0] == "net.0.rx.bytes")
                            current.NetworkRxBytesTotal = val;
                        else if (kv[0] == "net.0.tx.bytes")
                            current.NetworkTxBytesTotal = val;
                    }
                }
            }

            if (current != null)
                result.Add(current);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get VM metrics via virsh domstats");
        }

        return result;
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

    public async Task<VmShutdownReason> GetShutdownReasonAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string output = await _shell.RunBashAsync(
                "virsh domstate " + vmName + " --reason 2>/dev/null"
            );
            string reason = output.Trim().ToLowerInvariant();

            if (reason.Contains("shut off (user)") || reason.Contains("shut off (shutdown)"))
                return VmShutdownReason.GuestInitiated;
            if (reason.Contains("shut off (destroyed)"))
                return VmShutdownReason.HostInitiated;
            if (reason.Contains("shut off (crashed)") || reason.Contains("shut off (failed)"))
                return VmShutdownReason.Crashed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get shutdown reason for {VmName}", vmName);
        }

        return VmShutdownReason.Unknown;
    }

    private static long ParseKbValue(string line)
    {
        string[] parts = line.Split(':', 2);
        if (parts.Length < 2)
            return 0;
        string value = parts[1].Trim().Replace("kB", "").Trim();
        return long.TryParse(value, out long result) ? result : 0;
    }
}
