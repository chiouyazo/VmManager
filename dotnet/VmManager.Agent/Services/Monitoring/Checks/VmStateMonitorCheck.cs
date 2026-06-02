using System.Collections.Concurrent;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class VmStateMonitorCheck : IMonitoringCheck
{
    private readonly IVmBackend _backend;
    private readonly IMetricsProvider _metricsProvider;
    private readonly VmStopTracker _stopTracker;
    private readonly SettingsService _settingsService;
    private readonly ILogger<VmStateMonitorCheck> _logger;
    private readonly ConcurrentDictionary<string, string> _previousStates =
        new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeAlerts = new HashSet<string>();

    public string Name => "VmState";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.VmStateIntervalSeconds ?? 30);

    public VmStateMonitorCheck(
        IVmBackend backend,
        IMetricsProvider metricsProvider,
        VmStopTracker stopTracker,
        SettingsService settingsService,
        ILogger<VmStateMonitorCheck> logger
    )
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(metricsProvider);
        ArgumentNullException.ThrowIfNull(stopTracker);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);
        _backend = backend;
        _metricsProvider = metricsProvider;
        _stopTracker = stopTracker;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        List<VmInstance> vms = await _backend.GetVmsAsync();
        MonitoringThresholds thresholds =
            _settingsService.Load().Monitoring?.Thresholds ?? new MonitoringThresholds();

        foreach (VmInstance vm in vms)
        {
            string previousState = _previousStates.GetValueOrDefault(vm.Name, "");
            _previousStates[vm.Name] = vm.State;

            if (string.IsNullOrEmpty(previousState))
                continue;

            // Crash detection: was Running, now Off
            if (previousState == "Running" && vm.State == "Off")
            {
                if (_stopTracker.WasRecentlyStoppedByManager(vm.Name, TimeSpan.FromMinutes(2)))
                    continue;

                VmShutdownReason reason = await _metricsProvider.GetShutdownReasonAsync(
                    vm.Name,
                    cancellationToken
                );

                if (reason == VmShutdownReason.HostInitiated)
                {
                    // Stopped via hypervisor UI/API (not VmManager, but intentional). No alert.
                    _logger.LogDebug(
                        "{VmName} stopped via hypervisor (host-initiated), no alert",
                        vm.Name
                    );
                    continue;
                }

                if (reason == VmShutdownReason.GuestInitiated)
                {
                    // User shut down from within Windows. Info only, not a problem.
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Info,
                            CheckName = Name,
                            Title = vm.Name + " shut down from guest OS",
                            Message =
                                "The VM was shut down from within the guest operating system.",
                            VmName = vm.Name,
                        }
                    );
                }
                else if (reason == VmShutdownReason.Crashed)
                {
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Critical,
                            CheckName = Name,
                            Title = vm.Name + " crashed",
                            Message =
                                "The VM stopped unexpectedly. This was not initiated by VmManager or the guest OS.",
                            VmName = vm.Name,
                        }
                    );
                }
                else
                {
                    // Unknown reason - could be crash, could be external tool
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Warning,
                            CheckName = Name,
                            Title = vm.Name + " stopped unexpectedly",
                            Message = "The VM stopped but the reason could not be determined.",
                            VmName = vm.Name,
                        }
                    );
                }
            }

            // Stuck in Starting
            string stuckKey = "stuck-" + vm.Name;
            if (vm.State == "Starting" || vm.State == "Stopping")
            {
                if (!_activeAlerts.Contains(stuckKey))
                {
                    int threshold =
                        vm.State == "Starting"
                            ? thresholds.VmStuckStartingMinutes
                            : thresholds.VmStuckStoppingMinutes;

                    _activeAlerts.Add(stuckKey);
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Warning,
                            CheckName = "VmStuckState",
                            Title = vm.Name + " stuck in " + vm.State,
                            Message =
                                "The VM has been in "
                                + vm.State
                                + " state. Threshold: "
                                + threshold
                                + " minutes.",
                            VmName = vm.Name,
                        }
                    );
                }
            }
            else
            {
                _activeAlerts.Remove(stuckKey);
            }
        }

        _stopTracker.Cleanup();
        return alerts;
    }
}
