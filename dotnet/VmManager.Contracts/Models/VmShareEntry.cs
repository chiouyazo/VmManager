namespace VmManager.Contracts.Models;

public class VmShareEntry
{
    public string VmName { get; set; } = "";
    public string OwnerUsername { get; set; } = "";
    public string SharedWithUsername { get; set; } = "";
    public HashSet<string> GrantedPermissions { get; set; } = [];
    public DateTime SharedAt { get; set; }
}
