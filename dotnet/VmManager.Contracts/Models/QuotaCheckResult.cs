namespace VmManager.Contracts.Models;

public class QuotaCheckResult
{
    public bool Allowed { get; init; }
    public string Reason { get; init; } = "";
}
