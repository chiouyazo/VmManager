using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class DiskHealthMonitorCheck : IMonitoringCheck
{
    private readonly MetricsCache _cache;
    private readonly SettingsService _settingsService;

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

        foreach (DiskHealthInfo disk in _cache.GetDiskHealth())
        {
            if (!disk.Healthy)
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Critical,
                        CheckName = Name,
                        Title = "Disk unhealthy: " + disk.Device,
                        Message =
                            "SMART health check failed for "
                            + disk.Model
                            + " ("
                            + disk.Serial
                            + ").",
                    }
                );
            }
        }

        return Task.FromResult(alerts);
    }
}
