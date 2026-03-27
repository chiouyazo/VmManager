namespace VmManager.Contracts.Models;

public class VmSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VmName { get; set; } = "";
    public DateTime CreationTime { get; set; }
    public bool IsBase => string.Equals(Name, "Base", StringComparison.OrdinalIgnoreCase);
    public bool IsNotBase => !IsBase;
}
