using System.Text.Json;

namespace VmManager.Agent.Services;

public class EnvironmentStore
{
    private readonly string _path;
    private readonly ILogger<EnvironmentStore> _logger;
    private static readonly object FileLock = new object();

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions()
    {
        WriteIndented = true,
    };

    public EnvironmentStore(IAppPaths paths, ILogger<EnvironmentStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _path = paths.EnvironmentsPath;
        _logger = logger;
    }

    public List<EnvironmentMetadata> GetAll()
    {
        lock (FileLock)
        {
            return Load();
        }
    }

    public EnvironmentMetadata? Get(string key)
    {
        lock (FileLock)
        {
            return Load().FirstOrDefault(e => KeyEquals(e.Key, key));
        }
    }

    public EnvironmentMetadata? GetByVmName(string vmName)
    {
        lock (FileLock)
        {
            return Load().FirstOrDefault(e => KeyEquals(e.VmName, vmName));
        }
    }

    public void Upsert(EnvironmentMetadata env)
    {
        ArgumentNullException.ThrowIfNull(env);
        lock (FileLock)
        {
            List<EnvironmentMetadata> all = Load();
            all.RemoveAll(e => KeyEquals(e.Key, env.Key));
            all.Add(env);
            Save(all);
        }
    }

    public void Remove(string key)
    {
        lock (FileLock)
        {
            List<EnvironmentMetadata> all = Load();
            if (all.RemoveAll(e => KeyEquals(e.Key, key)) > 0)
                Save(all);
        }
    }

    private static bool KeyEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private List<EnvironmentMetadata> Load()
    {
        if (!File.Exists(_path))
            return [];
        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<EnvironmentMetadata>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load environments from {Path}", _path);
            return [];
        }
    }

    private void Save(List<EnvironmentMetadata> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string json = JsonSerializer.Serialize(all, WriteOptions);
            string tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save environments to {Path}", _path);
        }
    }
}
