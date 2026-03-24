using VmManager.Models;

namespace VmManager.Services;

/// <summary>
/// Abstraction for a VM backend (Hyper-V, Docker, etc.).
/// HyperVService implements this today; DockerService would implement it in the future.
/// </summary>
public interface IVmBackend
{
    Task<List<VmInstance>> GetVmsAsync();
    Task StartVmAsync(string name);
    Task StopVmAsync(string name);
    Task DeleteVmAsync(string name);
    Task RenameVmAsync(string currentName, string newName);
    Task<bool> ResetVmAsync(string name);

    Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName);
    Task CreateSnapshotAsync(string vmName, string snapshotName);
    Task RestoreSnapshotAsync(string vmName, string snapshotId);
    Task DeleteSnapshotAsync(string vmName, string snapshotId);

    Task ImportVmAsync(
        string extractedFolder,
        string localVmPath,
        int memoryMb,
        int cpuCount,
        string? vmName = null
    );

    Task ConnectToVmAsync(string vmName, string username = "", string password = "");
}
