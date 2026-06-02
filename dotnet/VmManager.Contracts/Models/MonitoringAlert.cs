namespace VmManager.Contracts.Models;

public class MonitoringAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public AlertSeverity Severity { get; set; }
    public string CheckName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? VmName { get; set; }
    public string? SourceIp { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}
