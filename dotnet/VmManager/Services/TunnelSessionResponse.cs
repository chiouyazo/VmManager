namespace VmManager.Services;

public sealed class TunnelSessionResponse
{
    public string Token { get; set; } = "";
    public string VmName { get; set; } = "";
    public int RemotePort { get; set; }
}
