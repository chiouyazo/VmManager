using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring;

public sealed class MonitoringService : BackgroundService
{
    private readonly IEnumerable<IMonitoringCheck> _checks;
    private readonly IMetricsProvider _metricsProvider;
    private readonly AlertStore _alertStore;
    private readonly AlertNotifier _alertNotifier;
    private readonly MetricsCache _metricsCache;
    private readonly SettingsService _settingsService;
    private readonly ILogger<MonitoringService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastRunTimes =
        new Dictionary<string, DateTimeOffset>();

    public MonitoringService(
        IEnumerable<IMonitoringCheck> checks,
        IMetricsProvider metricsProvider,
        AlertStore alertStore,
        AlertNotifier alertNotifier,
        MetricsCache metricsCache,
        SettingsService settingsService,
        ILogger<MonitoringService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(metricsProvider);
        ArgumentNullException.ThrowIfNull(alertStore);
        ArgumentNullException.ThrowIfNull(alertNotifier);
        ArgumentNullException.ThrowIfNull(metricsCache);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);
        _checks = checks;
        _metricsProvider = metricsProvider;
        _alertStore = alertStore;
        _alertNotifier = alertNotifier;
        _metricsCache = metricsCache;
        _settingsService = settingsService;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, DateTimeOffset> LastRunTimes => _lastRunTimes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        _logger.LogInformation("Monitoring service started with {Count} checks", _checks.Count());

        while (!stoppingToken.IsCancellationRequested)
        {
            MonitoringSettings? settings = _settingsService.Load().Monitoring;
            if (settings == null || !settings.Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            await RefreshMetricsAsync(stoppingToken);

            foreach (IMonitoringCheck check in _checks)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                DateTimeOffset lastRun = _lastRunTimes.GetValueOrDefault(
                    check.Name,
                    DateTimeOffset.MinValue
                );
                if (DateTimeOffset.UtcNow - lastRun < check.Interval)
                    continue;

                try
                {
                    List<MonitoringAlert> alerts = await check.ExecuteAsync(stoppingToken);
                    _lastRunTimes[check.Name] = DateTimeOffset.UtcNow;

                    if (alerts.Count > 0)
                    {
                        foreach (MonitoringAlert alert in alerts)
                        {
                            bool isResolution = alert.Id.EndsWith("-resolved");

                            _logger.LogInformation(
                                "[{Severity}] {CheckName}: {Title}",
                                alert.Severity,
                                alert.CheckName,
                                alert.Title
                            );

                            _alertStore.Add(alert);

                            try
                            {
                                if (isResolution && AlertNotifier.IsResolvable(alert.CheckName))
                                    await _alertNotifier.NotifyAsync(alert, isResolved: true);
                                else if (!isResolution)
                                    await _alertNotifier.NotifyAsync(alert);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Failed to send notification for alert {AlertId}",
                                    alert.Id
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Monitoring check {CheckName} failed", check.Name);
                }
            }

            _alertStore.CleanupOld(settings.AlertRetentionDays, settings.MaxAlertCount);

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task RefreshMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            HostMetrics host = await _metricsProvider.GetHostMetricsAsync(cancellationToken);
            _metricsCache.UpdateHostMetrics(host);

            List<VmMetrics> vms = await _metricsProvider.GetVmMetricsAsync(cancellationToken);
            _metricsCache.UpdateVmMetrics(vms);

            List<StorageMetrics> storage = await _metricsProvider.GetStorageMetricsAsync(
                cancellationToken
            );
            _metricsCache.UpdateStorageMetrics(storage);

            List<DiskHealthInfo> disks = await _metricsProvider.GetDiskHealthAsync(
                cancellationToken
            );
            _metricsCache.UpdateDiskHealth(disks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh metrics from hypervisor");
        }
    }
}
