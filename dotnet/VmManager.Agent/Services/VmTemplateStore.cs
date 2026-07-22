using System.Text.Json;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

/// <summary>
/// Persistent registry of user-created VM templates (see <see cref="VmTemplate"/>).
/// Backend-agnostic storage in vm-templates.json. Distinct from
/// <see cref="ProxmoxTemplateRegistry"/>, which maps image storage ids to
/// internal templates used for fast image-based VM creation.
/// </summary>
public sealed class VmTemplateStore
{
    private readonly string _filePath;
    private readonly ILogger<VmTemplateStore> _logger;
    private readonly object _lock = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public VmTemplateStore(IAppPaths paths, ILogger<VmTemplateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.Combine(paths.AppDataDir, "vm-templates.json");
        _logger = logger;
    }

    public List<VmTemplate> LoadAll()
    {
        return Load();
    }

    public VmTemplate? Get(int templateVmId)
    {
        return Load().Find(t => t.TemplateVmId == templateVmId);
    }

    public void Add(VmTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        lock (_lock)
        {
            List<VmTemplate> entries = Load();
            entries.RemoveAll(t => t.TemplateVmId == template.TemplateVmId);
            entries.Add(template);
            Save(entries);
            _logger.LogInformation(
                "Registered VM template '{Name}' (VMID {VmId}) created by {User}",
                template.Name,
                template.TemplateVmId,
                template.CreatedBy
            );
        }
    }

    public void Remove(int templateVmId)
    {
        lock (_lock)
        {
            List<VmTemplate> entries = Load();
            int removed = entries.RemoveAll(t => t.TemplateVmId == templateVmId);
            if (removed > 0)
            {
                Save(entries);
                _logger.LogInformation("Removed VM template entry VMID {VmId}", templateVmId);
            }
        }
    }

    private List<VmTemplate> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<VmTemplate>>(json) ?? new List<VmTemplate>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VM template store from {Path}", _filePath);
        }
        return new List<VmTemplate>();
    }

    private void Save(List<VmTemplate> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
