namespace VmManager.Agent.Services;

public sealed class TemplateEntry
{
    public string ImageStorageId { get; set; } = "";
    public int TemplateVmId { get; set; }
    public int DiskSizeGb { get; set; }
    public DateTime CreatedAt { get; set; }
}
