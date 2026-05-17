using System.Text.Json;

namespace VmManager.Agent.Services;

public class VmSharingService
{
    private readonly string _sharesPath;
    private readonly ILogger<VmSharingService> _logger;
    private static readonly object FileLock = new object();

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public VmSharingService(IAppPaths paths, ILogger<VmSharingService> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _sharesPath = paths.VmSharesPath;
        _logger = logger;
    }

    public List<VmShareEntry> GetSharesForVm(string vmName)
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();
            return shares
                .Where(s => string.Equals(s.VmName, vmName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public List<VmShareEntry> GetSharesForUser(string username)
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();
            return shares
                .Where(s =>
                    string.Equals(
                        s.SharedWithUsername,
                        username,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();
        }
    }

    public void ShareVm(
        string vmName,
        string ownerUsername,
        string sharedWithUsername,
        HashSet<string> grantedPermissions
    )
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();

            shares.RemoveAll(s =>
                string.Equals(s.VmName, vmName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    s.SharedWithUsername,
                    sharedWithUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            shares.Add(
                new VmShareEntry
                {
                    VmName = vmName,
                    OwnerUsername = ownerUsername,
                    SharedWithUsername = sharedWithUsername,
                    GrantedPermissions = grantedPermissions,
                    SharedAt = DateTime.UtcNow,
                }
            );

            SaveShares(shares);
            _logger.LogInformation(
                "Shared VM {VmName} with {SharedWith} (permissions: {Permissions})",
                vmName,
                sharedWithUsername,
                string.Join(", ", grantedPermissions)
            );
        }
    }

    public void UnshareVm(string vmName, string sharedWithUsername)
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();
            int removed = shares.RemoveAll(s =>
                string.Equals(s.VmName, vmName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    s.SharedWithUsername,
                    sharedWithUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (removed > 0)
            {
                SaveShares(shares);
                _logger.LogInformation(
                    "Unshared VM {VmName} from {SharedWith}",
                    vmName,
                    sharedWithUsername
                );
            }
        }
    }

    public void RemoveAllSharesForVm(string vmName)
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();
            int removed = shares.RemoveAll(s =>
                string.Equals(s.VmName, vmName, StringComparison.OrdinalIgnoreCase)
            );
            if (removed > 0)
                SaveShares(shares);
        }
    }

    public void RemoveAllSharesForUser(string username)
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();
            int removed = shares.RemoveAll(s =>
                string.Equals(s.SharedWithUsername, username, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.OwnerUsername, username, StringComparison.OrdinalIgnoreCase)
            );
            if (removed > 0)
                SaveShares(shares);
        }
    }

    public void RenameVm(string oldName, string newName)
    {
        lock (FileLock)
        {
            List<VmShareEntry> shares = LoadShares();
            bool changed = false;
            foreach (VmShareEntry share in shares)
            {
                if (string.Equals(share.VmName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    share.VmName = newName;
                    changed = true;
                }
            }
            if (changed)
                SaveShares(shares);
        }
    }

    private List<VmShareEntry> LoadShares()
    {
        if (!File.Exists(_sharesPath))
            return [];

        try
        {
            string json = File.ReadAllText(_sharesPath);
            return JsonSerializer.Deserialize<List<VmShareEntry>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load VM shares from {Path}", _sharesPath);
            return [];
        }
    }

    private void SaveShares(List<VmShareEntry> shares)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sharesPath)!);
        string json = JsonSerializer.Serialize(shares, WriteOptions);
        string tempPath = _sharesPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _sharesPath, true);
    }
}
