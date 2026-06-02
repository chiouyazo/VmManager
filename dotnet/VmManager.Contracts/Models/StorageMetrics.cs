namespace VmManager.Contracts.Models;

public class StorageMetrics
{
    public string Name { get; set; } = "";
    public long UsedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string Type { get; set; } = "";
    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}
