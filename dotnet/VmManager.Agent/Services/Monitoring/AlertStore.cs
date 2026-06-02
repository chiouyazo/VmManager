using System.Text.Json;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring;

public sealed class AlertStore
{
    private readonly string _filePath;
    private readonly ILogger<AlertStore> _logger;
    private readonly object _lock = new object();
    private List<MonitoringAlert> _alerts = new List<MonitoringAlert>();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AlertStore(IAppPaths paths, ILogger<AlertStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.Combine(paths.AppDataDir, "alerts.json");
        _logger = logger;
        Load();
    }

    public void Add(MonitoringAlert alert)
    {
        lock (_lock)
        {
            _alerts.Add(alert);
            Save();
        }
    }

    public void AddRange(List<MonitoringAlert> alerts)
    {
        if (alerts.Count == 0)
            return;
        lock (_lock)
        {
            _alerts.AddRange(alerts);
            Save();
        }
    }

    public List<MonitoringAlert> Query(
        AlertSeverity? severity = null,
        string? vmName = null,
        DateTimeOffset? since = null,
        bool? acknowledged = null,
        int limit = 100,
        int offset = 0
    )
    {
        lock (_lock)
        {
            IEnumerable<MonitoringAlert> query = _alerts.AsEnumerable().Reverse();

            if (severity.HasValue)
                query = query.Where(a => a.Severity == severity.Value);
            if (!string.IsNullOrEmpty(vmName))
                query = query.Where(a =>
                    string.Equals(a.VmName, vmName, StringComparison.OrdinalIgnoreCase)
                );
            if (since.HasValue)
                query = query.Where(a => a.Timestamp >= since.Value);
            if (acknowledged.HasValue)
                query = query.Where(a => a.Acknowledged == acknowledged.Value);

            return query.Skip(offset).Take(limit).ToList();
        }
    }

    public MonitoringAlert? GetById(string id)
    {
        lock (_lock)
        {
            return _alerts.FirstOrDefault(a => a.Id == id);
        }
    }

    public bool Acknowledge(string id, string acknowledgedBy)
    {
        lock (_lock)
        {
            MonitoringAlert? alert = _alerts.FirstOrDefault(a => a.Id == id);
            if (alert == null)
                return false;
            alert.Acknowledged = true;
            alert.AcknowledgedAt = DateTimeOffset.UtcNow;
            alert.AcknowledgedBy = acknowledgedBy;
            Save();
            return true;
        }
    }

    public int AcknowledgeAll(string acknowledgedBy, AlertSeverity? severity = null)
    {
        lock (_lock)
        {
            int count = 0;
            foreach (MonitoringAlert alert in _alerts)
            {
                if (alert.Acknowledged)
                    continue;
                if (severity.HasValue && alert.Severity != severity.Value)
                    continue;
                alert.Acknowledged = true;
                alert.AcknowledgedAt = DateTimeOffset.UtcNow;
                alert.AcknowledgedBy = acknowledgedBy;
                count++;
            }
            if (count > 0)
                Save();
            return count;
        }
    }

    public Dictionary<AlertSeverity, int> GetActiveAlertCounts()
    {
        lock (_lock)
        {
            return _alerts
                .Where(a => !a.Acknowledged)
                .GroupBy(a => a.Severity)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public void CleanupOld(int retentionDays, int maxCount)
    {
        lock (_lock)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            _alerts.RemoveAll(a => a.Timestamp < cutoff);
            if (_alerts.Count > maxCount)
                _alerts = _alerts.Skip(_alerts.Count - maxCount).ToList();
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                _alerts =
                    JsonSerializer.Deserialize<List<MonitoringAlert>>(json, JsonOptions)
                    ?? new List<MonitoringAlert>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load alerts from {Path}", _filePath);
            _alerts = new List<MonitoringAlert>();
        }
    }

    private void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_alerts, JsonOptions);
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save alerts to {Path}", _filePath);
        }
    }
}
