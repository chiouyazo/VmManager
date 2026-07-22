namespace VmManager.Contracts.Models;

public class EnvironmentProvisionRequest
{
    public string Key { get; set; } = "";

    public EnvironmentExistsBehavior IfExists { get; set; } = EnvironmentExistsBehavior.Replace;

    public string Owner { get; set; } = "";

    public List<string> AccessEmails { get; set; } = [];

    public string Image { get; set; } = "";
    public string Version { get; set; } = "";

    public int MemoryMb { get; set; } = 4096;
    public int CpuCount { get; set; } = 4;

    public int TtlMinutes { get; set; }

    public Dictionary<string, string> Labels { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public EnvironmentProvisionSpec Provision { get; set; } = new EnvironmentProvisionSpec();
}
