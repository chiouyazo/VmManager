using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class SnapshotDepthMonitorCheck : IMonitoringCheck
{
    private readonly IVmBackend _backend;
    private readonly SettingsService _settingsService;
    private readonly HashSet<string> _notified = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    );

    public string Name => "SnapshotDepth";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.CapacityIntervalSeconds ?? 900);

    public SnapshotDepthMonitorCheck(IVmBackend backend, SettingsService settingsService)
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
        foreach (VmInstance vm in vms.Where(v => v.IsManaged))
        {
            try
            {
                List<VmSnapshot> snapshots = await _backend.GetSnapshotsAsync(vm.Name);
                if (
                    snapshots.Count >= thresholds.SnapshotChainDepthWarning
                    && !_notified.Contains(vm.Name)
                )
                {
                    _notified.Add(vm.Name);
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Warning,
                            CheckName = Name,
                            Title = vm.Name + " has " + snapshots.Count + " snapshots",
                            Message =
                                "Snapshot chain depth exceeded "
                                + thresholds.SnapshotChainDepthWarning
                                + " threshold. Performance may degrade.",
                            VmName = vm.Name,
                        }
                    );
                }
            }
            catch
            {
                // Skip VMs where snapshots can't be queried
            }
        }

        return alerts;
    }
}
