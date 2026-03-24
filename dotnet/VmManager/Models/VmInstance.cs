namespace VmManager.Models;

/// <summary>A locally registered Hyper-V virtual machine.</summary>
public class VmInstance
{
    public string Name { get; set; } = "";

    /// <summary>Hyper-V state: Running, Off, Starting, Stopping, etc.</summary>
    public string State { get; set; } = "";

    /// <summary>Assigned memory in bytes as reported by Hyper-V.</summary>
    public long MemoryAssigned { get; set; }

    public TimeSpan Uptime { get; set; }

    public string MemoryDisplay => MemoryAssigned == 0 ? "-" : $"{MemoryAssigned / 1024 / 1024} MB";

    /// <summary>Which backend manages this VM: "HyperV" or "Docker".</summary>
    public string Backend { get; set; } = "HyperV";

    /// <summary>Temporary storage for the rename dialog result before the command fires.</summary>
    public string PendingRename { get; set; } = "";

    /// <summary>User notes for this VM (persisted in vm-notes.json).</summary>
    public string Notes { get; set; } = "";
}
