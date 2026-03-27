using System.Text.Json.Serialization;

namespace VmManager.Contracts.Models;

public class VmImageVersion
{
    public string Version { get; set; } = "";
    public string FileName { get; set; } = "";
    public double SizeGb { get; set; }
    public DateTime Date { get; set; }
    public string Notes { get; set; } = "";

    public bool IsLocallyAvailable { get; set; }

    [JsonIgnore]
    public bool IsUserSnapshot { get; set; }

    [JsonIgnore]
    public string PushedBy { get; set; } = "";

    public string ParentImageId { get; set; } = "";
    public string ParentImageName { get; set; } = "";
    public string FeedId { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public string? FeedRepository { get; set; }

    public List<VmNetworkAdapter>? Networks { get; set; }
}
