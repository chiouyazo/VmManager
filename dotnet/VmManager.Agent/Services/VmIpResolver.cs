using System.Management;
using System.Net;
using VmManager.Backends.HyperV;

namespace VmManager.Agent.Services;

public class VmIpResolver : IVmIpResolver
{
    private readonly ILogger<VmIpResolver> _logger;
    private readonly HyperVWmiHelper _wmi;
    private readonly PowerShellRunner _ps;

    public VmIpResolver(ILogger<VmIpResolver> logger, HyperVWmiHelper wmi, PowerShellRunner ps)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(wmi);
        ArgumentNullException.ThrowIfNull(ps);
        _logger = logger;
        _wmi = wmi;
        _ps = ps;
    }

    public async Task<string?> ResolveIpAsync(
        string vmName,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("Resolving IP address for VM {VmName}", vmName);

        string? ip = await Task.Run(() => ResolveViaWmi(vmName), cancellationToken);

        if (ip == null)
        {
            _logger.LogDebug("WMI resolution failed for {VmName}, trying PowerShell", vmName);
            ip = await ResolveViaPowerShellAsync(vmName);
        }

        if (ip == null)
        {
            _logger.LogWarning(
                "No IPv4 address found for VM {VmName}. Is the VM running with integration services?",
                vmName
            );
        }

        return ip;
    }

    private string? ResolveViaWmi(string vmName)
    {
        ManagementObject? vm = _wmi.GetVm(vmName);
        if (vm == null)
        {
            _logger.LogWarning("VM {VmName} not found via WMI", vmName);
            return null;
        }

        string vmGuid = (string)vm["Name"];

        SelectQuery query = new SelectQuery(
            "Msvm_GuestNetworkAdapterConfiguration",
            $"InstanceID LIKE 'Microsoft:{vmGuid}%'"
        );

        using ManagementObjectSearcher searcher = new ManagementObjectSearcher(_wmi.Scope, query);

        foreach (ManagementObject adapter in searcher.Get().Cast<ManagementObject>())
        {
            string[]? addresses = adapter["IPAddresses"] as string[];
            if (addresses == null)
                continue;

            foreach (string address in addresses)
            {
                string? filtered = FilterIpv4(address);
                if (filtered != null)
                {
                    _logger.LogDebug(
                        "Resolved VM {VmName} to {IpAddress} via WMI",
                        vmName,
                        filtered
                    );
                    return filtered;
                }
            }
        }

        return null;
    }

    private async Task<string?> ResolveViaPowerShellAsync(string vmName)
    {
        try
        {
            string escapedName = vmName.Replace("'", "''");
            string script =
                $"(Get-VMNetworkAdapter -VMName '{escapedName}' -ErrorAction Stop).IPAddresses | "
                + "Where-Object { $_ -match '^\\d+\\.\\d+\\.\\d+\\.\\d+$' -and $_ -notlike '169.254.*' } | "
                + "Select-Object -First 1";

            string output = await _ps.RunPsAsync(script);
            string? ip = output.Trim();

            if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out _))
            {
                _logger.LogDebug("Resolved VM {VmName} to {IpAddress} via PowerShell", vmName, ip);
                return ip;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PowerShell IP resolution failed for VM {VmName}", vmName);
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
