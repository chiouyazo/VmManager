using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Proxmox;

public sealed class ProxmoxMetricsProvider : IMetricsProvider
{
    private readonly ProxmoxApiClient _api;
    private readonly ILogger<ProxmoxMetricsProvider> _logger;

    public ProxmoxMetricsProvider(ProxmoxApiClient api, ILogger<ProxmoxMetricsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _logger = logger;
    }

    public async Task<HostMetrics> GetHostMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            JsonElement node = await _api.GetAsync<JsonElement>(
                "/api2/json/nodes/" + _api.Node + "/status"
            );

            HostMetrics metrics = new HostMetrics
            {
                Hostname = _api.Node,
                CollectedAt = DateTimeOffset.UtcNow,
            };

            if (node.TryGetProperty("cpu", out JsonElement cpu))
                metrics.CpuPercent = cpu.GetDouble() * 100;

            if (node.TryGetProperty("memory", out JsonElement mem))
            {
                if (mem.TryGetProperty("used", out JsonElement used))
                    metrics.MemoryUsedBytes = used.GetInt64();
                if (mem.TryGetProperty("total", out JsonElement total))
                    metrics.MemoryTotalBytes = total.GetInt64();
            }

            if (node.TryGetProperty("uptime", out JsonElement uptime))
                metrics.UptimeSeconds = uptime.GetInt64();

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get host metrics from Proxmox");
            return new HostMetrics { Hostname = _api.Node };
        }
    }

    public async Task<List<VmMetrics>> GetVmMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<VmMetrics> result = new List<VmMetrics>();
        try
        {
            JsonElement pool = await _api.GetAsync<JsonElement>("/api2/json/pools/" + _api.PoolId);
            if (!pool.TryGetProperty("members", out JsonElement members))
                return result;

            foreach (JsonElement member in members.EnumerateArray())
            {
                if (member.GetProperty("type").GetString() != "qemu")
                    continue;

                int vmid = member.GetProperty("vmid").GetInt32();
                string name = member.TryGetProperty("name", out JsonElement n)
                    ? n.GetString() ?? ""
                    : "";

                VmMetrics vm = new VmMetrics
                {
                    Name = name,
                    State = member.TryGetProperty("status", out JsonElement s)
                        ? s.GetString() ?? ""
                        : "",
                    CollectedAt = DateTimeOffset.UtcNow,
                };

                if (member.TryGetProperty("cpu", out JsonElement cpuVal))
                    vm.CpuPercent = cpuVal.GetDouble() * 100;
                if (member.TryGetProperty("mem", out JsonElement memVal))
                    vm.MemoryUsedBytes = memVal.GetInt64();
                if (member.TryGetProperty("maxmem", out JsonElement maxMem))
                    vm.MemoryAssignedBytes = maxMem.GetInt64();
                if (member.TryGetProperty("uptime", out JsonElement uptime))
                    vm.UptimeSeconds = uptime.GetInt64();

                try
                {
                    JsonElement rrd = await _api.GetAsync<JsonElement>(
                        "/api2/json/nodes/"
                            + _api.Node
                            + "/qemu/"
                            + vmid
                            + "/rrddata?timeframe=hour&cf=AVERAGE"
                    );

                    if (rrd.ValueKind == JsonValueKind.Array)
                    {
                        JsonElement last = default;
                        foreach (JsonElement entry in rrd.EnumerateArray())
                            last = entry;

                        if (last.ValueKind == JsonValueKind.Object)
                        {
                            if (last.TryGetProperty("diskread", out JsonElement dr))
                                vm.DiskReadBytesTotal = (long)dr.GetDouble();
                            if (last.TryGetProperty("diskwrite", out JsonElement dw))
                                vm.DiskWriteBytesTotal = (long)dw.GetDouble();
                            if (last.TryGetProperty("netin", out JsonElement ni))
                                vm.NetworkRxBytesTotal = (long)ni.GetDouble();
                            if (last.TryGetProperty("netout", out JsonElement no))
                                vm.NetworkTxBytesTotal = (long)no.GetDouble();
                        }
                    }
                }
                catch
                {
                    // RRD data may not be available for all VMs
                }

                result.Add(vm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get VM metrics from Proxmox");
        }

        return result;
    }

    public async Task<List<StorageMetrics>> GetStorageMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<StorageMetrics> result = new List<StorageMetrics>();
        try
        {
            JsonElement storage = await _api.GetAsync<JsonElement>(
                "/api2/json/nodes/" + _api.Node + "/storage/" + _api.StorageId + "/status"
            );

            result.Add(
                new StorageMetrics
                {
                    Name = _api.StorageId,
                    UsedBytes = storage.TryGetProperty("used", out JsonElement used)
                        ? used.GetInt64()
                        : 0,
                    TotalBytes = storage.TryGetProperty("total", out JsonElement total)
                        ? total.GetInt64()
                        : 0,
                    Type = storage.TryGetProperty("type", out JsonElement type)
                        ? type.GetString() ?? ""
                        : "",
                    CollectedAt = DateTimeOffset.UtcNow,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get storage metrics from Proxmox");
        }

        return result;
    }

    public async Task<List<DiskHealthInfo>> GetDiskHealthAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<DiskHealthInfo> result = new List<DiskHealthInfo>();
        try
        {
            JsonElement disks = await _api.GetAsync<JsonElement>(
                "/api2/json/nodes/" + _api.Node + "/disks/list"
            );

            if (disks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement disk in disks.EnumerateArray())
                {
                    DiskHealthInfo info = new DiskHealthInfo
                    {
                        Device = disk.TryGetProperty("devpath", out JsonElement dev)
                            ? dev.GetString() ?? ""
                            : "",
                        Model = disk.TryGetProperty("model", out JsonElement model)
                            ? model.GetString() ?? ""
                            : "",
                        Serial = disk.TryGetProperty("serial", out JsonElement serial)
                            ? serial.GetString() ?? ""
                            : "",
                        Healthy =
                            disk.TryGetProperty("health", out JsonElement health)
                            && health.GetString() == "PASSED",
                        CollectedAt = DateTimeOffset.UtcNow,
                    };

                    result.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get disk health from Proxmox");
        }

        return result;
    }

    public async Task<VmShutdownReason> GetShutdownReasonAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Resolve VM name to VMID via pool members
            int vmid = -1;
            JsonElement pool = await _api.GetAsync<JsonElement>("/api2/json/pools/" + _api.PoolId);
            if (pool.TryGetProperty("members", out JsonElement members))
            {
                foreach (JsonElement m in members.EnumerateArray())
                {
                    if (
                        m.TryGetProperty("name", out JsonElement n)
                        && string.Equals(n.GetString(), vmName, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        vmid = m.GetProperty("vmid").GetInt32();
                        break;
                    }
                }
            }

            if (vmid < 0)
                return VmShutdownReason.Unknown;

            // Check recent tasks for this VM
            JsonElement tasks = await _api.GetAsync<JsonElement>(
                "/api2/json/nodes/" + _api.Node + "/tasks?vmid=" + vmid + "&limit=5"
            );

            if (tasks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement task in tasks.EnumerateArray())
                {
                    string taskType = task.TryGetProperty("type", out JsonElement t)
                        ? t.GetString() ?? ""
                        : "";
                    long startTime = task.TryGetProperty("starttime", out JsonElement st)
                        ? st.GetInt64()
                        : 0;
                    DateTimeOffset taskTime = DateTimeOffset.FromUnixTimeSeconds(startTime);

                    // Only consider tasks from the last 2 minutes
                    if (DateTimeOffset.UtcNow - taskTime > TimeSpan.FromMinutes(2))
                        continue;

                    // qmstop = force stop (from Proxmox UI or API)
                    // qmshutdown = graceful shutdown request (from Proxmox UI or guest ACPI)
                    if (taskType == "qmstop" || taskType == "qmshutdown")
                        return VmShutdownReason.HostInitiated;
                }
            }

            // No recent stop/shutdown task = guest initiated or crash
            // Check if QEMU agent reported clean shutdown
            return VmShutdownReason.GuestInitiated;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine shutdown reason for {VmName}", vmName);
            return VmShutdownReason.Unknown;
        }
    }
}
