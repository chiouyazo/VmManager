using System.Diagnostics;
using System.Management;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.HyperV;

/// <summary>
/// VM lifecycle operations: list, start, stop, delete, rename, reset, connect.
/// </summary>
public class HyperVVmService
{
    private readonly ILogger<HyperVVmService> _logger;
    private readonly PowerShellRunner _ps;
    private readonly HyperVWmiHelper _wmi;

    public HyperVVmService(
        ILogger<HyperVVmService> logger,
        PowerShellRunner ps,
        HyperVWmiHelper wmi
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(ps);
        ArgumentNullException.ThrowIfNull(wmi);
        _logger = logger;
        _ps = ps;
        _wmi = wmi;
    }

    public async Task<List<VmInstance>> GetVmsAsync()
    {
        _logger.LogDebug("Loading VMs via WMI");
        List<VmInstance> vms = await Task.Run(() =>
        {
            try
            {
                // Don't filter by Caption - it's localized on non-English Windows
                SelectQuery query = new SelectQuery("Msvm_ComputerSystem");
                using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    _wmi.Scope,
                    query
                );
                List<VmInstance> result = new List<VmInstance>();

                string hostName = Environment.MachineName;
                foreach (ManagementObject vm in searcher.Get())
                {
                    string? elementName = (string?)vm["ElementName"];
                    if (string.Equals(elementName, hostName, StringComparison.OrdinalIgnoreCase))
                    {
                        vm.Dispose();
                        continue;
                    }
                    using (vm)
                    {
                        ushort state = (ushort)vm["EnabledState"];
                        object? onTime = vm["OnTimeInMilliseconds"];
                        result.Add(
                            new VmInstance
                            {
                                Name = (string)vm["ElementName"],
                                State = HyperVWmiHelper.MapWmiState(state),
                                MemoryAssigned = state == 2 ? _wmi.GetMemoryUsage(vm) : 0,
                                Uptime =
                                    onTime != null
                                        ? TimeSpan.FromMilliseconds((ulong)onTime)
                                        : TimeSpan.Zero,
                            }
                        );
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WMI query for VMs failed, will try PowerShell fallback");
                return new List<VmInstance>();
            }
        });

        // Fallback to PowerShell if WMI returned nothing
        if (vms.Count == 0)
            vms = await GetVmsViaPowerShellAsync();

        return vms;
    }

    private async Task<List<VmInstance>> GetVmsViaPowerShellAsync()
    {
        try
        {
            string output = await _ps.RunPsAsync(
                "Get-VM | Select-Object Name, State, MemoryAssigned, Uptime | ConvertTo-Json -Compress"
            );
            if (string.IsNullOrWhiteSpace(output) || output == "null")
                return [];

            List<VmInstance> vms = new List<VmInstance>();
            using JsonDocument doc = JsonDocument.Parse(
                output.TrimStart().StartsWith("[") ? output : $"[{output}]"
            );
            foreach (JsonElement el in doc.RootElement.EnumerateArray())
            {
                vms.Add(
                    new VmInstance
                    {
                        Name = el.GetProperty("Name").GetString() ?? "",
                        State = MapPsState(el.GetProperty("State").GetInt32()),
                        MemoryAssigned = el.TryGetProperty("MemoryAssigned", out JsonElement mem)
                            ? mem.GetInt64()
                            : 0,
                        Uptime = el.TryGetProperty("Uptime", out JsonElement up)
                            ? ParsePsTimeSpan(up)
                            : TimeSpan.Zero,
                    }
                );
            }

            return vms;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PowerShell Get-VM fallback failed");
            return [];
        }
    }

    private static string MapPsState(int state) =>
        state switch
        {
            2 => "Running",
            3 => "Off",
            6 => "Saved",
            9 => "Paused",
            _ => $"Unknown ({state})",
        };

    private static TimeSpan ParsePsTimeSpan(JsonElement el)
    {
        try
        {
            if (el.ValueKind == JsonValueKind.String)
                return TimeSpan.Parse(el.GetString()!);
            if (
                el.ValueKind == JsonValueKind.Object
                && el.TryGetProperty("Ticks", out JsonElement ticks)
            )
                return TimeSpan.FromTicks(ticks.GetInt64());
        }
        catch
        { /* Non-fatal: timespan parse failure returns zero */
        }
        return TimeSpan.Zero;
    }

    /// <summary>
    /// Runs diagnostic checks and returns a report about Hyper-V VM visibility.
    /// </summary>
    public async Task<string> TroubleshootVmListingAsync()
    {
        List<string> lines = new List<string>();
        try
        {
            // Check WMI namespace
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\virtualization\v2");
                scope.Connect();
                lines.Add("[OK] WMI namespace root\\virtualization\\v2 is accessible.");

                SelectQuery query = new SelectQuery("Msvm_ComputerSystem");
                using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    scope,
                    query
                );
                int count = searcher
                    .Get()
                    .Cast<ManagementObject>()
                    .Count(o => (string?)o["Description"] != "Microsoft Hosting Computer System");
                lines.Add($"[OK] WMI query returned {count} VM(s).");
            }
            catch (Exception ex)
            {
                lines.Add($"[FAIL] WMI namespace error: {ex.Message}");
            }

            try
            {
                string psOutput = await _ps.RunPsAsync(
                    "$vms = Get-VM; Write-Output \"$($vms.Count) VM(s) found via Get-VM\"; $vms | ForEach-Object { Write-Output \"  - $($_.Name) ($($_.State)) Path=$($_.Path)\" }"
                );
                lines.Add($"[PS] {psOutput}");
            }
            catch (Exception ex)
            {
                lines.Add($"[FAIL] PowerShell Get-VM failed: {ex.Message}");
            }

            try
            {
                string featureOutput = await _ps.RunPsAsync(
                    "(Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V).State"
                );
                lines.Add($"[INFO] Hyper-V feature state: {featureOutput}");
            }
            catch (Exception ex)
            {
                lines.Add($"[WARN] Could not check Hyper-V feature: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"[ERROR] Troubleshoot failed: {ex.Message}");
        }

        return string.Join("\n", lines);
    }

    public Task StartVmAsync(string name) => ChangeVmStateAsync(name, 2);

    public Task StopVmAsync(string name) => ChangeVmStateAsync(name, 3);

    public async Task DeleteVmAsync(string name)
    {
        _logger.LogInformation("Deleting VM {VmName}", name);
        string script = $$"""
            $vm = Get-VM -Name {{PowerShellRunner.Q(name)}} -ErrorAction Stop
            if ($vm.State -ne 'Off') { Stop-VM -Name {{PowerShellRunner.Q(name)}} -Force -TurnOff }

            $vhds = @(Get-VMHardDiskDrive -VMName {{PowerShellRunner.Q(
                name
            )}} | Select-Object -ExpandProperty Path)
            $vmPath = $vm.Path
            $vmConfigPath = $vm.ConfigurationLocation
            $dirsToClean = @()

            foreach ($vhd in $vhds) {
                $dir = Split-Path $vhd -Parent
                if ($dir -and ($dirsToClean -notcontains $dir)) { $dirsToClean += $dir }
            }
            if ($vmPath) {
                $d = Join-Path $vmPath {{PowerShellRunner.Q(name)}}
                if ($dirsToClean -notcontains $d) { $dirsToClean += $d }
            }
            if ($vmConfigPath) {
                $d = Join-Path $vmConfigPath {{PowerShellRunner.Q(name)}}
                if ($dirsToClean -notcontains $d) { $dirsToClean += $d }
            }

            Get-VMSnapshot -VMName {{PowerShellRunner.Q(name)}} -ErrorAction SilentlyContinue |
                Remove-VMSnapshot -IncludeAllChildSnapshots -Confirm:$false -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2

            Remove-VM -Name {{PowerShellRunner.Q(name)}} -Force

            foreach ($dir in $dirsToClean) {
                if ((Test-Path $dir) -and ($dir -ne 'C:\') -and ($dir -ne 'C:\VMs')) {
                    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
                }
            }

            $leftover = @()
            foreach ($dir in $dirsToClean) {
                if (Test-Path $dir) { $leftover += $dir }
            }
            if ($leftover.Count -gt 0) {
                throw "VM removed but files remain at: $($leftover -join ', ')"
            }
            """;
        await _ps.RunPsAsync(script);
    }

    public Task RenameVmAsync(string currentName, string newName) =>
        Task.Run(() =>
        {
            ManagementObject vm =
                _wmi.GetVm(currentName)
                ?? throw new InvalidOperationException($"VM '{currentName}' not found.");
            ManagementObject settings = _wmi.GetVmSettings(vm);
            settings["ElementName"] = newName;

            ManagementObject mgmt = _wmi.GetManagementService();
            ManagementBaseObject? modParams = mgmt.GetMethodParameters("ModifySystemSettings");
            modParams["SystemSettings"] = settings.GetText(TextFormat.WmiDtd20);
            ManagementBaseObject? result = mgmt.InvokeMethod(
                "ModifySystemSettings",
                modParams,
                null
            );
            HyperVWmiHelper.WaitForJob(result);
        });

    public async Task<bool> ResetVmAsync(string name, HyperVSnapshotService snapshots)
    {
        List<VmSnapshot> snapshotList = await snapshots.GetSnapshotsAsync(name);
        if (snapshotList.Count == 0)
            return false;

        VmSnapshot oldest = snapshotList.OrderBy(s => s.CreationTime).First();
        await snapshots.RestoreSnapshotAsync(name, oldest.Id);
        return true;
    }

    public Task ConnectToVmAsync(string vmName, string username = "", string password = "")
    {
        return Task.Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                Process
                    .Start(
                        new ProcessStartInfo("cmdkey.exe")
                        {
                            Arguments =
                                $"/generic:\"TERMSRV/{vmName}\" /user:\"{username}\" /pass:\"{password}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    )
                    ?.WaitForExit();
            }

            Process.Start(
                new ProcessStartInfo("vmconnect.exe", $"localhost \"{vmName}\"")
                {
                    UseShellExecute = true,
                }
            );
        });
    }

    private Task ChangeVmStateAsync(string name, ushort requestedState) =>
        Task.Run(async () =>
        {
            _logger.LogDebug(
                "ChangeVmState: looking up VM '{VmName}' via WMI (state={State})",
                name,
                requestedState
            );
            ManagementObject? vm = _wmi.GetVm(name);
            if (vm == null)
            {
                // WMI might be stale - fall back to PowerShell which is always reliable
                _logger.LogWarning(
                    "VM '{VmName}' not found via WMI, falling back to PowerShell",
                    name
                );
                string cmd =
                    requestedState == 2
                        ? $"Start-VM -Name {PowerShellRunner.Q(name)}"
                        : $"Stop-VM -Name {PowerShellRunner.Q(name)} -Force -TurnOff";
                await _ps.RunPsAsync(cmd);
                return;
            }
            ManagementBaseObject? inParams = vm.GetMethodParameters("RequestStateChange");
            inParams["RequestedState"] = requestedState;
            ManagementBaseObject? result = vm.InvokeMethod("RequestStateChange", inParams, null);
            HyperVWmiHelper.WaitForJob(result);
        });
}
