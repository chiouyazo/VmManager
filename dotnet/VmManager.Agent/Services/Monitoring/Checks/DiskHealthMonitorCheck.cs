using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class DiskHealthMonitorCheck : IMonitoringCheck
{
    private readonly MetricsCache _cache;
    private readonly SettingsService _settingsService;
    private readonly Dictionary<string, MonitoringAlert> _activeAlerts =
        new Dictionary<string, MonitoringAlert>();

    public string Name => "DiskHealth";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.DiskHealthIntervalSeconds ?? 3600);

    public DiskHealthMonitorCheck(MetricsCache cache, SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(settingsService);
        _cache = cache;
        _settingsService = settingsService;
    }

    public Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        HashSet<string> currentUnhealthy = new HashSet<string>();

        foreach (DiskHealthInfo disk in _cache.GetDiskHealth())
        {
            string key = disk.Device + "|" + disk.Serial;

            if (!disk.Healthy)
            {
                currentUnhealthy.Add(key);

                if (!_activeAlerts.ContainsKey(key))
                {
                    MonitoringAlert alert = new MonitoringAlert
                    {
                        Severity = AlertSeverity.Critical,
                        CheckName = Name,
                        Title = "Disk unhealthy: " + disk.Device,
                        Message =
                            "SMART status: "
                            + disk.HealthStatus
                            + "\nDevice: "
                            + disk.Device
                            + "\nModel: "
                            + disk.Model
                            + "\nSerial: "
                            + disk.Serial
                            + (
                                disk.WearLevelPercent.HasValue
                                    ? "\nWear level: " + disk.WearLevelPercent.Value + "%"
                                    : ""
                            )
                            + (
                                disk.TemperatureCelsius.HasValue
                                    ? "\nTemperature: " + disk.TemperatureCelsius.Value + " C"
                                    : ""
                            ),
                    };
                    alerts.Add(alert);
                    _activeAlerts[key] = alert;
                }
            }
        }

        List<string> recovered = new List<string>();
        foreach (KeyValuePair<string, MonitoringAlert> kvp in _activeAlerts)
        {
            if (!currentUnhealthy.Contains(kvp.Key))
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = Name,
                        Title = "Disk recovered: " + kvp.Key.Split('|')[0],
                        Message = "SMART health check passed. Previous alert: " + kvp.Value.Id,
                        Id = kvp.Value.Id + "-resolved",
                    }
                );
                recovered.Add(kvp.Key);
            }
        }

        foreach (string key in recovered)
            _activeAlerts.Remove(key);

        return Task.FromResult(alerts);
    }
}
