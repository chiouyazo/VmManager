using System.Diagnostics;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class AgentHealthMonitorCheck : IMonitoringCheck
{
    private readonly IVmBackend _backend;
    private readonly SettingsService _settingsService;
    private readonly ILogger<AgentHealthMonitorCheck> _logger;

    public string Name => "AgentHealth";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.AgentHealthIntervalSeconds ?? 300);

    public AgentHealthMonitorCheck(
        IVmBackend backend,
        SettingsService settingsService,
        ILogger<AgentHealthMonitorCheck> logger
    )
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);
        _backend = backend;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();

        // Check agent memory usage
        Process process = Process.GetCurrentProcess();
        long memoryMb = process.WorkingSet64 / (1024 * 1024);
        if (memoryMb > 1024)
        {
            alerts.Add(
                new MonitoringAlert
                {
                    Severity = AlertSeverity.Warning,
                    CheckName = Name,
                    Title = "Agent memory usage high: " + memoryMb + " MB",
                    Message = "The VmManager agent is using more than 1 GB of memory.",
                }
            );
        }

        // Check hypervisor API reachability
        try
        {
            await _backend.GetVmsAsync();
        }
        catch (Exception ex)
        {
            alerts.Add(
                new MonitoringAlert
                {
                    Severity = AlertSeverity.Critical,
                    CheckName = Name,
                    Title = "Hypervisor API unreachable",
                    Message = "Cannot communicate with the hypervisor backend: " + ex.Message,
                }
            );
        }

        return alerts;
    }
}
