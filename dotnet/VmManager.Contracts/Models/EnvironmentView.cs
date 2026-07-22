namespace VmManager.Contracts.Models;

public class EnvironmentView
{
    public string Key { get; set; } = "";
    public string VmName { get; set; } = "";
    public string Owner { get; set; } = "";
    public Dictionary<string, string> Labels { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public EnvironmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? LastError { get; set; }
    public List<string> AccessEmails { get; set; } = [];

    public string? RdpTarget { get; set; }

    public string? TaskId { get; set; }
}
