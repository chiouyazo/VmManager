using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VmManager.Services;

public class TempTracker : ITempTracker
{
    private readonly string _trackingFile;

    private readonly ILogger<TempTracker> _logger;
    private readonly HashSet<string> _activePaths = [];
    private readonly Lock _lock = new Lock();

    public TempTracker(IAppPaths paths, ILogger<TempTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _trackingFile = paths.PendingCleanupPath;
        _logger = logger;
    }

    public void CleanupOrphans()
    {
        if (!File.Exists(_trackingFile))
            return;

        try
        {
            string json = File.ReadAllText(_trackingFile);
            List<string>? paths = JsonSerializer.Deserialize<List<string>>(json);
            if (paths == null || paths.Count == 0)
                return;

            _logger.LogInformation(
                "Found {Count} orphaned temp paths from previous session",
                paths.Count
            );

            foreach (string path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        _logger.LogInformation("Cleaned up orphaned temp dir: {Path}", path);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                        _logger.LogInformation("Cleaned up orphaned temp file: {Path}", path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up orphaned path: {Path}", path);
                }
            }

            // Also clean any stale vmm_push_* or vmm_clone_* dirs in temp
            CleanupStaleVmmTempDirs();

            File.Delete(_trackingFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process orphan cleanup file");
        }
    }

    public void Register(string path)
    {
        lock (_lock)
        {
            _activePaths.Add(path);
            Persist();
        }
        _logger.LogDebug("Registered temp path: {Path}", path);
    }

    public void Unregister(string path)
    {
        lock (_lock)
        {
            _activePaths.Remove(path);
            Persist();
        }
        _logger.LogDebug("Unregistered temp path: {Path}", path);
    }

    public string CreateTrackedTempDir(string prefix = "vmm")
    {
        string dir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        Register(dir);
        return dir;
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_trackingFile)!);
            if (_activePaths.Count == 0)
            {
                if (File.Exists(_trackingFile))
                    File.Delete(_trackingFile);
            }
            else
            {
                File.WriteAllText(_trackingFile, JsonSerializer.Serialize(_activePaths.ToList()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist temp tracking file");
        }
    }

    private void CleanupStaleVmmTempDirs()
    {
        try
        {
            string tempRoot = Path.GetTempPath();
            foreach (string dir in Directory.GetDirectories(tempRoot, "vmm_push_*"))
            {
                DirectoryInfo info = new DirectoryInfo(dir);
                if (info.CreationTime < DateTime.Now.AddHours(-1))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        _logger.LogInformation("Cleaned up stale temp dir: {Path}", dir);
                    }
                    catch
                    { /* Cleanup failure is non-fatal */
                    }
                }
            }
            foreach (string dir in Directory.GetDirectories(tempRoot, "vmm-clone-*"))
            {
                DirectoryInfo info = new DirectoryInfo(dir);
                if (info.CreationTime < DateTime.Now.AddHours(-1))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        _logger.LogInformation("Cleaned up stale clone dir: {Path}", dir);
                    }
                    catch
                    { /* Cleanup failure is non-fatal */
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan for stale temp directories");
        }
    }
}
