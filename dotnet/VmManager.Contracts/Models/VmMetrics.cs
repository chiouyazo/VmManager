namespace VmManager.Contracts.Models;

public class VmMetrics
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryAssignedBytes { get; set; }
    public long DiskReadBytesTotal { get; set; }
    public long DiskWriteBytesTotal { get; set; }
    public long NetworkRxBytesTotal { get; set; }
    public long NetworkTxBytesTotal { get; set; }
    public long UptimeSeconds { get; set; }
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}
