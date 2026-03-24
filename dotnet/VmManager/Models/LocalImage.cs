namespace VmManager.Models;

/// <summary>A locally extracted VM image (lives in LocalVmPath/extracted/).</summary>
public record LocalImage
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public double SizeGb { get; set; }
    public DateTime ExtractedAt { get; set; }
}
