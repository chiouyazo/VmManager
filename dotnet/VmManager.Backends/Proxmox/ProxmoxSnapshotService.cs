using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Proxmox;

public class ProxmoxSnapshotService
{
    private readonly ProxmoxApiClient _api;
    private readonly ProxmoxVmService _vms;
    private readonly ILogger<ProxmoxSnapshotService> _logger;

    public ProxmoxSnapshotService(
        ProxmoxApiClient api,
        ProxmoxVmService vms,
        ILogger<ProxmoxSnapshotService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _vms = vms;
        _logger = logger;
    }

    public async Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName)
    {
        try
        {
            int vmid = await _vms.ResolveVmIdAsync(vmName);
            JsonElement snapshots = await _api.GetAsync<JsonElement>(
                $"{_api.VmPath(vmid)}/snapshot"
            );

            List<VmSnapshot> result = new List<VmSnapshot>();
            foreach (JsonElement s in snapshots.EnumerateArray())
            {
                string name = s.GetProperty("name").GetString() ?? "";
                if (name == "current")
                    continue;

                DateTime creationTime = DateTime.MinValue;
                if (s.TryGetProperty("snaptime", out JsonElement st))
                    creationTime = DateTimeOffset.FromUnixTimeSeconds(st.GetInt64()).LocalDateTime;

                result.Add(
                    new VmSnapshot
                    {
                        Id = $"{vmName}:{name}",
                        Name = name,
                        VmName = vmName,
                        CreationTime = creationTime,
                    }
                );
            }
            return result.OrderByDescending(s => s.CreationTime).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshots for VM {VmName}", vmName);
            return new List<VmSnapshot>();
        }
    }

    public async Task CreateSnapshotAsync(string vmName, string snapshotName)
    {
        int vmid = await _vms.ResolveVmIdAsync(vmName);
        _logger.LogInformation("Creating snapshot {Snap} for VM {Name}", snapshotName, vmName);
        string raw = await _api.PostRawAsync(
            $"{_api.VmPath(vmid)}/snapshot",
            new Dictionary<string, string> { ["snapname"] = snapshotName }
        );
        string upid = JsonDocument.Parse(raw).RootElement.GetProperty("data").GetString()!;
        await _api.PollTaskAsync(upid);
    }

    public async Task RestoreSnapshotAsync(string vmName, string snapshotId)
    {
        string snapshotName = ParseSnapshotName(snapshotId);
        int vmid = await _vms.ResolveVmIdAsync(vmName);
        _logger.LogInformation("Restoring VM {Name} to snapshot {Snap}", vmName, snapshotName);
        string raw = await _api.PostRawAsync(
            $"{_api.VmPath(vmid)}/snapshot/{Uri.EscapeDataString(snapshotName)}/rollback"
        );
        string upid = JsonDocument.Parse(raw).RootElement.GetProperty("data").GetString()!;
        await _api.PollTaskAsync(upid);
    }

    public async Task DeleteSnapshotAsync(string vmName, string snapshotId)
    {
        string snapshotName = ParseSnapshotName(snapshotId);
        int vmid = await _vms.ResolveVmIdAsync(vmName);
        _logger.LogInformation("Deleting snapshot {Snap} for VM {Name}", snapshotName, vmName);
        string raw = await _api.DeleteRawAsync(
            $"{_api.VmPath(vmid)}/snapshot/{Uri.EscapeDataString(snapshotName)}"
        );
        string upid = JsonDocument.Parse(raw).RootElement.GetProperty("data").GetString()!;
        await _api.PollTaskAsync(upid);
    }

    public async Task ExportSnapshotAsync(string snapshotId, string destDir)
    {
        (string vmName, string snapshotName) = ParseCompositeId(snapshotId);
        int vmid = await _vms.ResolveVmIdAsync(vmName);

        _logger.LogInformation(
            "Exporting snapshot {Snap} for VM {Name} to {Dir}",
            snapshotName,
            vmName,
            destDir
        );

        await RestoreSnapshotAsync(vmName, snapshotId);

        JsonElement config = await _api.GetAsync<JsonElement>($"{_api.VmPath(vmid)}/config");
        string? diskPath = null;
        if (config.TryGetProperty("sata0", out JsonElement sata))
        {
            string sataStr = sata.GetString() ?? "";
            int comma = sataStr.IndexOf(',');
            string volId = comma > 0 ? sataStr[..comma] : sataStr;
            string storage = volId.Split(':')[0];
            string path = volId.Split(':')[1];
            diskPath = $"/mnt/{storage}/images/{path}";
        }

        if (diskPath != null && File.Exists(diskPath))
        {
            Directory.CreateDirectory(destDir);
            File.Copy(diskPath, Path.Combine(destDir, "disk.qcow2"), true);
            File.WriteAllText(
                Path.Combine(destDir, "metadata.json"),
                "{\"provider\": \"proxmox\"}"
            );
        }
    }

    private static string ParseSnapshotName(string snapshotId)
    {
        int colon = snapshotId.IndexOf(':');
        return colon >= 0 ? snapshotId[(colon + 1)..] : snapshotId;
    }

    private static (string VmName, string SnapshotName) ParseCompositeId(string id)
    {
        int colon = id.IndexOf(':');
        if (colon < 0)
            throw new ArgumentException($"Invalid snapshot ID format: {id}");
        return (id[..colon], id[(colon + 1)..]);
    }
}
