using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring;

public interface IMonitoringCheck
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken);
}
