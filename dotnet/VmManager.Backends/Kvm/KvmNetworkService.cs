using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Kvm;

public class KvmNetworkService : INetworkService
{
    private readonly ShellRunner _sh;
    private readonly ILogger<KvmNetworkService> _logger;

    public KvmNetworkService(ShellRunner sh, ILogger<KvmNetworkService> logger)
    {
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(logger);
        _sh = sh;
        _logger = logger;
    }

    public async Task<List<SwitchInfo>> GetSwitchesAsync()
    {
        string output = await _sh.RunBashAsync(
            "virsh net-list --all --name 2>/dev/null | grep -v '^$' || true"
        );
        if (string.IsNullOrWhiteSpace(output))
            return new List<SwitchInfo>();

        string[] names = output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        List<SwitchInfo> networks = new List<SwitchInfo>();

        foreach (string name in names)
        {
            try
            {
                string xml = await _sh.RunBashAsync($"virsh net-dumpxml {Q(name)}");
                string switchType = "Internal";
                if (xml.Contains("<forward mode='nat'"))
                    switchType = "NAT";
                else if (xml.Contains("<forward mode='bridge'"))
                    switchType = "External";
                else if (xml.Contains("<forward mode='none'") || !xml.Contains("<forward"))
                    switchType = "Internal";

                networks.Add(new SwitchInfo(name, switchType, null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get network info for {NetworkName}", name);
            }
        }

        return networks;
    }

    public async Task CreateSwitchAsync(string networkName, NetworkDefinition def)
    {
        _logger.LogInformation(
            "Creating {SwitchType} network {NetworkName}",
            def.SwitchType,
            networkName
        );

        string gateway = def.NatGateway ?? "192.168.100.1";
        string subnet = def.NatSubnet ?? "192.168.100.0/24";
        string prefix = subnet.Contains('/') ? subnet.Split('/')[1] : "24";
        string dhcpStart = def.DhcpRangeStart ?? "";
        string dhcpEnd = def.DhcpRangeEnd ?? "";

        string xml = def.SwitchType switch
        {
            SwitchType.NAT => BuildNatNetworkXml(networkName, gateway, prefix, dhcpStart, dhcpEnd),
            SwitchType.Internal => BuildInternalNetworkXml(networkName),
            SwitchType.External => BuildBridgeNetworkXml(networkName, def.PhysicalAdapter ?? "br0"),
            SwitchType.Private => BuildInternalNetworkXml(networkName),
            _ => throw new ArgumentOutOfRangeException(
                nameof(def),
                $"Unsupported network type: {def.SwitchType}"
            ),
        };

        string tmpXml = Path.Combine(Path.GetTempPath(), $"vmm_net_{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(tmpXml, xml);
            await _sh.RunBashAsync($"virsh net-define {Q(tmpXml)}");
            await _sh.RunBashAsync($"virsh net-start {Q(networkName)}");
            await _sh.RunBashAsync($"virsh net-autostart {Q(networkName)}");
        }
        finally
        {
            try
            {
                File.Delete(tmpXml);
            }
            catch { }
        }
    }

    public Task UpdateSwitchAsync(string networkName, NetworkDefinition def)
    {
        _logger.LogWarning(
            "In-place network update is not supported on KVM; recreating {NetworkName}",
            networkName
        );
        return Task.CompletedTask;
    }

    public async Task RemoveSwitchAsync(string networkName)
    {
        _logger.LogInformation("Removing network {NetworkName}", networkName);
        await _sh.RunBashAsync($"virsh net-destroy {Q(networkName)} 2>/dev/null || true");
        await _sh.RunBashAsync($"virsh net-undefine {Q(networkName)}");
    }

    public async Task ConfigureVmAdaptersAsync(
        string vmName,
        List<(string SwitchName, VmNetworkAdapter Config)> adapters
    )
    {
        _logger.LogInformation(
            "Configuring {Count} network adapter(s) on VM {VmName}",
            adapters.Count,
            vmName
        );

        string existingMacs = await _sh.RunBashAsync(
            $"virsh domiflist {Q(vmName)} 2>/dev/null | tail -n +3 | awk '{{print $5}}' || true"
        );
        foreach (
            string mac in existingMacs.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!string.IsNullOrWhiteSpace(mac) && mac != "-")
                await _sh.RunBashAsync(
                    $"virsh detach-interface {Q(vmName)} network --mac {Q(mac)} --config 2>/dev/null || true"
                );
        }

        foreach ((string networkName, VmNetworkAdapter config) in adapters)
        {
            string macArg = string.IsNullOrEmpty(config.MacAddress)
                ? ""
                : $" --mac {Q(config.MacAddress)}";
            await _sh.RunBashAsync(
                $"virsh attach-interface {Q(vmName)} network {Q(networkName)} --model virtio --config{macArg}"
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
        _logger.LogWarning(
            "Guest IP configuration via PowerShell remoting is not supported on KVM for VM {VmName}",
            vmName
        );
        return Task.CompletedTask;
    }

    private static string Q(string value) => ShellRunner.Q(value);

    private static string BuildNatNetworkXml(
        string name,
        string gateway,
        string prefix,
        string dhcpStart,
        string dhcpEnd
    )
    {
        string dhcpBlock = "";
        if (!string.IsNullOrEmpty(dhcpStart) && !string.IsNullOrEmpty(dhcpEnd))
        {
            dhcpBlock = $"""
                        <dhcp>
                          <range start='{dhcpStart}' end='{dhcpEnd}'/>
                        </dhcp>
                """;
        }

        return $"""
            <network>
              <name>{name}</name>
              <forward mode='nat'/>
              <ip address='{gateway}' prefix='{prefix}'>
            {dhcpBlock}  </ip>
            </network>
            """;
    }

    private static string BuildInternalNetworkXml(string name) =>
        $"""
            <network>
              <name>{name}</name>
            </network>
            """;

    private static string BuildBridgeNetworkXml(string name, string bridgeInterface) =>
        $"""
            <network>
              <name>{name}</name>
              <forward mode='bridge'/>
              <bridge name='{bridgeInterface}'/>
            </network>
            """;
}
