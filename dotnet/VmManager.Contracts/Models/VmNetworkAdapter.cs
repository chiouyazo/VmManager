namespace VmManager.Contracts.Models;

public class VmNetworkAdapter
{
    public string NetworkId { get; set; } = "";
    public string? StaticIp { get; set; }
    public string? Gateway { get; set; }
    public string? DnsServers { get; set; }
    public string? MacAddress { get; set; }
    public int? VlanId { get; set; }
}
