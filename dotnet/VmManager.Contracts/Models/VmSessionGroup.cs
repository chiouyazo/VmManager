namespace VmManager.Contracts.Models;

public class VmSessionGroup
{
    public string VmName { get; set; } = "";
    public string VmState { get; set; } = "";
    public List<ActiveSession> Sessions { get; set; } = new List<ActiveSession>();
}
