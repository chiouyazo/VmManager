namespace VmManager.Agent.Controllers;

public sealed class CreateVmRequest
{
    public string ExtractedFolder { get; set; } = "";
    public string Name { get; set; } = "";
    public int MemoryMb { get; set; } = 4096;
    public int CpuCount { get; set; } = 4;
    public VmOrigin? Origin { get; set; }
    public List<VmNetworkAdapter>? Networks { get; set; }
}
