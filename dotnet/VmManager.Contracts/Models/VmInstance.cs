namespace VmManager.Contracts.Models;

public class VmInstance
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public long MemoryAssigned { get; set; }
    public TimeSpan Uptime { get; set; }

    public string MemoryDisplay => MemoryAssigned == 0 ? "-" : $"{MemoryAssigned / 1024 / 1024} MB";

    public string Backend { get; set; } = "HyperV";
    public bool IsManaged { get; set; }

    public string GroupKey => IsManaged ? Backend : Backend + "_External";

    public VmOrigin? Origin { get; set; }

    public string? OriginDisplay =>
        Origin != null && !string.IsNullOrEmpty(Origin.ImageName)
            ? $"{Origin.ImageName} v{Origin.Version}"
            : null;

    public string Notes { get; set; } = "";

    public string Owner { get; set; } = "";
    public List<string> SharedWith { get; set; } = [];
    public HashSet<string> EffectivePermissions { get; set; } = [];

    public bool IsRunning => string.Equals(State, "Running", StringComparison.OrdinalIgnoreCase);
    public bool IsOff => string.Equals(State, "Off", StringComparison.OrdinalIgnoreCase);
}
