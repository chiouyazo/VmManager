namespace VmManager.Contracts.Models;

public class AgentConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? RdpProxyHost { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool IsLocal { get; set; }
}
