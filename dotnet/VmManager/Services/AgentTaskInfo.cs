namespace VmManager.Services;

public sealed class AgentTaskInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFailed { get; set; }
    public bool IsCancelled { get; set; }
    public string? ErrorMessage { get; set; }
}
