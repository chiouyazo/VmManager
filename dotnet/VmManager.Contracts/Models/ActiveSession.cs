namespace VmManager.Contracts.Models;

public class ActiveSession
{
    public string VmName { get; set; } = "";
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTimeOffset ConnectedAt { get; set; }
    public string State { get; set; } = "";
    public double DurationSeconds { get; set; }
}
