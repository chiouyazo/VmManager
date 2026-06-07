namespace VmManager.Contracts.Models;

public class ProxmoxSettings
{
    public string ApiUrl { get; set; } = "";
    public string ApiTokenId { get; set; } = "";
    public string ApiTokenSecret { get; set; } = "";
    public string Node { get; set; } = "";
    public string StorageId { get; set; } = "vmmanager-storage";
    public string PoolId { get; set; } = "";
    public bool VerifySsl { get; set; } = true;
    public int MaxPoolMemoryMb { get; set; }
    public int MaxPoolCpuCores { get; set; }
    public int VmIdRangeStart { get; set; }
    public int VmIdRangeEnd { get; set; }
    public string DefaultBridge { get; set; } = "vmbr0";
    public int DefaultVlanTag { get; set; }
    public string VmSubnet { get; set; } = "";
    public string ImportMethod { get; set; } = "Standard";
    public int AgentVmId { get; set; }
}
