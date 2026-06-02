using System.Collections.Concurrent;

namespace VmManager.Agent.Services.Monitoring;

public sealed class LoginAttemptTracker
{
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _failedAttempts =
        new ConcurrentDictionary<string, List<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);

    public void RecordFailedAttempt(string username, string? sourceIp = null)
    {
        string key = username + "|" + (sourceIp ?? "unknown");
        List<DateTimeOffset> attempts = _failedAttempts.GetOrAdd(
            key,
            _ => new List<DateTimeOffset>()
        );
        lock (attempts)
        {
            attempts.Add(DateTimeOffset.UtcNow);
        }
    }

    public int GetFailedAttemptCount(string username, TimeSpan window)
    {
        int count = 0;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - window;

        foreach (KeyValuePair<string, List<DateTimeOffset>> entry in _failedAttempts)
        {
            if (!entry.Key.StartsWith(username + "|", StringComparison.OrdinalIgnoreCase))
                continue;

            lock (entry.Value)
            {
                count += entry.Value.Count(t => t >= cutoff);
            }
        }

        return count;
    }

    public int GetFailedAttemptCountByIp(string sourceIp, TimeSpan window)
    {
        int count = 0;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - window;
        string ipSuffix = "|" + sourceIp;

        foreach (KeyValuePair<string, List<DateTimeOffset>> entry in _failedAttempts)
        {
            if (!entry.Key.EndsWith(ipSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            lock (entry.Value)
            {
                count += entry.Value.Count(t => t >= cutoff);
            }
        }

        return count;
    }

    public void Cleanup(TimeSpan maxAge)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (KeyValuePair<string, List<DateTimeOffset>> entry in _failedAttempts)
        {
            lock (entry.Value)
            {
                entry.Value.RemoveAll(t => t < cutoff);
            }

            if (entry.Value.Count == 0)
                _failedAttempts.TryRemove(entry.Key, out _);
        }
    }
}
