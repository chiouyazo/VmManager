using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Proxmox;

public class ProxmoxNetworkService : INetworkService
{
    private readonly ProxmoxApiClient _api;
    private readonly ProxmoxVmService _vms;
    private readonly ILogger<ProxmoxNetworkService> _logger;

    public ProxmoxNetworkService(
        ProxmoxApiClient api,
        ProxmoxVmService vms,
        ILogger<ProxmoxNetworkService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _vms = vms;
        _logger = logger;
    }

    public async Task<List<SwitchInfo>> GetSwitchesAsync()
    {
        List<SwitchInfo> result = new List<SwitchInfo>();
        try
        {
            JsonElement networks = await _api.GetAsync<JsonElement>(
                $"/api2/json/nodes/{_api.Node}/network"
            );
            foreach (JsonElement net in networks.EnumerateArray())
            {
                string type = net.TryGetProperty("type", out JsonElement t)
                    ? t.GetString() ?? ""
                    : "";
                if (type != "bridge")
                    continue;

                string iface = net.GetProperty("iface").GetString() ?? "";
                bool hasGateway = net.TryGetProperty("gateway", out _);

                result.Add(new SwitchInfo(iface, hasGateway ? "NAT" : "Internal", null));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list network bridges");
        }
        return result;
    }

    public Task CreateSwitchAsync(string switchName, NetworkDefinition definition)
    {
        _logger.LogWarning(
            "Creating bridges on Proxmox requires host-level network configuration. "
                + "Bridge {Name} should be pre-configured in /etc/network/interfaces.",
            switchName
        );
        return Task.CompletedTask;
    }

    public Task UpdateSwitchAsync(string switchName, NetworkDefinition definition)
    {
        _logger.LogWarning("Updating bridges is not supported on Proxmox");
        return Task.CompletedTask;
    }

    public Task RemoveSwitchAsync(string switchName)
    {
        _logger.LogWarning("Removing bridges is not supported on Proxmox");
        return Task.CompletedTask;
    }

    public async Task ConfigureVmAdaptersAsync(
        string vmName,
        List<(string SwitchName, VmNetworkAdapter Config)> adapters
    )
    {
        int vmid = await _vms.ResolveVmIdAsync(vmName);
        _logger.LogInformation("Configuring network adapters for VM {Name}", vmName);

        for (int i = 0; i < adapters.Count; i++)
        {
            (string bridge, VmNetworkAdapter config) = adapters[i];
            string value = $"e1000e,bridge={bridge}";
            if (config.VlanId > 0)
                value += $",tag={config.VlanId}";
            if (!string.IsNullOrEmpty(config.MacAddress))
                value = $"e1000e={config.MacAddress},bridge={bridge}";

            await _api.PutAsync(
                $"{_api.VmPath(vmid)}/config",
                new Dictionary<string, string> { [$"net{i}"] = value }
            );
        }
    }

    public Task ConfigureGuestIpAsync(
        string vmName,
        string username,
        string password,
        List<VmNetworkAdapter> adapters
    )
    {
        _logger.LogWarning("Guest IP configuration is not supported on Proxmox");
        return Task.CompletedTask;
    }
}
