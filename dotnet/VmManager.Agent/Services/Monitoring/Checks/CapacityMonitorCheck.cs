using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class CapacityMonitorCheck : IMonitoringCheck
{
    private const double HysteresisPercent = 5.0;

    private readonly IVmBackend _backend;
    private readonly SettingsService _settingsService;
    private bool _alertActive;
    private MonitoringAlert? _activeAlert;

    public string Name => "Capacity";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.CapacityIntervalSeconds ?? 900);

    public CapacityMonitorCheck(IVmBackend backend, SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(settingsService);
        _backend = backend;
        _settingsService = settingsService;
    }

    public async Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        AppSettings settings = _settingsService.Load();
        MonitoringThresholds thresholds =
            settings.Monitoring?.Thresholds ?? new MonitoringThresholds();

        if (settings.MaxTotalVms <= 0)
            return alerts;

        List<VmInstance> vms = await _backend.GetVmsAsync();
        double usedPercent = (double)vms.Count / settings.MaxTotalVms * 100;

        if (!_alertActive && usedPercent >= thresholds.CapacityPercentWarning)
        {
            _activeAlert = new MonitoringAlert
            {
                Severity = AlertSeverity.Warning,
                CheckName = Name,
                Title = "VM capacity at " + usedPercent.ToString("F0") + "%",
                Message = vms.Count + " of " + settings.MaxTotalVms + " VMs in use.",
            };
            alerts.Add(_activeAlert);
            _alertActive = true;
        }
        else if (
            _alertActive
            && usedPercent < thresholds.CapacityPercentWarning - HysteresisPercent
        )
        {
            if (_activeAlert != null)
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = Name,
                        Title = "VM capacity recovered: " + usedPercent.ToString("F0") + "%",
                        Message =
                            vms.Count
                            + " of "
                            + settings.MaxTotalVms
                            + " VMs. Previous alert: "
                            + _activeAlert.Id,
                        Id = _activeAlert.Id + "-resolved",
                    }
                );
            }
            _alertActive = false;
            _activeAlert = null;
        }

        return alerts;
    }
}
