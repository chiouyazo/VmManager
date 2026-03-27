namespace VmManager.Agent.Services;

public sealed class RdpSession
{
    public string Token { get; init; } = "";
    public string VmName { get; init; } = "";
    public string VmIp { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RdpSessionState State { get; set; }
}
