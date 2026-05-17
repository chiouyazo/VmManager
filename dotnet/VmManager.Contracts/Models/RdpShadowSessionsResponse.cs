namespace VmManager.Contracts.Models;

public class RdpShadowSessionsResponse
{
    public string VmIp { get; set; } = "";
    public List<RdpShadowSession> Sessions { get; set; } = [];
}
