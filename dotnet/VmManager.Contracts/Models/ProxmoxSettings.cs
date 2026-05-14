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
}
