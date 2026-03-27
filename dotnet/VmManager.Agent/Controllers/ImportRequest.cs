namespace VmManager.Agent.Controllers;

public sealed class ImportRequest
{
    public string VersionRef { get; set; } = "";
    public string SafeFileName { get; set; } = "";
    public VmImageVersion? Version { get; set; }
}
