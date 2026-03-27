using System.Text.Json;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Shared;

/// <summary>Loads and saves user settings from AppData\Roaming\VmManager\settings.json.</summary>
public class SettingsService
{
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    public SettingsService(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _settingsPath = paths.SettingsPath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            // First run - write defaults so the file exists for future loads.
            AppSettings defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();

            // Migrate feeds to deterministic IDs if needed
            bool migrated = false;
            foreach (FeedConfiguration feed in settings.Feeds)
            {
                string deterministicId = FeedConfiguration.ComputeId(
                    feed.Type,
                    feed.Url,
                    feed.Repository
                );
                if (feed.Id != deterministicId)
                {
                    feed.Id = deterministicId;
                    migrated = true;
                }
            }
            if (migrated)
                Save(settings);

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, WriteOptions));
    }

    // Async wrappers kept for compatibility with pages that use await
    public Task<AppSettings> LoadAsync() => Task.FromResult(Load());

    public Task SaveAsync(AppSettings settings)
    {
        Save(settings);
        return Task.CompletedTask;
    }
}
