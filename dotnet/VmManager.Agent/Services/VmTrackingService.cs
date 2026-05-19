using System.Text.Json;

namespace VmManager.Agent.Services;

public class VmTrackingService : IVmTrackingService
{
    private readonly IAppPaths _paths;
    private readonly ILogger<VmTrackingService> _logger;

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions()
    {
        WriteIndented = true,
    };

    private record ManagedVmEntry(string Name, VmOrigin? Origin, DateTime? CreatedAt = null);

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
            List<ManagedVmEntry> entries = LoadEntries();
            entries.RemoveAll(e => e.Name == vmName);
            entries.Add(new ManagedVmEntry(vmName, origin, DateTime.UtcNow));
            SaveEntries(entries);
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
            List<ManagedVmEntry> entries = LoadEntries();
            if (entries.RemoveAll(e => e.Name == vmName) > 0)
            {
                SaveEntries(entries);
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

    public DateTime? GetCreatedAt(string vmName)
    {
        try
        {
            List<ManagedVmEntry> entries = LoadEntries();
            return entries.FirstOrDefault(e => e.Name == vmName)?.CreatedAt;
        }
        catch
        {
            return null;
        }
    }

    public List<(string Name, DateTime CreatedAt, string Owner)> GetVmsOlderThan(
        int days,
        VmOwnershipService ownershipService
    )
    {
        List<ManagedVmEntry> entries = LoadEntries();
        DateTime cutoff = DateTime.UtcNow.AddDays(-days);
        List<(string, DateTime, string)> result = new List<(string, DateTime, string)>();
        foreach (ManagedVmEntry entry in entries)
        {
            if (entry.CreatedAt.HasValue && entry.CreatedAt.Value < cutoff)
            {
                string owner = ownershipService.GetOwner(entry.Name);
                result.Add((entry.Name, entry.CreatedAt.Value, owner));
            }
        }
        return result;
    }

    public void PruneStaleEntries(IReadOnlySet<string> existingVmNames)
    {
        try
        {
            List<ManagedVmEntry> entries = LoadEntries();
            int removed = entries.RemoveAll(e => !existingVmNames.Contains(e.Name));

            if (removed == 0)
                return;

            SaveEntries(entries);
            _logger.LogInformation("Pruned {Count} stale VM entries", removed);
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

    private Dictionary<string, VmOrigin?> LoadAllInternal()
    {
        return LoadEntries().ToDictionary(e => e.Name, e => e.Origin);
    }

    private List<ManagedVmEntry> LoadEntries()
    {
        try
        {
            if (File.Exists(_paths.ManagedVmsPath))
            {
                string json = File.ReadAllText(_paths.ManagedVmsPath);
                try
                {
                    return JsonSerializer.Deserialize<List<ManagedVmEntry>>(json) ?? [];
                }
                catch (JsonException)
                {
                    _logger.LogInformation("Migrating managed-vms.json from old format");
                    HashSet<string> names = JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
                    List<ManagedVmEntry> migrated = names
                        .Select(n => new ManagedVmEntry(n, null))
                        .ToList();
                    SaveEntries(migrated);
                    return migrated;
                }
            }
            else
            {
                List<ManagedVmEntry> entries = [];
                if (File.Exists(_paths.NotesPath))
                {
                    string notesJson = File.ReadAllText(_paths.NotesPath);
                    Dictionary<string, string>? notes = JsonSerializer.Deserialize<
                        Dictionary<string, string>
                    >(notesJson);
                    if (notes != null)
                        entries = notes.Keys.Select(n => new ManagedVmEntry(n, null)).ToList();
                }
                SaveEntries(entries);
                return entries;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load managed VMs from {Path}", _paths.ManagedVmsPath);
            return [];
        }
    }

    private void SaveEntries(List<ManagedVmEntry> entries)
    {
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
