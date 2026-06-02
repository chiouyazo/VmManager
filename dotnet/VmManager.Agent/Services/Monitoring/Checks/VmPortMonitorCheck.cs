using System.Collections.Concurrent;
using System.Net.Sockets;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class VmPortMonitorCheck : IMonitoringCheck
{
    private static readonly TimeSpan BootGracePeriod = TimeSpan.FromMinutes(5);

    private readonly IVmBackend _backend;
    private readonly IVmIpResolver _ipResolver;
    private readonly SettingsService _settingsService;
    private readonly HashSet<string> _activeAlerts = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, DateTimeOffset> _firstSeenRunning =
        new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

    public string Name => "RdpPort";
    public TimeSpan Interval =>
        TimeSpan.FromSeconds(_settingsService.Load().Monitoring?.PortCheckIntervalSeconds ?? 60);

    public VmPortMonitorCheck(
        IVmBackend backend,
        IVmIpResolver ipResolver,
        SettingsService settingsService
    )
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(ipResolver);
        ArgumentNullException.ThrowIfNull(settingsService);
        _backend = backend;
        _ipResolver = ipResolver;
        _settingsService = settingsService;
    }

    public async Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        List<VmInstance> vms = await _backend.GetVmsAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Track when VMs first appear as running (for boot grace period)
        HashSet<string> currentRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VmInstance vm in vms.Where(v => v.State == "Running"))
        {
            currentRunning.Add(vm.Name);
            _firstSeenRunning.TryAdd(vm.Name, now);
        }

        // Clean up VMs that are no longer running
        foreach (string name in _firstSeenRunning.Keys.ToList())
        {
            if (!currentRunning.Contains(name))
            {
                _firstSeenRunning.TryRemove(name, out _);
                _activeAlerts.Remove("rdp-" + name);
                _activeAlerts.Remove("winrm-" + name);
                _activeAlerts.Remove("noip-" + name);
            }
        }

        foreach (VmInstance vm in vms.Where(v => v.State == "Running" && v.IsManaged))
        {
            // Skip VMs still in boot grace period
            DateTimeOffset firstSeen = _firstSeenRunning.GetValueOrDefault(vm.Name, now);
            if (now - firstSeen < BootGracePeriod)
                continue;

            string? ip = await _ipResolver.ResolveIpAsync(vm.Name, cancellationToken);

            // No IP after grace period
            string noIpKey = "noip-" + vm.Name;
            if (string.IsNullOrEmpty(ip))
            {
                if (!_activeAlerts.Contains(noIpKey))
                {
                    _activeAlerts.Add(noIpKey);
                    alerts.Add(
                        new MonitoringAlert
                        {
                            Severity = AlertSeverity.Warning,
                            CheckName = "RdpPort",
                            Title = vm.Name + " has no IP address",
                            Message =
                                "The VM has been running for "
                                + (int)(now - firstSeen).TotalMinutes
                                + " minutes but no IP address could be resolved. "
                                + "Check network adapter configuration and DHCP.",
                            VmName = vm.Name,
                        }
                    );
                }
                continue;
            }

            // IP recovered
            if (_activeAlerts.Remove(noIpKey))
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = "RdpPort",
                        Title = vm.Name + " IP address assigned: " + ip,
                        Message = "The VM now has an IP address.",
                        VmName = vm.Name,
                    }
                );
            }

            // RDP port check
            string rdpKey = "rdp-" + vm.Name;
            bool rdpReachable = await IsPortReachableAsync(ip, 3389, cancellationToken);
            if (!rdpReachable && !_activeAlerts.Contains(rdpKey))
            {
                _activeAlerts.Add(rdpKey);
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Warning,
                        CheckName = "RdpPort",
                        Title = vm.Name + " RDP port unreachable",
                        Message =
                            "Port 3389 is not responding on "
                            + ip
                            + ". "
                            + "The VM has been running for "
                            + (int)(now - firstSeen).TotalMinutes
                            + " minutes.",
                        VmName = vm.Name,
                    }
                );
            }
            else if (rdpReachable && _activeAlerts.Remove(rdpKey))
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = "RdpPort",
                        Title = vm.Name + " RDP port recovered",
                        Message = "Port 3389 is now reachable on " + ip + ".",
                        VmName = vm.Name,
                        Id = rdpKey + "-resolved",
                    }
                );
            }

            // WinRM port check
            string winrmKey = "winrm-" + vm.Name;
            bool winrmReachable = await IsPortReachableAsync(ip, 5985, cancellationToken);
            if (!winrmReachable && !_activeAlerts.Contains(winrmKey))
            {
                _activeAlerts.Add(winrmKey);
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = "WinRmPort",
                        Title = vm.Name + " WinRM port unreachable",
                        Message = "Port 5985 is not responding on " + ip + ".",
                        VmName = vm.Name,
                    }
                );
            }
            else if (winrmReachable && _activeAlerts.Remove(winrmKey))
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Info,
                        CheckName = "WinRmPort",
                        Title = vm.Name + " WinRM port recovered",
                        Message = "Port 5985 is now reachable on " + ip + ".",
                        VmName = vm.Name,
                        Id = winrmKey + "-resolved",
                    }
                );
            }
        }

        return alerts;
    }

    private static async Task<bool> IsPortReachableAsync(
        string host,
        int port,
        CancellationToken ct
    )
    {
        try
        {
            using TcpClient client = new TcpClient();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                ct
            );
            timeout.CancelAfter(3000);
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
