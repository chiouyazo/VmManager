using System.Text.Json;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Shared;

/// <summary>Loads and saves user settings from AppData\Roaming\VmManager\settings.json.</summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly object _fileLock = new();

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
        lock (_fileLock)
        {
            if (!File.Exists(_settingsPath))
            {
                AppSettings defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(_settingsPath);
                AppSettings settings =
                    JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();

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
    }

    public void Save(AppSettings settings)
    {
        lock (_fileLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            string json = JsonSerializer.Serialize(settings, WriteOptions);
            string tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, true);
        }
    }

    // Async wrappers kept for compatibility with pages that use await
    public Task<AppSettings> LoadAsync() => Task.FromResult(Load());

    public Task SaveAsync(AppSettings settings)
    {
        Save(settings);
        return Task.CompletedTask;
    }
}
