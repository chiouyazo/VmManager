using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Backends.Kvm;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends.Proxmox;

public class ProxmoxIpResolver : IVmIpResolver
{
    private readonly ProxmoxApiClient _api;
    private readonly ProxmoxVmService _vms;
    private readonly ShellRunner _sh;
    private readonly ILogger<ProxmoxIpResolver> _logger;

    public ProxmoxIpResolver(
        ProxmoxApiClient api,
        ProxmoxVmService vms,
        ShellRunner sh,
        ILogger<ProxmoxIpResolver> logger
    )
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _vms = vms;
        _sh = sh;
        _logger = logger;
    }

    public async Task<string?> ResolveIpAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        int vmid;
        try
        {
            vmid = await _vms.ResolveVmIdAsync(vmName);
        }
        catch
        {
            _logger.LogDebug("Could not resolve VMID for {VmName}", vmName);
            return null;
        }

        _logger.LogDebug(
            "Resolving IP for {VmName} (VMID {VmId}): trying guest agent",
            vmName,
            vmid
        );
        string? ip = await TryGuestAgentAsync(vmid);
        if (ip != null)
        {
            _logger.LogInformation("Resolved IP for {VmName} via guest agent: {Ip}", vmName, ip);
            return ip;
        }

        _logger.LogDebug("Guest agent failed for {VmName}, trying ARP scan", vmName);
        ip = await TryArpScanAsync(vmid);
        if (ip != null)
            _logger.LogInformation("Resolved IP for {VmName} via ARP scan: {Ip}", vmName, ip);
        else
            _logger.LogDebug("ARP scan found no IP for {VmName}", vmName);
        return ip;
    }

    private async Task<string?> TryGuestAgentAsync(int vmid)
    {
        try
        {
            JsonElement data = await _api.GetAsync<JsonElement>(
                $"{_api.VmPath(vmid)}/agent/network-get-interfaces"
            );
            if (data.TryGetProperty("result", out JsonElement result))
            {
                foreach (JsonElement iface in result.EnumerateArray())
                {
                    if (!iface.TryGetProperty("ip-addresses", out JsonElement addrs))
                        continue;
                    foreach (JsonElement addr in addrs.EnumerateArray())
                    {
                        string? type = addr.TryGetProperty("ip-address-type", out JsonElement t)
                            ? t.GetString()
                            : null;
                        string? ipStr = addr.TryGetProperty("ip-address", out JsonElement a)
                            ? a.GetString()
                            : null;
                        if (type == "ipv4" && ipStr != null && FilterIpv4(ipStr) != null)
                            return ipStr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Guest agent query failed for VMID {VmId}", vmid);
        }
        return null;
    }

    private async Task<string?> TryArpScanAsync(int vmid)
    {
        try
        {
            JsonElement config = await _api.GetAsync<JsonElement>($"{_api.VmPath(vmid)}/config");
            string? mac = null;
            foreach (JsonProperty prop in config.EnumerateObject())
            {
                if (!prop.Name.StartsWith("net"))
                    continue;
                string val = prop.Value.GetString() ?? "";
                int eqIdx = val.IndexOf('=');
                if (eqIdx > 0)
                {
                    string afterEq = val[(eqIdx + 1)..];
                    int comma = afterEq.IndexOf(',');
                    mac = comma > 0 ? afterEq[..comma] : afterEq;
                    break;
                }
            }

            if (string.IsNullOrEmpty(mac))
                return null;

            string? cached = FindMacInArp(await _sh.RunBashAsync("ip neigh"), mac);
            if (cached != null)
                return cached;

            string subnet = _api.VmSubnet;
            if (string.IsNullOrEmpty(subnet))
                return null;

            await _sh.RunBashAsync(
                $"for i in $(seq 1 254); do ping -c 1 -W 0.2 {subnet}.$i > /dev/null 2>&1 & done; wait"
            );
            await Task.Delay(300);

            return FindMacInArp(await _sh.RunBashAsync("ip neigh"), mac);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ARP scan failed for VMID {VmId}", vmid);
        }
        return null;
    }

    private static string? FindMacInArp(string arpOutput, string mac)
    {
        foreach (string line in arpOutput.Split('\n'))
        {
            if (line.Contains(mac, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(' ');
                if (parts.Length > 0 && FilterIpv4(parts[0]) != null)
                    return parts[0];
            }
        }
        return null;
    }

    private static string? FilterIpv4(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress? parsed))
            return null;
        if (parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;
        byte[] bytes = parsed.GetAddressBytes();
        if (bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254))
            return null;
        return address;
    }
}
