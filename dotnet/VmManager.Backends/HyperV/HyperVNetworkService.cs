using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.HyperV;

public record PhysicalAdapterInfo(
    string Name,
    string InterfaceDescription,
    string Status,
    string LinkSpeed,
    string MediaType
);

public class HyperVNetworkService : INetworkService
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<HyperVNetworkService> _logger;

    public HyperVNetworkService(PowerShellRunner ps, ILogger<HyperVNetworkService> logger)
    {
        ArgumentNullException.ThrowIfNull(ps);
        ArgumentNullException.ThrowIfNull(logger);
        _ps = ps;
        _logger = logger;
    }

    public async Task<List<SwitchInfo>> GetSwitchesAsync()
    {
        string script = """
            Get-VMSwitch | Select-Object Name, SwitchType, NetAdapterInterfaceDescription |
                ForEach-Object {
                    [PSCustomObject]@{
                        Name = $_.Name
                        SwitchType = $_.SwitchType.ToString()
                        NetAdapterName = $_.NetAdapterInterfaceDescription
                    }
                } | ConvertTo-Json -Compress
            """;
        string json = await _ps.RunPsAsync(script);
        if (string.IsNullOrWhiteSpace(json))
            return new List<SwitchInfo>();

        List<SwitchInfo>? list = json.TrimStart().StartsWith("[")
            ? JsonSerializer.Deserialize<List<SwitchInfo>>(json)
            : new List<SwitchInfo> { JsonSerializer.Deserialize<SwitchInfo>(json)! };
        return list ?? new List<SwitchInfo>();
    }

    public async Task<List<PhysicalAdapterInfo>> GetPhysicalAdaptersAsync()
    {
        string script = """
            Get-NetAdapter -Physical | Select-Object Name, InterfaceDescription, Status, LinkSpeed, MediaType |
                ForEach-Object {
                    [PSCustomObject]@{
                        Name = $_.Name
                        InterfaceDescription = $_.InterfaceDescription
                        Status = $_.Status
                        LinkSpeed = $_.LinkSpeed
                        MediaType = $_.MediaType
                    }
                } | ConvertTo-Json -Compress
            """;
        string json = await _ps.RunPsAsync(script);
        if (string.IsNullOrWhiteSpace(json))
            return new List<PhysicalAdapterInfo>();

        List<PhysicalAdapterInfo>? list = json.TrimStart().StartsWith("[")
            ? JsonSerializer.Deserialize<List<PhysicalAdapterInfo>>(json)
            : new List<PhysicalAdapterInfo>
            {
                JsonSerializer.Deserialize<PhysicalAdapterInfo>(json)!,
            };
        return list ?? new List<PhysicalAdapterInfo>();
    }

    public async Task CreateSwitchAsync(string switchName, NetworkDefinition def)
    {
        _logger.LogInformation(
            "Creating {SwitchType} switch {SwitchName}",
            def.SwitchType,
            switchName
        );

        string script = def.SwitchType switch
        {
            SwitchType.Internal =>
                $"New-VMSwitch -Name {PowerShellRunner.Q(switchName)} -SwitchType Internal",
            SwitchType.Private =>
                $"New-VMSwitch -Name {PowerShellRunner.Q(switchName)} -SwitchType Private",
            SwitchType.External => await BuildExternalSwitchScript(switchName, def),
            SwitchType.NAT => BuildNatSwitchScript(switchName, def),
            _ => throw new ArgumentOutOfRangeException(
                nameof(def),
                $"Unsupported switch type: {def.SwitchType}"
            ),
        };

        await _ps.RunPsAsync(script);
    }

    public async Task UpdateSwitchAsync(string switchName, NetworkDefinition def)
    {
        _logger.LogInformation("Updating switch {SwitchName}", switchName);

        string script = "";

        if (def.SwitchType == SwitchType.NAT)
        {
            script = $$"""
                $natName = {{PowerShellRunner.Q(switchName)}} + '_nat'
                Remove-NetNat -Name $natName -Confirm:$false -ErrorAction SilentlyContinue

                $ifIndex = (Get-NetAdapter | Where-Object { $_.Name -like "*{{switchName}}*" }).ifIndex
                if ($ifIndex) {
                    Get-NetIPAddress -InterfaceIndex $ifIndex -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
                    New-NetIPAddress -IPAddress {{PowerShellRunner.Q(
                    def.NatGateway ?? "192.168.100.1"
                )}} -PrefixLength 24 -InterfaceIndex $ifIndex
                }
                New-NetNat -Name $natName -InternalIPInterfaceAddressPrefix {{PowerShellRunner.Q(
                    def.NatSubnet ?? "192.168.100.0/24"
                )}}
                """;
        }
        else if (def.SwitchType == SwitchType.External && def.PhysicalAdapter != null)
        {
            string adapterName = await ResolvePhysicalAdapterAsync(def.PhysicalAdapter);
            script =
                $"Set-VMSwitch -Name {PowerShellRunner.Q(switchName)} -NetAdapterName {PowerShellRunner.Q(adapterName)} -AllowManagementOS ${(def.AllowManagementOs ? "true" : "false")}";
        }

        if (def.MinimumBandwidthAbsolute.HasValue || def.MaximumBandwidth.HasValue)
        {
            if (!string.IsNullOrEmpty(script))
                script += "\n";
            string bandwidthParams = "";
            if (def.MinimumBandwidthAbsolute.HasValue)
                bandwidthParams +=
                    $" -DefaultFlowMinimumBandwidthAbsolute {def.MinimumBandwidthAbsolute.Value}";
            if (def.MaximumBandwidth.HasValue)
                bandwidthParams += $" -DefaultFlowMaximumBandwidth {def.MaximumBandwidth.Value}";
            script += $"Set-VMSwitch -Name {PowerShellRunner.Q(switchName)}{bandwidthParams}";
        }

        if (!string.IsNullOrEmpty(script))
            await _ps.RunPsAsync(script);
    }

    public async Task RemoveSwitchAsync(string switchName)
    {
        _logger.LogInformation("Removing switch {SwitchName}", switchName);

        string script = $$"""
            $natName = {{PowerShellRunner.Q(switchName)}} + '_nat'
            Remove-NetNat -Name $natName -Confirm:$false -ErrorAction SilentlyContinue
            Remove-VMSwitch -Name {{PowerShellRunner.Q(switchName)}} -Force
            """;
        await _ps.RunPsAsync(script);
    }

    public async Task<string> ResolvePhysicalAdapterAsync(string selector)
    {
        if (selector.StartsWith("name:"))
        {
            string exactName = selector["name:".Length..];
            return exactName;
        }

        if (selector.StartsWith("description:"))
        {
            string pattern = selector["description:".Length..];
            string script = $$"""
                $adapter = Get-NetAdapter -Physical | Where-Object {
                    $_.InterfaceDescription -like {{PowerShellRunner.Q(pattern)}}
                } | Select-Object -First 1
                if (-not $adapter) { throw "No adapter matching description '{{pattern}}'" }

                $boundSwitches = Get-VMSwitch -SwitchType External -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like 'VmMgr-*' } |
                    Select-Object -ExpandProperty NetAdapterInterfaceDescription
                if ($boundSwitches -contains $adapter.InterfaceDescription) {
                    throw "Adapter '$($adapter.Name)' is already bound to a VmMgr switch"
                }
                $adapter.Name
                """;
            return await _ps.RunPsAsync(script);
        }

        bool includeWireless = selector == "auto-wireless";
        string autoScript = $$"""
            $adapters = Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' }
            {{(
                includeWireless
                    ? ""
                    : "$adapters = $adapters | Where-Object { $_.MediaType -ne '802.11' -and $_.PhysicalMediaType -ne '14' }"
            )}}

            $boundSwitches = Get-VMSwitch -SwitchType External -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like 'VmMgr-*' } |
                Select-Object -ExpandProperty NetAdapterInterfaceDescription

            $adapters = $adapters | Where-Object { $boundSwitches -notcontains $_.InterfaceDescription }
            $adapter = $adapters | Sort-Object -Property LinkSpeed -Descending | Select-Object -First 1
            if (-not $adapter) { throw 'No suitable physical adapter found' }
            $adapter.Name
            """;
        return await _ps.RunPsAsync(autoScript);
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

        string script =
            $"Get-VMNetworkAdapter -VMName {PowerShellRunner.Q(vmName)} | Remove-VMNetworkAdapter\n";

        foreach ((string switchName, VmNetworkAdapter config) in adapters)
        {
            script +=
                $"Add-VMNetworkAdapter -VMName {PowerShellRunner.Q(vmName)} -SwitchName {PowerShellRunner.Q(switchName)}\n";

            if (config.VlanId.HasValue)
            {
                script +=
                    $"Get-VMNetworkAdapter -VMName {PowerShellRunner.Q(vmName)} | Select-Object -Last 1 | Set-VMNetworkAdapterVlan -Access -VlanId {config.VlanId.Value}\n";
            }

            if (!string.IsNullOrEmpty(config.MacAddress))
            {
                script +=
                    $"Get-VMNetworkAdapter -VMName {PowerShellRunner.Q(vmName)} | Select-Object -Last 1 | Set-VMNetworkAdapter -StaticMacAddress {PowerShellRunner.Q(config.MacAddress)}\n";
            }
        }

        await _ps.RunPsAsync(script);
    }

    public async Task ConfigureGuestIpAsync(
        string vmName,
        string username,
        string password,
        List<VmNetworkAdapter> adapters
    )
    {
        List<VmNetworkAdapter> staticAdapters = adapters
            .Where(a => !string.IsNullOrEmpty(a.StaticIp))
            .ToList();
        if (staticAdapters.Count == 0)
            return;

        _logger.LogInformation(
            "Configuring static IP for {Count} adapter(s) on VM {VmName}",
            staticAdapters.Count,
            vmName
        );

        string innerBlock = "";
        for (int i = 0; i < staticAdapters.Count; i++)
        {
            VmNetworkAdapter adapter = staticAdapters[i];
            string prefix = "24";
            string ip = adapter.StaticIp!;
            if (ip.Contains('/'))
            {
                string[] parts = ip.Split('/');
                ip = parts[0];
                prefix = parts[1];
            }

            innerBlock += $$"""
                $nic = Get-NetAdapter | Select-Object -Index {{i}}
                if ($nic) {
                    New-NetIPAddress -InterfaceIndex $nic.ifIndex -IPAddress '{{ip}}' -PrefixLength {{prefix}} -DefaultGateway '{{adapter.Gateway
                    ?? ""}}' -ErrorAction SilentlyContinue
                    {{(
                    string.IsNullOrEmpty(adapter.DnsServers)
                        ? ""
                        : $"Set-DnsClientServerAddress -InterfaceIndex $nic.ifIndex -ServerAddresses @('{adapter.DnsServers.Replace(",", "','")}')"
                )}}
                }
                """;
        }

        string script = $$"""
            $cred = New-Object PSCredential({{PowerShellRunner.Q(
                username
            )}}, (ConvertTo-SecureString {{PowerShellRunner.Q(password)}} -AsPlainText -Force))
            $session = $null
            $tries = 0
            while ($tries -lt 40 -and -not $session) {
                try {
                    $session = New-PSSession -VMName {{PowerShellRunner.Q(
                vmName
            )}} -Credential $cred -ErrorAction Stop
                } catch {
                    Start-Sleep -Seconds 2
                    $tries++
                }
            }
            if (-not $session) { throw "VM '{{vmName}}' did not become responsive within 80 seconds." }

            Invoke-Command -Session $session -ScriptBlock {
                {{innerBlock}}
            }
            Remove-PSSession $session
            """;
        await _ps.RunPsAsync(script);
    }

    private async Task<string> BuildExternalSwitchScript(string switchName, NetworkDefinition def)
    {
        string adapterName = await ResolvePhysicalAdapterAsync(def.PhysicalAdapter ?? "auto");
        string allowMgmt = def.AllowManagementOs ? "$true" : "$false";
        return $"New-VMSwitch -Name {PowerShellRunner.Q(switchName)} -NetAdapterName {PowerShellRunner.Q(adapterName)} -AllowManagementOS {allowMgmt}";
    }

    private static string BuildNatSwitchScript(string switchName, NetworkDefinition def)
    {
        string gateway = def.NatGateway ?? "192.168.100.1";
        string subnet = def.NatSubnet ?? "192.168.100.0/24";
        string natName = switchName + "_nat";

        return $$"""
            New-VMSwitch -Name {{PowerShellRunner.Q(switchName)}} -SwitchType Internal
            $ifIndex = (Get-NetAdapter | Where-Object { $_.Name -like "*{{switchName}}*" }).ifIndex
            New-NetIPAddress -IPAddress {{PowerShellRunner.Q(
                gateway
            )}} -PrefixLength 24 -InterfaceIndex $ifIndex
            New-NetNat -Name {{PowerShellRunner.Q(
                natName
            )}} -InternalIPInterfaceAddressPrefix {{PowerShellRunner.Q(subnet)}}
            """;
    }
}
