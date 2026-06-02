using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring;

public sealed class MetricsCache
{
    private readonly object _lock = new object();
    private HostMetrics _hostMetrics = new HostMetrics();
    private List<VmMetrics> _vmMetrics = new List<VmMetrics>();
    private List<StorageMetrics> _storageMetrics = new List<StorageMetrics>();
    private List<DiskHealthInfo> _diskHealth = new List<DiskHealthInfo>();

    public HostMetrics GetHostMetrics()
    {
        lock (_lock)
            return _hostMetrics;
    }

    public List<VmMetrics> GetVmMetrics()
    {
        lock (_lock)
            return _vmMetrics.ToList();
    }

    public VmMetrics? GetVmMetrics(string vmName)
    {
        lock (_lock)
            return _vmMetrics.FirstOrDefault(v =>
                string.Equals(v.Name, vmName, StringComparison.OrdinalIgnoreCase)
            );
    }

    public List<StorageMetrics> GetStorageMetrics()
    {
        lock (_lock)
            return _storageMetrics.ToList();
    }

    public List<DiskHealthInfo> GetDiskHealth()
    {
        lock (_lock)
            return _diskHealth.ToList();
    }

    public void UpdateHostMetrics(HostMetrics metrics)
    {
        lock (_lock)
            _hostMetrics = metrics;
    }

    public void UpdateVmMetrics(List<VmMetrics> metrics)
    {
        lock (_lock)
            _vmMetrics = metrics;
    }

    public void UpdateStorageMetrics(List<StorageMetrics> metrics)
    {
        lock (_lock)
            _storageMetrics = metrics;
    }

    public void UpdateDiskHealth(List<DiskHealthInfo> health)
    {
        lock (_lock)
            _diskHealth = health;
    }
}
