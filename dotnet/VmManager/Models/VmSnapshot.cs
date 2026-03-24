namespace VmManager.Models;

/// <summary>A Hyper-V checkpoint (snapshot) for a virtual machine.</summary>
public class VmSnapshot
{
    /// <summary>The unique Hyper-V checkpoint ID (GUID).</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string VmName { get; set; } = "";
    public DateTime CreationTime { get; set; }
}
