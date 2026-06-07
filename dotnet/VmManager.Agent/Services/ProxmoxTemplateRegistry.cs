using System.Text.Json;
using VmManager.Contracts.Interfaces;

namespace VmManager.Agent.Services;

public sealed class ProxmoxTemplateRegistry
{
    private readonly string _filePath;
    private readonly ILogger<ProxmoxTemplateRegistry> _logger;
    private readonly object _lock = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public ProxmoxTemplateRegistry(IAppPaths paths, ILogger<ProxmoxTemplateRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.Combine(paths.AppDataDir, "image-templates.json");
        _logger = logger;
    }

    public int? GetTemplateVmId(string imageStorageId)
    {
        List<TemplateEntry> entries = Load();
        TemplateEntry? entry = entries.Find(e =>
            string.Equals(e.ImageStorageId, imageStorageId, StringComparison.OrdinalIgnoreCase)
        );
        return entry?.TemplateVmId;
    }

    public void Register(string imageStorageId, int templateVmId, int diskSizeGb)
    {
        lock (_lock)
        {
            List<TemplateEntry> entries = Load();
            entries.RemoveAll(e =>
                string.Equals(e.ImageStorageId, imageStorageId, StringComparison.OrdinalIgnoreCase)
            );
            entries.Add(
                new TemplateEntry
                {
                    ImageStorageId = imageStorageId,
                    TemplateVmId = templateVmId,
                    DiskSizeGb = diskSizeGb,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            Save(entries);
            _logger.LogInformation(
                "Registered template VMID {VmId} for image {ImageId} ({SizeGb} GB)",
                templateVmId,
                imageStorageId,
                diskSizeGb
            );
        }
    }

    public void Remove(string imageStorageId)
    {
        lock (_lock)
        {
            List<TemplateEntry> entries = Load();
            int removed = entries.RemoveAll(e =>
                string.Equals(e.ImageStorageId, imageStorageId, StringComparison.OrdinalIgnoreCase)
            );
            if (removed > 0)
            {
                Save(entries);
                _logger.LogInformation(
                    "Removed template entry for image {ImageId}",
                    imageStorageId
                );
            }
        }
    }

    public List<TemplateEntry> LoadAll()
    {
        return Load();
    }

    private List<TemplateEntry> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<TemplateEntry>>(json)
                    ?? new List<TemplateEntry>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load template registry from {Path}", _filePath);
        }
        return new List<TemplateEntry>();
    }

    private void Save(List<TemplateEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
