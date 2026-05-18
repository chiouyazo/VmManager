using System.Text.Json;
using VmManager.Contracts.Interfaces;
using VmManager.Models;

namespace VmManager.Services;

public class RdpPreferencesService
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public RdpPreferencesService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _filePath = Path.Combine(appPaths.AppDataDir, "rdp-preferences.json");
    }

    public RdpConnectionSettings Load()
    {
        if (!File.Exists(_filePath))
            return new RdpConnectionSettings();

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<RdpConnectionSettings>(json, JsonOptions)
                ?? new RdpConnectionSettings();
        }
        catch
        {
            return new RdpConnectionSettings();
        }
    }

    public void Save(RdpConnectionSettings settings)
    {
        string directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
