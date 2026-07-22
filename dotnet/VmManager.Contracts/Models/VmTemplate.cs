namespace VmManager.Contracts.Models;

/// <summary>
/// A user-created Proxmox VM template that others can create new VMs from.
/// Persisted in vm-templates.json; the underlying Proxmox object is a real
/// template (qemu with template=1) identified by <see cref="TemplateVmId"/>.
/// </summary>
public class VmTemplate
{
    public int TemplateVmId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string SourceVmName { get; set; } = "";
    public int MemoryMb { get; set; }
    public int CpuCount { get; set; }

    /// <summary>Origin of the source VM's image, carried over so VMs created
    /// from this template keep a meaningful lineage.</summary>
    public VmOrigin? Origin { get; set; }
}
