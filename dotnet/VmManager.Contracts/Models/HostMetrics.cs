namespace VmManager.Contracts.Models;

public class HostMetrics
{
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public long UptimeSeconds { get; set; }
    public double? TemperatureCelsius { get; set; }
    public string Hostname { get; set; } = "";
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}
