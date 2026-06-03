using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class CapacityMonitorCheck : IMonitoringCheck
{
    private const double HysteresisPercent = 5.0;

    private readonly IVmBackend _backend;
    private readonly SettingsService _settingsService;
    private bool _alertActive;
    private MonitoringAlert? _activeAlert;
    private bool _idRangeAlertActive;
    private MonitoringAlert? _activeIdRangeAlert;

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

        // VM ID range check (Proxmox only)
        ProxmoxSettings? proxmox = settings.Proxmox;
        if (
            proxmox != null
            && proxmox.VmIdRangeStart > 0
            && proxmox.VmIdRangeEnd > proxmox.VmIdRangeStart
        )
        {
            int rangeSize = proxmox.VmIdRangeEnd - proxmox.VmIdRangeStart + 1;
            int usedInRange = vms.Count;
            double rangeUsedPercent = (double)usedInRange / rangeSize * 100;

            if (!_idRangeAlertActive && rangeUsedPercent >= thresholds.CapacityPercentWarning)
            {
                int remaining = rangeSize - usedInRange;
                AlertSeverity severity =
                    remaining <= 5 ? AlertSeverity.Critical : AlertSeverity.Warning;

                _activeIdRangeAlert = new MonitoringAlert
                {
                    Severity = severity,
                    CheckName = Name,
                    Title =
                        "VM ID range "
                        + rangeUsedPercent.ToString("F0")
                        + "% used ("
                        + remaining
                        + " IDs remaining)",
                    Message =
                        usedInRange
                        + " of "
                        + rangeSize
                        + " IDs used in range "
                        + proxmox.VmIdRangeStart
                        + "-"
                        + proxmox.VmIdRangeEnd
                        + ".",
                };
                alerts.Add(_activeIdRangeAlert);
                _idRangeAlertActive = true;
            }
            else if (
                _idRangeAlertActive
                && rangeUsedPercent < thresholds.CapacityPercentWarning - HysteresisPercent
            )
            {
                if (_activeIdRangeAlert != null)
                {
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Info,
                            CheckName = Name,
                            Title =
                                "VM ID range recovered: "
                                + rangeUsedPercent.ToString("F0")
                                + "% used",
                            Message =
                                "ID range usage dropped below threshold. Previous alert: "
                                + _activeIdRangeAlert.Id,
                            Id = _activeIdRangeAlert.Id + "-resolved",
                        }
                    );
                }
                _idRangeAlertActive = false;
                _activeIdRangeAlert = null;
            }
        }

        return alerts;
    }
}
