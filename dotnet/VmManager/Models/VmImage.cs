namespace VmManager.Models;

/// <summary>Represents a VM image entry from the network catalog.</summary>
public class VmImage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Image platform type: "Windows" or "Linux".</summary>
    public string ImageType { get; set; } = "Windows";

    public List<string> Features { get; set; } = [];
    public List<VmImageVersion> Versions { get; set; } = [];
}
