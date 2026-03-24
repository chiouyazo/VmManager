using System.IO;
using System.Text.Json;
using VmManager.Models;

namespace VmManager.Services;

/// <summary>Loads and saves user settings from AppData\Roaming\VmManager\settings.json.</summary>
public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VmManager",
        "settings.json"
    );

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            // First run - write defaults so the file exists for future loads.
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, WriteOptions));
    }

    // Async wrappers kept for compatibility with pages that use await
    public Task<AppSettings> LoadAsync() => Task.FromResult(Load());

    public Task SaveAsync(AppSettings settings)
    {
        Save(settings);
        return Task.CompletedTask;
    }
}
