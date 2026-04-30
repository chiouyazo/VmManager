namespace VmManager.Agent.Services;

public sealed class TunnelSession
{
    public string Token { get; init; } = "";
    public string VmName { get; init; } = "";
    public string VmIp { get; init; } = "";
    public int RemotePort { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TunnelSessionState State { get; set; }
}
