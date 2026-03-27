using System.Net;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends.Kvm;

public class KvmIpResolver : IVmIpResolver
{
    private readonly ShellRunner _sh;
    private readonly ILogger<KvmIpResolver> _logger;

    public KvmIpResolver(ShellRunner sh, ILogger<KvmIpResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(logger);
        _sh = sh;
        _logger = logger;
    }

    public async Task<string?> ResolveIpAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("Resolving IP address for VM {VmName}", vmName);

        string? ip = await TryResolveViaAgentAsync(vmName);

        if (ip == null)
            ip = await TryResolveViaDomifaddrAsync(vmName);

        if (ip == null)
            ip = await TryResolveViaLeaseSourceAsync(vmName);

        if (ip == null)
            ip = await TryResolveViaDhcpLeasesAsync(vmName);

        if (ip == null)
        {
            _logger.LogWarning(
                "No IPv4 address found for VM {VmName}. Is the VM running with qemu-guest-agent?",
                vmName
            );
        }

        return ip;
    }

    private async Task<string?> TryResolveViaAgentAsync(string vmName)
    {
        try
        {
            string output = await _sh.RunBashAsync(
                $"virsh domifaddr {ShellRunner.Q(vmName)} --source agent 2>/dev/null"
            );
            return ParseDomifaddrOutput(output);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryResolveViaDomifaddrAsync(string vmName)
    {
        try
        {
            string output = await _sh.RunBashAsync(
                $"virsh domifaddr {ShellRunner.Q(vmName)} 2>/dev/null"
            );
            return ParseDomifaddrOutput(output);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryResolveViaLeaseSourceAsync(string vmName)
    {
        try
        {
            string output = await _sh.RunBashAsync(
                $"virsh domifaddr {ShellRunner.Q(vmName)} --source lease 2>/dev/null"
            );
            return ParseDomifaddrOutput(output);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryResolveViaDhcpLeasesAsync(string vmName)
    {
        try
        {
            string iflistOutput = await _sh.RunBashAsync(
                $"virsh domiflist {ShellRunner.Q(vmName)} 2>/dev/null"
            );

            string? mac = null;
            List<string> networks = new();
            foreach (string line in iflistOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('-') || trimmed.StartsWith("Interface"))
                    continue;
                string[] cols = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length >= 5)
                {
                    mac ??= cols[4];
                    if (cols[1] == "network" && !string.IsNullOrEmpty(cols[2]))
                        networks.Add(cols[2]);
                }
            }

            if (string.IsNullOrEmpty(mac) || mac == "-")
                return null;

            if (networks.Count == 0)
                networks.Add("default");

            foreach (string network in networks)
            {
                string leases = await _sh.RunBashAsync(
                    $"virsh net-dhcp-leases {ShellRunner.Q(network)} 2>/dev/null | grep -i {ShellRunner.Q(mac)} || true"
                );
                foreach (string line in leases.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (string part in parts)
                    {
                        string candidate = part.Contains('/') ? part.Split('/')[0] : part;
                        string? filtered = FilterIpv4(candidate);
                        if (filtered != null)
                            return filtered;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private string? ParseDomifaddrOutput(string output)
    {
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string candidate = part.Contains('/') ? part.Split('/')[0] : part;
                string? filtered = FilterIpv4(candidate);
                if (filtered != null)
                {
                    _logger.LogDebug("Resolved VM to {IpAddress} via virsh domifaddr", filtered);
                    return filtered;
                }
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
        if (bytes[0] == 169 && bytes[1] == 254)
            return null;

        return address;
    }
}
