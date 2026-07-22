namespace VmManager.Agent.Services;

public sealed class ProvisionResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }

    public string Output { get; init; } = "";
}
