namespace VmManager.Contracts.Models;

public class ManagedNetwork
{
    public string NetworkId { get; set; } = "";
    public string SwitchName { get; set; } = "";
    public string ConfigHash { get; set; } = "";
    public int ReferenceCount { get; set; }
    public List<string> VmNames { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
}
