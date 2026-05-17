namespace VmManager.Contracts.Models;

public class RdpShadowSession
{
    public string SessionName { get; set; } = "";
    public string Username { get; set; } = "";
    public int SessionId { get; set; }
    public string State { get; set; } = "";
}
