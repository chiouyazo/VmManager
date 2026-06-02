using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class HostResourceMonitorCheck : IMonitoringCheck
{
    private const double HysteresisPercent = 5.0;

    private readonly MetricsCache _cache;
    private readonly SettingsService _settingsService;
    private bool _cpuAlertActive;
    private bool _memoryAlertActive;
    private MonitoringAlert? _activeCpuAlert;
    private MonitoringAlert? _activeMemoryAlert;

    public string Name => "HostCpu";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.HostHealthIntervalSeconds ?? 300);

    public HostResourceMonitorCheck(MetricsCache cache, SettingsService settingsService)
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

        HostMetrics host = _cache.GetHostMetrics();

        // CPU check with hysteresis
        if (!_cpuAlertActive)
        {
            if (host.CpuPercent >= thresholds.HostCpuPercentCritical)
            {
                _activeCpuAlert = new MonitoringAlert
                {
                    Severity = AlertSeverity.Critical,
                    CheckName = "HostCpu",
                    Title = "Host CPU critical: " + host.CpuPercent.ToString("F1") + "%",
                    Message =
                        "CPU usage exceeded " + thresholds.HostCpuPercentCritical + "% threshold.",
                };
                alerts.Add(_activeCpuAlert);
                _cpuAlertActive = true;
            }
            else if (host.CpuPercent >= thresholds.HostCpuPercentWarning)
            {
                _activeCpuAlert = new MonitoringAlert
                {
                    Severity = AlertSeverity.Warning,
                    CheckName = "HostCpu",
                    Title = "Host CPU high: " + host.CpuPercent.ToString("F1") + "%",
                    Message =
                        "CPU usage exceeded " + thresholds.HostCpuPercentWarning + "% threshold.",
                };
                alerts.Add(_activeCpuAlert);
                _cpuAlertActive = true;
            }
        }
        else if (host.CpuPercent < thresholds.HostCpuPercentWarning - HysteresisPercent)
        {
            if (_activeCpuAlert != null)
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = "HostCpu",
                        Title = "Host CPU recovered: " + host.CpuPercent.ToString("F1") + "%",
                        Message =
                            "CPU usage dropped below threshold. Previous alert: "
                            + _activeCpuAlert.Id,
                        Id = _activeCpuAlert.Id + "-resolved",
                    }
                );
            }
            _cpuAlertActive = false;
            _activeCpuAlert = null;
        }

        // Memory check with hysteresis
        if (host.MemoryTotalBytes > 0)
        {
            double memPercent = (double)host.MemoryUsedBytes / host.MemoryTotalBytes * 100;

            if (!_memoryAlertActive)
            {
                if (memPercent >= thresholds.HostMemoryPercentCritical)
                {
                    _activeMemoryAlert = new MonitoringAlert
                    {
                        Severity = AlertSeverity.Critical,
                        CheckName = "HostMemory",
                        Title = "Host memory critical: " + memPercent.ToString("F1") + "%",
                        Message =
                            "Memory usage exceeded "
                            + thresholds.HostMemoryPercentCritical
                            + "% threshold.",
                    };
                    alerts.Add(_activeMemoryAlert);
                    _memoryAlertActive = true;
                }
                else if (memPercent >= thresholds.HostMemoryPercentWarning)
                {
                    _activeMemoryAlert = new MonitoringAlert
                    {
                        Severity = AlertSeverity.Warning,
                        CheckName = "HostMemory",
                        Title = "Host memory high: " + memPercent.ToString("F1") + "%",
                        Message =
                            "Memory usage exceeded "
                            + thresholds.HostMemoryPercentWarning
                            + "% threshold.",
                    };
                    alerts.Add(_activeMemoryAlert);
                    _memoryAlertActive = true;
                }
            }
            else if (memPercent < thresholds.HostMemoryPercentWarning - HysteresisPercent)
            {
                if (_activeMemoryAlert != null)
                {
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Info,
                            CheckName = "HostMemory",
                            Title = "Host memory recovered: " + memPercent.ToString("F1") + "%",
                            Message =
                                "Memory usage dropped below threshold. Previous alert: "
                                + _activeMemoryAlert.Id,
                            Id = _activeMemoryAlert.Id + "-resolved",
                        }
                    );
                }
                _memoryAlertActive = false;
                _activeMemoryAlert = null;
            }
        }

        return Task.FromResult(alerts);
    }
}
