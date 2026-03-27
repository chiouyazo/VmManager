using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Kvm;

public class KvmSnapshotService
{
    private readonly ShellRunner _sh;
    private readonly ILogger<KvmSnapshotService> _logger;

    public KvmSnapshotService(ShellRunner sh, ILogger<KvmSnapshotService> logger)
    {
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(logger);
        _sh = sh;
        _logger = logger;
    }

    public async Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName)
    {
        try
        {
            string namesOutput = await _sh.RunBashAsync(
                $"virsh snapshot-list {Q(vmName)} --name 2>/dev/null | grep -v '^$' || true"
            );
            if (string.IsNullOrWhiteSpace(namesOutput))
                return new List<VmSnapshot>();

            string[] names = namesOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            List<VmSnapshot> snapshots = new List<VmSnapshot>();

            foreach (string name in names)
            {
                try
                {
                    string xml = await _sh.RunBashAsync(
                        $"virsh snapshot-dumpxml {Q(vmName)} {Q(name)}"
                    );
                    DateTime creationTime = ParseSnapshotCreationTimeFromXml(xml);
                    snapshots.Add(
                        new VmSnapshot
                        {
                            Id = vmName + ":" + name,
                            Name = name,
                            VmName = vmName,
                            CreationTime = creationTime,
                        }
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to get snapshot info for {VmName}/{SnapshotName}",
                        vmName,
                        name
                    );
                }
            }

            return snapshots.OrderByDescending(s => s.CreationTime).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshots for VM {VmName}", vmName);
            return new List<VmSnapshot>();
        }
    }

    public async Task CreateSnapshotAsync(string vmName, string snapshotName)
    {
        _logger.LogInformation(
            "Creating snapshot {SnapshotName} for VM {VmName}",
            snapshotName,
            vmName
        );
        try
        {
            await _sh.RunBashAsync(
                $"virsh snapshot-create-as {Q(vmName)} --name {Q(snapshotName)}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create snapshot {SnapshotName} for VM {VmName}",
                snapshotName,
                vmName
            );
            throw;
        }
    }

    public async Task RestoreSnapshotAsync(string vmName, string snapshotId)
    {
        string snapshotName = ParseSnapshotName(snapshotId);
        _logger.LogInformation(
            "Restoring VM {VmName} to snapshot {SnapshotName}",
            vmName,
            snapshotName
        );
        try
        {
            await _sh.RunBashAsync($"virsh snapshot-revert {Q(vmName)} {Q(snapshotName)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore snapshot {SnapshotName} for VM {VmName}",
                snapshotName,
                vmName
            );
            throw;
        }
    }

    public async Task DeleteSnapshotAsync(string vmName, string snapshotId)
    {
        string snapshotName = ParseSnapshotName(snapshotId);
        _logger.LogInformation(
            "Deleting snapshot {SnapshotName} for VM {VmName}",
            snapshotName,
            vmName
        );
        try
        {
            await _sh.RunBashAsync($"virsh snapshot-delete {Q(vmName)} {Q(snapshotName)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete snapshot {SnapshotName} for VM {VmName}",
                snapshotName,
                vmName
            );
            throw;
        }
    }

    public async Task ExportSnapshotAsync(string snapshotId, string destDir)
    {
        (string vmName, string snapshotName) = ParseCompositeId(snapshotId);
        _logger.LogInformation(
            "Exporting snapshot {SnapshotName} for VM {VmName} to {DestDir}",
            snapshotName,
            vmName,
            destDir
        );

        string metadataJson = """{"provider": "kvm"}""";
        string script = $"""
            SNAP_XML=$(virsh snapshot-dumpxml {Q(vmName)} {Q(snapshotName)})
            DISK_PATH=$(echo "$SNAP_XML" | grep -oP '(?<=source file=.)[^'"'"'"]+')
            mkdir -p {Q(destDir)}
            DEST_FILE="{destDir}/disk.qcow2"
            qemu-img convert -f qcow2 -O qcow2 "$DISK_PATH" "$DEST_FILE"
            echo '{metadataJson}' > "{destDir}/metadata.json"
            """;
        await _sh.RunBashAsync(script);
    }

    public async Task<Dictionary<string, int>> GetAllSnapshotCountsAsync()
    {
        Dictionary<string, int> result = new Dictionary<string, int>();
        try
        {
            string namesOutput = await _sh.RunBashAsync("virsh list --all --name | grep -v '^$'");
            if (string.IsNullOrWhiteSpace(namesOutput))
                return result;

            string[] vmNames = namesOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            foreach (string vmName in vmNames)
            {
                try
                {
                    string countOutput = await _sh.RunBashAsync(
                        $"virsh snapshot-list {Q(vmName)} --name 2>/dev/null | grep -cv '^$' || echo 0"
                    );
                    int.TryParse(countOutput.Trim(), out int count);
                    result[vmName] = count;
                }
                catch
                {
                    result[vmName] = 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshot counts");
        }
        return result;
    }

    private static string ParseSnapshotName(string snapshotId)
    {
        int colonIndex = snapshotId.IndexOf(':');
        return colonIndex >= 0 ? snapshotId[(colonIndex + 1)..] : snapshotId;
    }

    private static (string VmName, string SnapshotName) ParseCompositeId(string snapshotId)
    {
        int colonIndex = snapshotId.IndexOf(':');
        if (colonIndex < 0)
            throw new ArgumentException($"Invalid composite snapshot ID: {snapshotId}");
        return (snapshotId[..colonIndex], snapshotId[(colonIndex + 1)..]);
    }

    private static string Q(string value) => ShellRunner.Q(value);

    private static DateTime ParseSnapshotCreationTimeFromXml(string xml)
    {
        // Look for <creationTime>UNIX_TIMESTAMP</creationTime>
        foreach (string line in xml.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("<creationTime>") && trimmed.EndsWith("</creationTime>"))
            {
                string value = trimmed["<creationTime>".Length..^"</creationTime>".Length];
                if (long.TryParse(value, out long unixTime))
                    return DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;
            }
        }
        return DateTime.MinValue;
    }
}
