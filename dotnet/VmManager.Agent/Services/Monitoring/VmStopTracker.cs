using System.Collections.Concurrent;

namespace VmManager.Agent.Services.Monitoring;

public sealed class VmStopTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentStops =
        new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

    public void RecordStop(string vmName)
    {
        _recentStops[vmName] = DateTimeOffset.UtcNow;
    }

    public bool WasRecentlyStoppedByManager(string vmName, TimeSpan window)
    {
        if (!_recentStops.TryGetValue(vmName, out DateTimeOffset stoppedAt))
            return false;

        if (DateTimeOffset.UtcNow - stoppedAt > window)
        {
            _recentStops.TryRemove(vmName, out _);
            return false;
        }

        return true;
    }

    public void Cleanup()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        foreach (KeyValuePair<string, DateTimeOffset> entry in _recentStops)
        {
            if (entry.Value < cutoff)
                _recentStops.TryRemove(entry.Key, out _);
        }
    }
}
