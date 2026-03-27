using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Kvm;

public class KvmVmService
{
    private readonly ShellRunner _sh;
    private readonly ILogger<KvmVmService> _logger;

    public KvmVmService(ShellRunner sh, ILogger<KvmVmService> logger)
    {
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(logger);
        _sh = sh;
        _logger = logger;
    }

    public async Task<List<VmInstance>> GetVmsAsync()
    {
        _logger.LogDebug("Loading VMs via virsh");
        try
        {
            string namesOutput = await _sh.RunBashAsync("virsh list --all --name");
            if (string.IsNullOrWhiteSpace(namesOutput))
                return new List<VmInstance>();

            string[] names = namesOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            List<VmInstance> vms = new List<VmInstance>();

            foreach (string name in names)
            {
                try
                {
                    string info = await _sh.RunBashAsync($"virsh dominfo {Q(name)}");
                    string state = ParseDomInfoField(info, "State");
                    string memoryStr = ParseDomInfoField(info, "Max memory");
                    string cpuStr = ParseDomInfoField(info, "CPU(s)");

                    long memoryKb = 0;
                    if (!string.IsNullOrEmpty(memoryStr))
                    {
                        string numPart = memoryStr.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries
                        )[0];
                        long.TryParse(numPart, out memoryKb);
                    }

                    vms.Add(
                        new VmInstance
                        {
                            Name = name,
                            State = MapLibvirtState(state),
                            MemoryAssigned = memoryKb * 1024,
                            Backend = "KVM",
                        }
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get info for VM {VmName}", name);
                }
            }

            return vms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list VMs via virsh");
            return new List<VmInstance>();
        }
    }

    public async Task StartVmAsync(string name)
    {
        _logger.LogInformation("Starting VM {VmName}", name);
        await _sh.RunBashAsync($"virsh start {Q(name)}");
    }

    public async Task StopVmAsync(string name)
    {
        _logger.LogInformation("Stopping VM {VmName}", name);
        await _sh.RunBashAsync($"virsh shutdown {Q(name)}");

        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            string state = await GetVmStateAsync(name);
            if (state == "Off")
                return;
        }

        _logger.LogWarning("VM {VmName} did not shut down gracefully, forcing destroy", name);
        await _sh.RunBashAsync($"virsh destroy {Q(name)}");
    }

    public async Task DeleteVmAsync(string name)
    {
        _logger.LogInformation("Deleting VM {VmName}", name);
        _logger.LogInformation("Destroying VM {VmName} (if running)", name);
        await _sh.RunBashAsync($"virsh destroy {Q(name)} 2>/dev/null || true");
        _logger.LogInformation("Undefining VM {VmName} with storage removal", name);
        await _sh.RunBashAsync(
            $"virsh undefine {Q(name)} --remove-all-storage --snapshots-metadata --nvram"
        );
    }

    public async Task RenameVmAsync(string currentName, string newName)
    {
        string state = await GetVmStateAsync(currentName);
        if (state != "Off")
            throw new InvalidOperationException(
                $"VM '{currentName}' must be off to rename. Current state: {state}"
            );

        _logger.LogInformation("Renaming VM {CurrentName} to {NewName}", currentName, newName);
        await _sh.RunBashAsync($"virsh domrename {Q(currentName)} {Q(newName)}");
    }

    public async Task<bool> ResetVmAsync(string name, KvmSnapshotService snapshots)
    {
        List<VmSnapshot> snapshotList = await snapshots.GetSnapshotsAsync(name);
        if (snapshotList.Count == 0)
            return false;

        VmSnapshot oldest = snapshotList.OrderBy(s => s.CreationTime).First();
        await snapshots.RestoreSnapshotAsync(name, oldest.Id);
        return true;
    }

    public Task ConnectToVmAsync(string vmName, string username, string password)
    {
        _logger.LogWarning(
            "Direct VM console connection is not supported on headless Linux; use RDP instead"
        );
        return Task.CompletedTask;
    }

    private async Task<string> GetVmStateAsync(string name)
    {
        string info = await _sh.RunBashAsync($"virsh dominfo {Q(name)}");
        string state = ParseDomInfoField(info, "State");
        return MapLibvirtState(state);
    }

    private static string Q(string value) => ShellRunner.Q(value);

    private static string MapLibvirtState(string state) =>
        state switch
        {
            "running" => "Running",
            "shut off" => "Off",
            "paused" => "Paused",
            "idle" => "Running",
            "in shutdown" => "Running",
            "crashed" => "Off",
            "pmsuspended" => "Saved",
            _ => state,
        };

    private static string ParseDomInfoField(string domInfo, string fieldName)
    {
        foreach (string line in domInfo.Split('\n'))
        {
            if (line.StartsWith(fieldName + ":", StringComparison.OrdinalIgnoreCase))
                return line[(fieldName.Length + 1)..].Trim();
        }
        return "";
    }
}
