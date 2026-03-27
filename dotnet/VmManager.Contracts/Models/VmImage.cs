namespace VmManager.Contracts.Models;

public class VmImage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ImageType { get; set; } = "Windows";

    public List<string> Features { get; set; } = new List<string>();

    public string SourceType { get; set; } = "OCI";
    public string FeedName { get; set; } = "";
    public string SourceLabel => !string.IsNullOrEmpty(FeedName) ? FeedName : SourceType;
    public string FeedId { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public string? FeedRepository { get; set; }

    public List<VmImageVersion> Versions { get; set; } = new List<VmImageVersion>();
    public List<VmImageVersion> UserSnapshots { get; set; } = new List<VmImageVersion>();

    public List<NetworkDefinition> AvailableNetworks { get; set; } = [];
}
