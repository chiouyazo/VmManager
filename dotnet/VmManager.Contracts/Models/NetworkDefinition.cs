namespace VmManager.Contracts.Models;

public class NetworkDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public SwitchType SwitchType { get; set; }

    public string? PhysicalAdapter { get; set; }
    public bool AllowManagementOs { get; set; } = true;

    public string? NatSubnet { get; set; }
    public string? NatGateway { get; set; }
    public string? DhcpRangeStart { get; set; }
    public string? DhcpRangeEnd { get; set; }

    public int? VlanId { get; set; }
    public long? MinimumBandwidthAbsolute { get; set; }
    public long? MaximumBandwidth { get; set; }
}
