using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class StorageMonitorCheck : IMonitoringCheck
{
    private const double HysteresisPercent = 5.0;

    private readonly MetricsCache _cache;
    private readonly SettingsService _settingsService;
    private readonly Dictionary<string, MonitoringAlert> _activeAlerts = new Dictionary<
        string,
        MonitoringAlert
    >(StringComparer.OrdinalIgnoreCase);

    public string Name => "Storage";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.HostHealthIntervalSeconds ?? 300);

    public StorageMonitorCheck(MetricsCache cache, SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(settingsService);
        _cache = cache;
        _settingsService = settingsService;
    }

    public Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        MonitoringThresholds thresholds =
            _settingsService.Load().Monitoring?.Thresholds ?? new MonitoringThresholds();

        foreach (StorageMetrics storage in _cache.GetStorageMetrics())
        {
            if (storage.TotalBytes == 0)
                continue;
            double freePercent = 100 - storage.UsedPercent;

            if (!_activeAlerts.ContainsKey(storage.Name))
            {
                if (freePercent <= thresholds.StorageFreePercentCritical)
                {
                    MonitoringAlert alert = new MonitoringAlert
                    {
                        Severity = AlertSeverity.Critical,
                        CheckName = Name,
                        Title =
                            "Storage '"
                            + storage.Name
                            + "' critically low: "
                            + freePercent.ToString("F1")
                            + "% free",
                        Message =
                            "Free space below "
                            + thresholds.StorageFreePercentCritical
                            + "% threshold. "
                            + "Used: "
                            + (storage.UsedBytes / (1024.0 * 1024 * 1024)).ToString("F0")
                            + " GB / "
                            + (storage.TotalBytes / (1024.0 * 1024 * 1024)).ToString("F0")
                            + " GB.",
                    };
                    alerts.Add(alert);
                    _activeAlerts[storage.Name] = alert;
                }
                else if (freePercent <= thresholds.StorageFreePercentWarning)
                {
                    MonitoringAlert alert = new MonitoringAlert
                    {
                        Severity = AlertSeverity.Warning,
                        CheckName = Name,
                        Title =
                            "Storage '"
                            + storage.Name
                            + "' low: "
                            + freePercent.ToString("F1")
                            + "% free",
                        Message =
                            "Free space below "
                            + thresholds.StorageFreePercentWarning
                            + "% threshold. "
                            + "Used: "
                            + (storage.UsedBytes / (1024.0 * 1024 * 1024)).ToString("F0")
                            + " GB / "
                            + (storage.TotalBytes / (1024.0 * 1024 * 1024)).ToString("F0")
                            + " GB.",
                    };
                    alerts.Add(alert);
                    _activeAlerts[storage.Name] = alert;
                }
            }
            else if (freePercent > thresholds.StorageFreePercentWarning + HysteresisPercent)
            {
                MonitoringAlert original = _activeAlerts[storage.Name];
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = Name,
                        Title =
                            "Storage '"
                            + storage.Name
                            + "' recovered: "
                            + freePercent.ToString("F1")
                            + "% free",
                        Message =
                            "Storage free space is back above threshold. Previous alert: "
                            + original.Id,
                        Id = original.Id + "-resolved",
                    }
                );
                _activeAlerts.Remove(storage.Name);
            }
        }

        return Task.FromResult(alerts);
    }
}
