namespace VmManager.Contracts.Models;

public class DiskHealthInfo
{
    public string Device { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public bool Healthy { get; set; } = true;
    public string HealthStatus { get; set; } = "UNKNOWN";
    public double? TemperatureCelsius { get; set; }
    public int? WearLevelPercent { get; set; }
    public Dictionary<string, string> SmartAttributes { get; set; } =
        new Dictionary<string, string>();
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}
