using System.Text;
using VmManager.Agent.Services.Monitoring;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Endpoints;

public static class PrometheusEndpoints
{
    public static IEndpointRouteBuilder MapPrometheusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/metrics",
                (MetricsCache cache, AlertStore alertStore) =>
                {
                    StringBuilder sb = new StringBuilder();
                    HostMetrics host = cache.GetHostMetrics();

                    sb.AppendLine("# HELP vmmanager_host_cpu_usage_ratio Host CPU usage (0-1)");
                    sb.AppendLine("# TYPE vmmanager_host_cpu_usage_ratio gauge");
                    sb.AppendLine(
                        "vmmanager_host_cpu_usage_ratio "
                            + (host.CpuPercent / 100).ToString(
                                "F4",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                    );

                    sb.AppendLine("# HELP vmmanager_host_memory_used_bytes Host memory used");
                    sb.AppendLine("# TYPE vmmanager_host_memory_used_bytes gauge");
                    sb.AppendLine("vmmanager_host_memory_used_bytes " + host.MemoryUsedBytes);

                    sb.AppendLine("# HELP vmmanager_host_memory_total_bytes Host memory total");
                    sb.AppendLine("# TYPE vmmanager_host_memory_total_bytes gauge");
                    sb.AppendLine("vmmanager_host_memory_total_bytes " + host.MemoryTotalBytes);

                    sb.AppendLine("# HELP vmmanager_host_uptime_seconds Host uptime");
                    sb.AppendLine("# TYPE vmmanager_host_uptime_seconds gauge");
                    sb.AppendLine("vmmanager_host_uptime_seconds " + host.UptimeSeconds);

                    List<VmMetrics> vms = cache.GetVmMetrics();
                    if (vms.Count > 0)
                    {
                        sb.AppendLine("# HELP vmmanager_vm_cpu_usage_ratio Per-VM CPU usage (0-1)");
                        sb.AppendLine("# TYPE vmmanager_vm_cpu_usage_ratio gauge");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_cpu_usage_ratio{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + (vm.CpuPercent / 100).ToString(
                                        "F4",
                                        System.Globalization.CultureInfo.InvariantCulture
                                    )
                            );

                        sb.AppendLine("# HELP vmmanager_vm_memory_used_bytes Per-VM memory used");
                        sb.AppendLine("# TYPE vmmanager_vm_memory_used_bytes gauge");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_memory_used_bytes{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + vm.MemoryUsedBytes
                            );

                        sb.AppendLine(
                            "# HELP vmmanager_vm_memory_assigned_bytes Per-VM memory assigned"
                        );
                        sb.AppendLine("# TYPE vmmanager_vm_memory_assigned_bytes gauge");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_memory_assigned_bytes{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + vm.MemoryAssignedBytes
                            );

                        sb.AppendLine("# HELP vmmanager_vm_disk_read_bytes_total Per-VM disk read");
                        sb.AppendLine("# TYPE vmmanager_vm_disk_read_bytes_total counter");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_disk_read_bytes_total{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + vm.DiskReadBytesTotal
                            );

                        sb.AppendLine(
                            "# HELP vmmanager_vm_disk_write_bytes_total Per-VM disk write"
                        );
                        sb.AppendLine("# TYPE vmmanager_vm_disk_write_bytes_total counter");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_disk_write_bytes_total{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + vm.DiskWriteBytesTotal
                            );

                        sb.AppendLine(
                            "# HELP vmmanager_vm_net_rx_bytes_total Per-VM network received"
                        );
                        sb.AppendLine("# TYPE vmmanager_vm_net_rx_bytes_total counter");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_net_rx_bytes_total{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + vm.NetworkRxBytesTotal
                            );

                        sb.AppendLine("# HELP vmmanager_vm_net_tx_bytes_total Per-VM network sent");
                        sb.AppendLine("# TYPE vmmanager_vm_net_tx_bytes_total counter");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_net_tx_bytes_total{vm=\""
                                    + vm.Name
                                    + "\"} "
                                    + vm.NetworkTxBytesTotal
                            );

                        sb.AppendLine("# HELP vmmanager_vm_state VM state (1 = in this state)");
                        sb.AppendLine("# TYPE vmmanager_vm_state gauge");
                        foreach (VmMetrics vm in vms)
                            sb.AppendLine(
                                "vmmanager_vm_state{vm=\""
                                    + vm.Name
                                    + "\",state=\""
                                    + vm.State
                                    + "\"} 1"
                            );
                    }

                    List<StorageMetrics> storages = cache.GetStorageMetrics();
                    if (storages.Count > 0)
                    {
                        sb.AppendLine("# HELP vmmanager_storage_used_bytes Storage used");
                        sb.AppendLine("# TYPE vmmanager_storage_used_bytes gauge");
                        foreach (StorageMetrics s in storages)
                            sb.AppendLine(
                                "vmmanager_storage_used_bytes{pool=\""
                                    + s.Name
                                    + "\"} "
                                    + s.UsedBytes
                            );

                        sb.AppendLine("# HELP vmmanager_storage_total_bytes Storage total");
                        sb.AppendLine("# TYPE vmmanager_storage_total_bytes gauge");
                        foreach (StorageMetrics s in storages)
                            sb.AppendLine(
                                "vmmanager_storage_total_bytes{pool=\""
                                    + s.Name
                                    + "\"} "
                                    + s.TotalBytes
                            );
                    }

                    Dictionary<AlertSeverity, int> alertCounts = alertStore.GetActiveAlertCounts();
                    sb.AppendLine(
                        "# HELP vmmanager_alerts_active_total Active alert count by severity"
                    );
                    sb.AppendLine("# TYPE vmmanager_alerts_active_total gauge");
                    foreach (AlertSeverity sev in Enum.GetValues<AlertSeverity>())
                    {
                        int count = alertCounts.GetValueOrDefault(sev, 0);
                        sb.AppendLine(
                            "vmmanager_alerts_active_total{severity=\""
                                + sev.ToString().ToLowerInvariant()
                                + "\"} "
                                + count
                        );
                    }

                    return Results.Text(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
                }
            )
            .AllowAnonymous();

        return endpoints;
    }
}
