using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VmManager.Services;

public class VmTrackingService : IVmTrackingService
{
    private readonly IAppPaths _paths;
    private readonly ILogger<VmTrackingService> _logger;

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions()
    {
        WriteIndented = true,
    };

    private record ManagedVmEntry(string Name, VmOrigin? Origin);

    public VmTrackingService(IAppPaths paths, ILogger<VmTrackingService> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _paths = paths;
        _logger = logger;
    }

    public void TrackVm(string vmName, VmOrigin? origin)
    {
        _logger.LogInformation(
            "TrackVm: {VmName}, FeedId={FeedId}, FeedUrl={FeedUrl}, Repo={Repo}, ImageId={ImageId}",
            vmName,
            origin?.FeedId ?? "(null)",
            origin?.FeedUrl ?? "(null)",
            origin?.Repository ?? "(null)",
            origin?.ImageId ?? "(null)"
        );

        try
        {
            Dictionary<string, VmOrigin?> vms = LoadAllInternal();
            vms[vmName] = origin;
            SaveVms(vms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist VM tracking for {VmName}", vmName);
        }
    }

    public void UntrackVm(string vmName)
    {
        try
        {
            Dictionary<string, VmOrigin?> vms = LoadAllInternal();
            if (vms.Remove(vmName))
            {
                SaveVms(vms);
                _logger.LogInformation("Untracked VM {VmName}", vmName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to untrack VM {VmName}", vmName);
        }
    }

    public VmOrigin? GetOrigin(string vmName)
    {
        try
        {
            if (!File.Exists(_paths.ManagedVmsPath))
                return null;
            string json = File.ReadAllText(_paths.ManagedVmsPath);
            List<ManagedVmEntry> entries =
                JsonSerializer.Deserialize<List<ManagedVmEntry>>(json) ?? [];
            return entries.FirstOrDefault(e => e.Name == vmName)?.Origin;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read origin for VM {VmName}", vmName);
            return null;
        }
    }

    public Dictionary<string, VmOrigin?> LoadAll() => LoadAllInternal();

    public void PruneStaleEntries(IReadOnlySet<string> existingVmNames)
    {
        try
        {
            Dictionary<string, VmOrigin?> vms = LoadAllInternal();
            List<string> stale = vms.Keys.Where(name => !existingVmNames.Contains(name)).ToList();

            if (stale.Count == 0)
                return;

            foreach (string name in stale)
                vms.Remove(name);

            SaveVms(vms);
            _logger.LogInformation(
                "Pruned {Count} stale VM entries: {Names}",
                stale.Count,
                string.Join(", ", stale)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune stale VM entries");
        }
    }

    public Dictionary<string, string> LoadNotes()
    {
        try
        {
            if (File.Exists(_paths.NotesPath))
            {
                string json = File.ReadAllText(_paths.NotesPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VM notes");
        }
        return [];
    }

    public void SaveNote(string vmName, string note)
    {
        Dictionary<string, string> notes = LoadNotes();
        if (string.IsNullOrWhiteSpace(note))
            notes.Remove(vmName);
        else
            notes[vmName] = note;
        SaveNotes(notes);
    }

    public void RemoveNote(string vmName)
    {
        Dictionary<string, string> notes = LoadNotes();
        if (notes.Remove(vmName))
            SaveNotes(notes);
    }

    public void SetVmCredentials(string vmName, string vmUsername, string vmPassword) { }

    public (string? Username, string? Password) GetVmCredentials(string vmName) => (null, null);

    private Dictionary<string, VmOrigin?> LoadAllInternal()
    {
        try
        {
            if (File.Exists(_paths.ManagedVmsPath))
            {
                string json = File.ReadAllText(_paths.ManagedVmsPath);
                try
                {
                    List<ManagedVmEntry> entries =
                        JsonSerializer.Deserialize<List<ManagedVmEntry>>(json) ?? [];
                    return entries.ToDictionary(e => e.Name, e => e.Origin);
                }
                catch (JsonException)
                {
                    // Fall back to old format (plain string array) and migrate
                    _logger.LogInformation("Migrating managed-vms.json from old format");
                    HashSet<string> names = JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
                    Dictionary<string, VmOrigin?> migrated = names.ToDictionary(
                        n => n,
                        _ => (VmOrigin?)null
                    );
                    SaveVms(migrated);
                    return migrated;
                }
            }
            else
            {
                // First run: seed from VMs that have notes
                Dictionary<string, VmOrigin?> vms = [];
                if (File.Exists(_paths.NotesPath))
                {
                    string notesJson = File.ReadAllText(_paths.NotesPath);
                    Dictionary<string, string>? notes = JsonSerializer.Deserialize<
                        Dictionary<string, string>
                    >(notesJson);
                    if (notes != null)
                        vms = notes.Keys.ToDictionary(n => n, _ => (VmOrigin?)null);
                }
                SaveVms(vms);
                return vms;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load managed VMs from {Path}", _paths.ManagedVmsPath);
            return [];
        }
    }

    private void SaveVms(Dictionary<string, VmOrigin?> vms)
    {
        List<ManagedVmEntry> entries = vms.Select(kv => new ManagedVmEntry(kv.Key, kv.Value))
            .ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ManagedVmsPath)!);
        File.WriteAllText(_paths.ManagedVmsPath, JsonSerializer.Serialize(entries, WriteOptions));
    }

    private void SaveNotes(Dictionary<string, string> notes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.NotesPath)!);
            File.WriteAllText(_paths.NotesPath, JsonSerializer.Serialize(notes, WriteOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save VM notes");
        }
    }
}
