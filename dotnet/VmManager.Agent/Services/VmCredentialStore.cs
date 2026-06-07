using System.Text.Json;
using VmManager.Contracts.Interfaces;

namespace VmManager.Agent.Services;

public sealed class VmCredentialStore
{
    private readonly string _filePath;
    private readonly ILogger<VmCredentialStore> _logger;
    private readonly object _lock = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public VmCredentialStore(IAppPaths paths, ILogger<VmCredentialStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.Combine(paths.AppDataDir, "vm-credentials.json");
        _logger = logger;
    }

    public void SetCredentials(string vmName, string forUsername, string vmUser, string vmPassword)
    {
        lock (_lock)
        {
            List<VmCredentialEntry> entries = Load();
            entries.RemoveAll(e =>
                string.Equals(e.VmName, vmName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.ForUsername, forUsername, StringComparison.OrdinalIgnoreCase)
            );
            if (!string.IsNullOrWhiteSpace(vmUser))
            {
                entries.Add(
                    new VmCredentialEntry
                    {
                        VmName = vmName,
                        ForUsername = forUsername,
                        VmUser = vmUser,
                        VmPassword = vmPassword,
                    }
                );
            }
            Save(entries);
        }
    }

    public (string? VmUser, string? VmPassword) GetCredentials(string vmName, string forUsername)
    {
        List<VmCredentialEntry> entries = Load();
        VmCredentialEntry? entry = entries.Find(e =>
            string.Equals(e.VmName, vmName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ForUsername, forUsername, StringComparison.OrdinalIgnoreCase)
        );
        if (entry != null && !string.IsNullOrWhiteSpace(entry.VmUser))
            return (entry.VmUser, entry.VmPassword);
        return (null, null);
    }

    public bool HasCredentials(string vmName, string forUsername)
    {
        List<VmCredentialEntry> entries = Load();
        return entries.Exists(e =>
            string.Equals(e.VmName, vmName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ForUsername, forUsername, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(e.VmUser)
        );
    }

    public void RemoveCredentials(string vmName, string forUsername)
    {
        lock (_lock)
        {
            List<VmCredentialEntry> entries = Load();
            int removed = entries.RemoveAll(e =>
                string.Equals(e.VmName, vmName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.ForUsername, forUsername, StringComparison.OrdinalIgnoreCase)
            );
            if (removed > 0)
                Save(entries);
        }
    }

    public void RemoveAllForVm(string vmName)
    {
        lock (_lock)
        {
            List<VmCredentialEntry> entries = Load();
            int removed = entries.RemoveAll(e =>
                string.Equals(e.VmName, vmName, StringComparison.OrdinalIgnoreCase)
            );
            if (removed > 0)
                Save(entries);
        }
    }

    private List<VmCredentialEntry> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<VmCredentialEntry>>(json)
                    ?? new List<VmCredentialEntry>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VM credentials from {Path}", _filePath);
        }
        return new List<VmCredentialEntry>();
    }

    private void Save(List<VmCredentialEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private sealed class VmCredentialEntry
    {
        public string VmName { get; set; } = "";
        public string ForUsername { get; set; } = "";
        public string VmUser { get; set; } = "";
        public string VmPassword { get; set; } = "";
    }
}
