namespace VmManager.Contracts.Models;

public class EnvironmentProvisionSpec
{
    public string ScriptBase64 { get; set; } = "";

    public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();

    public int TimeoutSeconds { get; set; } = 1800;
    public ProvisionFailureBehavior OnFailure { get; set; } = ProvisionFailureBehavior.Keep;
}
