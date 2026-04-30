namespace VmManager.Contracts.Models;

public class VmAccessEntry
{
    public string VmName { get; set; } = "";
    public string Owner { get; set; } = "";
    public List<VmAccessGrant> Grants { get; set; } = [];
}
