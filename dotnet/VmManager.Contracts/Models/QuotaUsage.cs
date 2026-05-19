namespace VmManager.Contracts.Models;

public class QuotaUsage
{
    public int VmsOwned { get; init; }
    public int MaxVms { get; init; }
    public int GlobalVmCount { get; init; }
    public int GlobalMaxVms { get; init; }
}
