using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.HyperV;

/// <summary>
/// Snapshot (checkpoint) operations for Hyper-V VMs.
/// </summary>
public class HyperVSnapshotService
{
    private readonly ILogger<HyperVSnapshotService> _logger;
    private readonly PowerShellRunner _ps;

    public HyperVSnapshotService(ILogger<HyperVSnapshotService> logger, PowerShellRunner ps)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(ps);
        _logger = logger;
        _ps = ps;
    }

    public async Task<Dictionary<string, int>> GetAllSnapshotCountsAsync()
    {
        try
        {
            string output = await _ps.RunPsAsync(
                "Get-VM | ForEach-Object { $c = (Get-VMSnapshot -VMName $_.Name -ErrorAction SilentlyContinue | Measure-Object).Count; \"$($_.Name)|$c\" }"
            );
            Dictionary<string, int> result = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(output))
                return result;
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Trim().Split('|', 2);
                if (parts.Length == 2 && int.TryParse(parts[1], out int count))
                    result[parts[0]] = count;
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshot counts for all VMs");
            return new Dictionary<string, int>();
        }
    }

    public async Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName)
    {
        try
        {
            string output = await _ps.RunPsAsync(
                $"Get-VMSnapshot -VMName {PowerShellRunner.Q(vmName)} | Select-Object Name, Id, CreationTime | ConvertTo-Json -Compress"
            );
            if (string.IsNullOrWhiteSpace(output) || output == "null")
                return [];

            string json = output.TrimStart().StartsWith("[") ? output : $"[{output}]";
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc
                .RootElement.EnumerateArray()
                .Select(el => new VmSnapshot
                {
                    Id = el.GetProperty("Id").GetString() ?? "",
                    Name = el.GetProperty("Name").GetString() ?? "",
                    VmName = vmName,
                    CreationTime = el.TryGetProperty("CreationTime", out JsonElement ct)
                        ? ParsePsDateTime(ct)
                        : DateTime.MinValue,
                })
                .OrderByDescending(s => s.CreationTime)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshots for VM {VmName}", vmName);
            return [];
        }
    }

    public async Task CreateSnapshotAsync(string vmName, string snapshotName)
    {
        _logger.LogInformation(
            "Creating snapshot {SnapshotName} for VM {VmName}",
            snapshotName,
            vmName
        );
        await _ps.RunPsAsync(
            $"Checkpoint-VM -Name {PowerShellRunner.Q(vmName)} -SnapshotName {PowerShellRunner.Q(snapshotName)} -Confirm:$false"
        );
    }

    public async Task StopVmIfRunningAsync(string vmName)
    {
        await _ps.RunPsAsync(
            $"$vm = Get-VM -Name {PowerShellRunner.Q(vmName)}; if ($vm.State -ne 'Off') {{ Stop-VM -Name {PowerShellRunner.Q(vmName)} -Force -TurnOff }}"
        );
    }

    public async Task ApplySnapshotAsync(string vmName, string snapshotId)
    {
        await _ps.RunPsAsync(
            $"$snap = Get-VMSnapshot -VMName {PowerShellRunner.Q(vmName)} | Where-Object {{ $_.Id -eq '{snapshotId}' }}; if (-not $snap) {{ throw 'Snapshot not found.' }}; Restore-VMSnapshot -VMSnapshot $snap -Confirm:$false"
        );
    }

    public async Task RestoreSnapshotAsync(string vmName, string snapshotId)
    {
        _logger.LogInformation(
            "Restoring VM {VmName} to snapshot {SnapshotId}",
            vmName,
            snapshotId
        );
        await StopVmIfRunningAsync(vmName);
        await ApplySnapshotAsync(vmName, snapshotId);
    }

    public async Task DeleteSnapshotAsync(string vmName, string snapshotId)
    {
        await _ps.RunPsAsync(
            $"$snap = Get-VMSnapshot -VMName {PowerShellRunner.Q(vmName)} | Where-Object {{ $_.Id -eq '{snapshotId}' }}; if (-not $snap) {{ throw 'Snapshot not found.' }}; Remove-VMSnapshot -VMSnapshot $snap -Confirm:$false"
        );
    }

    public Task QuickSnapshotAsync(string vmName)
    {
        string name = $"Snapshot {DateTime.Now:yyyy-MM-dd HH:mm}";
        return CreateSnapshotAsync(vmName, name);
    }

    /// <summary>
    /// Exports a snapshot as a standalone .box-ready directory.
    /// Applies the snapshot to a temp clone, converts the disk to standalone VHDX,
    /// and writes disk.vhdx + metadata.json to exportPath.
    /// </summary>
    public async Task ExportSnapshotAsync(string snapshotId, string exportPath)
    {
        string script = $$"""
            $exportPath = {{PowerShellRunner.Q(exportPath)}}
            $cp = Get-VMCheckpoint -Id {{PowerShellRunner.Q(snapshotId)}}
            if (-not $cp) { throw 'Snapshot not found.' }

            $vmName = $cp.VMName
            $vm = Get-VM -Name $vmName

            # Find the VM's current VHDX
            $vhds = @(Get-VMHardDiskDrive -VMName $vmName | Select-Object -ExpandProperty Path)
            if ($vhds.Count -eq 0) { throw 'VM has no hard disks.' }
            $sourceVhd = $vhds[0]

            # Apply the snapshot to get the disk in that state
            # We need to do this on a copy to avoid modifying the live VM
            $tempDir = Join-Path $env:TEMP "vmm_export_$([Guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

            try {
                # Export the full VM at the checkpoint state
                Export-VMCheckpoint -VMCheckpoint $cp -Path $tempDir -ErrorAction Stop

                # Find the exported VHDX (could be nested in subdirectories)
                $exportedVhdx = Get-ChildItem $tempDir -Filter '*.vhdx' -Recurse |
                    Sort-Object Length -Descending |
                    Select-Object -First 1

                if (-not $exportedVhdx) {
                    # Try avhdx (differencing disk from checkpoint)
                    $exportedVhdx = Get-ChildItem $tempDir -Filter '*.avhdx' -Recurse |
                        Sort-Object Length -Descending |
                        Select-Object -First 1
                }

                if (-not $exportedVhdx) { throw 'No VHDX found in exported checkpoint.' }

                # Convert to standalone VHDX (flattens any differencing chain)
                New-Item -ItemType Directory -Path $exportPath -Force | Out-Null
                $destVhdx = Join-Path $exportPath 'disk.vhdx'

                $vhdInfo = Get-VHD -Path $exportedVhdx.FullName
                if ($vhdInfo.ParentPath) {
                    Write-Host "Converting differencing disk to standalone..."
                    Convert-VHD -Path $exportedVhdx.FullName -DestinationPath $destVhdx -VHDType Dynamic -ErrorAction Stop
                } else {
                    Write-Host "Copying standalone disk..."
                    Copy-Item -Path $exportedVhdx.FullName -Destination $destVhdx -Force
                }

                # Verify standalone
                $finalInfo = Get-VHD -Path $destVhdx
                if ($finalInfo.ParentPath) { throw "Exported disk still has parent reference: $($finalInfo.ParentPath)" }

                # Write metadata
                '{"provider": "hyperv"}' | Set-Content (Join-Path $exportPath 'metadata.json') -Encoding UTF8
                Write-Host "Export complete: $destVhdx"
            } finally {
                Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            """;
        await _ps.RunPsAsync(script);
    }

    /// <summary>Uploads a snapshot to a network share.</summary>
    public async Task UploadSnapshotAsync(
        string vmName,
        string snapshotName,
        string snapshotId,
        string networkShareRoot
    )
    {
        string script = $$"""
                          $username  = $env:USERNAME
                          $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
                          $vmNameVal = {{PowerShellRunner.Q(vmName)}}
                          $dest      = Join-Path {{PowerShellRunner.Q(
                              networkShareRoot
                          )}} "user-shares\$username\${vmNameVal}_$timestamp"
                          New-Item -ItemType Directory -Path $dest -Force | Out-Null
                          $cp = Get-VMCheckpoint -Id {{PowerShellRunner.Q(snapshotId)}}
                          if (-not $cp) { throw "Snapshot not found." }
                          Export-VMCheckpoint -VMCheckpoint $cp -Path $dest
                          $meta = [ordered]@{
                              Username     = $username
                              VmName       = $vmNameVal
                              SnapshotName = {{PowerShellRunner.Q(snapshotName)}}
                              ExportedAt   = (Get-Date -Format 'o')
                          } | ConvertTo-Json
                          Set-Content -Path (Join-Path $dest 'userinfo.json') -Value $meta -Encoding UTF8
                          """;
        await _ps.RunPsAsync(script);
    }

    internal static DateTime ParsePsDateTime(JsonElement el)
    {
        try
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                string str = el.GetString()!;
                if (str.StartsWith("/Date(") && str.EndsWith(")/"))
                {
                    string ms = str[6..^2];
                    return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(ms)).LocalDateTime;
                }
                return DateTime.Parse(str);
            }
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("DateTime", out JsonElement dt))
                    return DateTime.Parse(dt.GetString()!);
                if (
                    el.TryGetProperty("value", out JsonElement val)
                    && val.ValueKind == JsonValueKind.String
                )
                {
                    string valStr = val.GetString()!;
                    if (valStr.StartsWith("/Date(") && valStr.EndsWith(")/"))
                    {
                        string ms = valStr[6..^2];
                        return DateTimeOffset
                            .FromUnixTimeMilliseconds(long.Parse(ms))
                            .LocalDateTime;
                    }
                }
            }
            if (el.ValueKind == JsonValueKind.Number)
                return DateTimeOffset.FromUnixTimeMilliseconds(el.GetInt64()).LocalDateTime;
        }
        catch
        { /* Non-fatal: datetime parse failure returns MinValue */
        }
        return DateTime.MinValue;
    }
}
