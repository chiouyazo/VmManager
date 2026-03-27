namespace VmManager.Contracts.Models;

public class VmOrigin
{
    public string ImageId { get; set; } = "";
    public string ImageName { get; set; } = "";
    public string Version { get; set; } = "";
    public string FeedId { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public string? Repository { get; set; }
}
