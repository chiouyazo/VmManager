namespace VmManager.Agent.Services;

public sealed record ProvisionOutcome(
    ProvisionStatus Status,
    string Key,
    string? TaskId = null,
    string? Message = null
);
