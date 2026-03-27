namespace VmManager.Contracts.Models;

public class NetworksManifest
{
    public int Version { get; set; } = 1;
    public List<NetworkDefinition> Networks { get; set; } = [];
}
