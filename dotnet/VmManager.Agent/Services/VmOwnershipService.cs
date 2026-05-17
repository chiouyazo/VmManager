using System.Text.Json;

namespace VmManager.Agent.Services;

public class VmOwnershipService
{
    private readonly string _ownersPath;
    private readonly ILogger<VmOwnershipService> _logger;
    private static readonly object FileLock = new object();

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public VmOwnershipService(IAppPaths paths, ILogger<VmOwnershipService> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _ownersPath = paths.VmOwnersPath;
        _logger = logger;
    }

    public string GetOwner(string vmName)
    {
        lock (FileLock)
        {
            Dictionary<string, string> owners = LoadOwners();
            return owners.TryGetValue(vmName, out string? owner) ? owner : "admin";
        }
    }

    public void SetOwner(string vmName, string username)
    {
        lock (FileLock)
        {
            Dictionary<string, string> owners = LoadOwners();
            owners[vmName] = username;
            SaveOwners(owners);
        }
    }

    public void RemoveOwner(string vmName)
    {
        lock (FileLock)
        {
            Dictionary<string, string> owners = LoadOwners();
            if (owners.Remove(vmName))
                SaveOwners(owners);
        }
    }

    public List<string> GetVmsOwnedBy(string username)
    {
        lock (FileLock)
        {
            Dictionary<string, string> owners = LoadOwners();
            return owners
                .Where(kv => string.Equals(kv.Value, username, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    public void TransferOwnership(string vmName, string newOwner)
    {
        lock (FileLock)
        {
            Dictionary<string, string> owners = LoadOwners();
            string previousOwner = owners.TryGetValue(vmName, out string? o) ? o : "admin";
            owners[vmName] = newOwner;
            SaveOwners(owners);
            _logger.LogInformation(
                "Transferred ownership of {VmName} from {PreviousOwner} to {NewOwner}",
                vmName,
                previousOwner,
                newOwner
            );
        }
    }

    public void RenameVm(string oldName, string newName)
    {
        lock (FileLock)
        {
            Dictionary<string, string> owners = LoadOwners();
            if (owners.Remove(oldName, out string? owner))
            {
                owners[newName] = owner;
                SaveOwners(owners);
            }
        }
    }

    private Dictionary<string, string> LoadOwners()
    {
        if (!File.Exists(_ownersPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = File.ReadAllText(_ownersPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load VM owners from {Path}", _ownersPath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveOwners(Dictionary<string, string> owners)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_ownersPath)!);
        string json = JsonSerializer.Serialize(owners, WriteOptions);
        string tempPath = _ownersPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _ownersPath, true);
    }
}
