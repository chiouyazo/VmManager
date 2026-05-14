using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Proxmox;

public class ProxmoxVmService
{
    private readonly ProxmoxApiClient _api;
    private readonly ILogger<ProxmoxVmService> _logger;

    public ProxmoxVmService(ProxmoxApiClient api, ILogger<ProxmoxVmService> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _logger = logger;
    }

    public async Task<List<VmInstance>> GetVmsAsync()
    {
        _logger.LogDebug("Loading VMs from pool {Pool}", _api.PoolId);
        List<VmInstance> result = new List<VmInstance>();

        try
        {
            JsonElement pool = await _api.GetAsync<JsonElement>($"/api2/json/pools/{_api.PoolId}");
            List<int> vmIds = new List<int>();
            if (pool.TryGetProperty("members", out JsonElement members))
            {
                foreach (JsonElement m in members.EnumerateArray())
                {
                    if (m.TryGetProperty("type", out JsonElement t) && t.GetString() == "qemu")
                        vmIds.Add(m.GetProperty("vmid").GetInt32());
                }
            }

            foreach (int vmid in vmIds)
            {
                try
                {
                    JsonElement status = await _api.GetAsync<JsonElement>(
                        $"{_api.VmPath(vmid)}/status/current"
                    );
                    result.Add(
                        new VmInstance
                        {
                            Name = status.TryGetProperty("name", out JsonElement n)
                                ? n.GetString() ?? $"vm-{vmid}"
                                : $"vm-{vmid}",
                            State = MapState(status.GetProperty("status").GetString() ?? ""),
                            MemoryAssigned = status.TryGetProperty("maxmem", out JsonElement mem)
                                ? mem.GetInt64()
                                : 0,
                            Uptime = status.TryGetProperty("uptime", out JsonElement up)
                                ? TimeSpan.FromSeconds(up.GetInt64())
                                : TimeSpan.Zero,
                            Backend = "Proxmox",
                        }
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get status for VMID {VmId}", vmid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list VMs from pool");
        }

        return result;
    }

    public async Task StartVmAsync(string name)
    {
        int vmid = await ResolveVmIdAsync(name);
        _logger.LogInformation("Starting VM {Name} (VMID {VmId})", name, vmid);
        string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/start");
        await _api.PollTaskAsync(upid);
    }

    public async Task StopVmAsync(string name)
    {
        int vmid = await ResolveVmIdAsync(name);
        _logger.LogInformation("Stopping VM {Name} (VMID {VmId})", name, vmid);

        string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/shutdown");
        try
        {
            await _api.PollTaskAsync(upid, TimeSpan.FromSeconds(30));
            return;
        }
        catch
        {
            _logger.LogWarning("Graceful shutdown timed out for {Name}, forcing stop", name);
        }

        upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/stop");
        await _api.PollTaskAsync(upid);
    }

    public async Task DeleteVmAsync(string name)
    {
        int vmid = await ResolveVmIdAsync(name);
        _logger.LogInformation("Deleting VM {Name} (VMID {VmId})", name, vmid);

        string state = await GetVmStateAsync(vmid);
        if (state != "Off")
        {
            string stopUpid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/stop");
            await _api.PollTaskAsync(stopUpid, TimeSpan.FromSeconds(15));
        }

        await _api.DeleteAsync($"{_api.VmPath(vmid)}?destroy-unreferenced-disks=1&purge=1");
    }

    public async Task RenameVmAsync(string currentName, string newName)
    {
        int vmid = await ResolveVmIdAsync(currentName);
        string state = await GetVmStateAsync(vmid);
        if (state != "Off")
            throw new InvalidOperationException($"VM '{currentName}' must be off to rename.");

        _logger.LogInformation(
            "Renaming VM {Old} to {New} (VMID {VmId})",
            currentName,
            newName,
            vmid
        );
        await _api.PutAsync(
            $"{_api.VmPath(vmid)}/config",
            new Dictionary<string, string> { ["name"] = newName }
        );
    }

    public async Task<bool> ResetVmAsync(string name, ProxmoxSnapshotService snapshots)
    {
        List<VmSnapshot> snaps = await snapshots.GetSnapshotsAsync(name);
        if (snaps.Count == 0)
            return false;
        VmSnapshot oldest = snaps.OrderBy(s => s.CreationTime).First();
        await snapshots.RestoreSnapshotAsync(name, oldest.Id);
        return true;
    }

    public Task ConnectToVmAsync(string vmName, string username, string password)
    {
        _logger.LogWarning("Direct VM console is not supported; use RDP instead");
        return Task.CompletedTask;
    }

    public async Task<int> ResolveVmIdAsync(string name)
    {
        JsonElement pool = await _api.GetAsync<JsonElement>($"/api2/json/pools/{_api.PoolId}");
        if (!pool.TryGetProperty("members", out JsonElement members))
            throw new InvalidOperationException($"Pool '{_api.PoolId}' has no members");

        foreach (JsonElement m in members.EnumerateArray())
        {
            if (m.TryGetProperty("type", out JsonElement t) && t.GetString() != "qemu")
                continue;
            int vmid = m.GetProperty("vmid").GetInt32();
            JsonElement config = await _api.GetAsync<JsonElement>($"{_api.VmPath(vmid)}/config");
            string vmName = config.TryGetProperty("name", out JsonElement n)
                ? n.GetString() ?? ""
                : "";
            if (vmName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return vmid;
        }

        throw new InvalidOperationException($"VM '{name}' not found in pool '{_api.PoolId}'");
    }

    private async Task<string> GetVmStateAsync(int vmid)
    {
        JsonElement status = await _api.GetAsync<JsonElement>(
            $"{_api.VmPath(vmid)}/status/current"
        );
        return MapState(status.GetProperty("status").GetString() ?? "");
    }

    private async Task<string> PostForUpidAsync(
        string path,
        Dictionary<string, string>? data = null
    )
    {
        string raw = await _api.PostRawAsync(path, data);
        using JsonDocument doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("No UPID in response");
    }

    private static string MapState(string proxmoxState) =>
        proxmoxState switch
        {
            "running" => "Running",
            "stopped" => "Off",
            "paused" => "Paused",
            _ => proxmoxState,
        };
}
