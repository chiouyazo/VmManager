using System.IO;

namespace VmManager.Models;

/// <summary>
/// Represents a VM checkpoint that a user has exported to the network share.
/// Lives at: networkRoot\user-shares\{username}\{vmName}_{timestamp}\
/// </summary>
public class UserShare
{
    public string Username { get; set; } = "";
    public string VmName { get; set; } = "";
    public string SnapshotName { get; set; } = "";
    public DateTime ExportedAt { get; set; }

    /// <summary>The timestamped export folder (contains userinfo.json + VM subfolder).</summary>
    public string ExportFolder { get; set; } = "";

    /// <summary>
    /// Path to the VM files subfolder created by Export-VMCheckpoint.
    /// Passed directly to <see cref="HyperVService.ImportVmAsync"/> as the extracted folder.
    /// </summary>
    public string VmFilesFolder => Path.Combine(ExportFolder, VmName);
}
