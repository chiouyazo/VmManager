namespace VmManager.Contracts.Models;

/// <summary>A locally extracted VM image (lives in LocalVmPath/extracted/).</summary>
public record LocalImage
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public double SizeGb { get; set; }
    public DateTime ExtractedAt { get; set; }

    // Origin tracking: persisted in vmmanager.json so we know where this image came from
    public string? FeedId { get; set; }
    public string? FeedUrl { get; set; }
    public string? FeedRepository { get; set; }
    public string? ParentImageId { get; set; }
    public string? ParentImageName { get; set; }
    public string? ImageVersion { get; set; }

    /// <summary>Composed display name: "<feed> / <name> - <version>" when metadata exists, else Name.</summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(ParentImageName) || string.IsNullOrEmpty(ImageVersion))
                return Name;
            string prefix = !string.IsNullOrEmpty(FeedRepository) ? FeedRepository + " / " : "";
            return prefix + ParentImageName + " - " + ImageVersion;
        }
    }
}
