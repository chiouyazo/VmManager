using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class VmUptimeMonitorCheck : IMonitoringCheck
{
    private readonly IVmBackend _backend;
    private readonly SettingsService _settingsService;
    private readonly HashSet<string> _notified = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    );

    public string Name => "VmUptime";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.CapacityIntervalSeconds ?? 900);

    public VmUptimeMonitorCheck(IVmBackend backend, SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(settingsService);
        _backend = backend;
        _settingsService = settingsService;
    }

    public async Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        MonitoringThresholds thresholds =
            _settingsService.Load().Monitoring?.Thresholds ?? new MonitoringThresholds();

        List<VmInstance> vms = await _backend.GetVmsAsync();
        foreach (VmInstance vm in vms.Where(v => v.State == "Running"))
        {
            double uptimeDays = vm.Uptime.TotalDays;
            if (uptimeDays >= thresholds.VmUptimeDaysWarning && !_notified.Contains(vm.Name))
            {
                _notified.Add(vm.Name);
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = Name,
                        Title = vm.Name + " running for " + (int)uptimeDays + " days",
                        Message =
                            "VM uptime exceeded "
                            + thresholds.VmUptimeDaysWarning
                            + " day threshold.",
                        VmName = vm.Name,
                    }
                );
            }
        }

        return alerts;
    }
}
