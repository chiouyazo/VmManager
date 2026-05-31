using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

public sealed class FakeVmBackend : IVmBackend
{
    private readonly List<VmInstance> _vms = new List<VmInstance>
    {
        new VmInstance
        {
            Name = "Dev-Win11",
            State = "Running",
            MemoryAssigned = 4L * 1024 * 1024 * 1024,
            IsManaged = true,
            Backend = "Proxmox",
        },
        new VmInstance
        {
            Name = "Test-Server",
            State = "Off",
            MemoryAssigned = 8L * 1024 * 1024 * 1024,
            IsManaged = true,
            Backend = "Proxmox",
        },
        new VmInstance
        {
            Name = "Build-Agent",
            State = "Running",
            MemoryAssigned = 2L * 1024 * 1024 * 1024,
            IsManaged = true,
            Backend = "Proxmox",
        },
        new VmInstance
        {
            Name = "Staging",
            State = "Saved",
            MemoryAssigned = 4L * 1024 * 1024 * 1024,
            IsManaged = true,
            Backend = "Proxmox",
        },
        new VmInstance
        {
            Name = "Legacy-App",
            State = "Off",
            IsManaged = false,
            Backend = "Proxmox",
        },
        new VmInstance
        {
            Name = "Docker-Host",
            State = "Running",
            MemoryAssigned = 16L * 1024 * 1024 * 1024,
            IsManaged = true,
            Backend = "Proxmox",
        },
    };

    public Task<List<VmInstance>> GetVmsAsync() => Task.FromResult(_vms.ToList());

    public Task StartVmAsync(string name)
    {
        VmInstance? vm = _vms.Find(v => v.Name == name);
        if (vm != null)
            vm.State = "Running";
        return Task.CompletedTask;
    }

    public Task StopVmAsync(string name)
    {
        VmInstance? vm = _vms.Find(v => v.Name == name);
        if (vm != null)
            vm.State = "Off";
        return Task.CompletedTask;
    }

    public Task DeleteVmAsync(string name)
    {
        _vms.RemoveAll(v => v.Name == name);
        return Task.CompletedTask;
    }

    public Task RenameVmAsync(string currentName, string newName)
    {
        VmInstance? vm = _vms.Find(v => v.Name == currentName);
        if (vm != null)
            vm.Name = newName;
        return Task.CompletedTask;
    }

    public Task<bool> ResetVmAsync(string name) => Task.FromResult(true);

    public Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName) =>
        Task.FromResult(
            new List<VmSnapshot>
            {
                new VmSnapshot
                {
                    Id = "base",
                    Name = "Base",
                    CreationTime = DateTime.UtcNow.AddDays(-30),
                },
                new VmSnapshot
                {
                    Id = "pre-update",
                    Name = "Pre-Update",
                    CreationTime = DateTime.UtcNow.AddDays(-2),
                },
            }
        );

    public Task CreateSnapshotAsync(string vmName, string snapshotName) => Task.CompletedTask;

    public Task RestoreSnapshotAsync(string vmName, string snapshotId) => Task.CompletedTask;

    public Task DeleteSnapshotAsync(string vmName, string snapshotId) => Task.CompletedTask;

    public Task ExportSnapshotAsync(string snapshotId, string destDir) => Task.CompletedTask;

    public Task CloneVmFromSnapshotAsync(string vmName, string snapshotName, string newVmName) =>
        Task.CompletedTask;

    public Task ResetDiskAsync(string name) => Task.CompletedTask;

    public Task ConnectToVmAsync(string vmName, string username = "", string password = "") =>
        Task.CompletedTask;

    public Task<string?> TroubleshootAsync() => Task.FromResult<string?>(null);

    public Task ImportVmAsync(
        string extractedFolder,
        string localVmPath,
        int memoryMb,
        int cpuCount,
        string? vmName = null,
        bool skipDefaultNetwork = false,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ConfigureLocaleAsync(
        string vmName,
        string username,
        string password,
        string locale = "de-DE",
        string keyboardLayout = "00000407",
        string timezone = "",
        Action<string>? onStatus = null
    ) => Task.CompletedTask;

    public Task RunPostCreationAsync(
        string vmName,
        string username,
        string password,
        bool renameComputer,
        string? postCreationScript = null,
        Action<string>? onStatus = null
    ) => Task.CompletedTask;
}
