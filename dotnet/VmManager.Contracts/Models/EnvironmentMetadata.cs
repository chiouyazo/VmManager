namespace VmManager.Contracts.Models;

public class EnvironmentMetadata
{
    public string Key { get; set; } = "";
    public string VmName { get; set; } = "";
    public string Owner { get; set; } = "";

    public Dictionary<string, string> Labels { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public EnvironmentStatus Status { get; set; } = EnvironmentStatus.Provisioning;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? LastError { get; set; }
    public string? ProvisionLogPath { get; set; }

    public List<string> AccessEmails { get; set; } = [];
}
