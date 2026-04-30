using System.Text.Json;
using System.Text.Json.Serialization;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

public class VmAccessStore
{
    private readonly IAppPaths _paths;
    private readonly ILogger<VmAccessStore> _logger;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public VmAccessStore(IAppPaths paths, ILogger<VmAccessStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void SetOwner(string vmName, string username)
    {
        lock (_lock)
        {
            List<VmAccessEntry> entries = Load();
            VmAccessEntry? entry = entries.FirstOrDefault(e =>
                e.VmName.Equals(vmName, StringComparison.OrdinalIgnoreCase)
            );
            if (entry == null)
            {
                entry = new VmAccessEntry { VmName = vmName };
                entries.Add(entry);
            }
            entry.Owner = username;
            Save(entries);
        }
    }

    public string? GetOwner(string vmName)
    {
        List<VmAccessEntry> entries = Load();
        return entries
            .FirstOrDefault(e => e.VmName.Equals(vmName, StringComparison.OrdinalIgnoreCase))
            ?.Owner;
    }

    public VmAccessEntry? GetEntry(string vmName)
    {
        List<VmAccessEntry> entries = Load();
        return entries.FirstOrDefault(e =>
            e.VmName.Equals(vmName, StringComparison.OrdinalIgnoreCase)
        );
    }

    public List<VmAccessEntry> GetAll() => Load();

    public void SetGrant(string vmName, string username, VmPermission permission)
    {
        lock (_lock)
        {
            List<VmAccessEntry> entries = Load();
            VmAccessEntry? entry = entries.FirstOrDefault(e =>
                e.VmName.Equals(vmName, StringComparison.OrdinalIgnoreCase)
            );
            if (entry == null)
            {
                entry = new VmAccessEntry { VmName = vmName };
                entries.Add(entry);
            }
            VmAccessGrant? grant = entry.Grants.FirstOrDefault(g =>
                g.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
            );
            if (grant != null)
                grant.Permission = permission;
            else
                entry.Grants.Add(
                    new VmAccessGrant { Username = username, Permission = permission }
                );
            Save(entries);
        }
    }

    public void RemoveGrant(string vmName, string username)
    {
        lock (_lock)
        {
            List<VmAccessEntry> entries = Load();
            VmAccessEntry? entry = entries.FirstOrDefault(e =>
                e.VmName.Equals(vmName, StringComparison.OrdinalIgnoreCase)
            );
            if (entry == null)
                return;
            entry.Grants.RemoveAll(g =>
                g.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
            );
            Save(entries);
        }
    }

    public void RemoveVm(string vmName)
    {
        lock (_lock)
        {
            List<VmAccessEntry> entries = Load();
            entries.RemoveAll(e => e.VmName.Equals(vmName, StringComparison.OrdinalIgnoreCase));
            Save(entries);
        }
    }

    public void RenameVm(string oldName, string newName)
    {
        lock (_lock)
        {
            List<VmAccessEntry> entries = Load();
            VmAccessEntry? entry = entries.FirstOrDefault(e =>
                e.VmName.Equals(oldName, StringComparison.OrdinalIgnoreCase)
            );
            if (entry != null)
                entry.VmName = newName;
            Save(entries);
        }
    }

    private List<VmAccessEntry> Load()
    {
        try
        {
            if (!File.Exists(_paths.VmAccessPath))
                return [];
            string json = File.ReadAllText(_paths.VmAccessPath);
            return JsonSerializer.Deserialize<List<VmAccessEntry>>(json, Options) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VM access data");
            return [];
        }
    }

    private void Save(List<VmAccessEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.VmAccessPath)!);
            string json = JsonSerializer.Serialize(entries, Options);
            string tmp = _paths.VmAccessPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _paths.VmAccessPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save VM access data");
        }
    }
}
