namespace VmManager.Contracts.Models;

public class MonitoringThresholds
{
    public int VmStuckStartingMinutes { get; set; } = 10;
    public int VmStuckStoppingMinutes { get; set; } = 5;
    public int VmUptimeDaysWarning { get; set; } = 30;
    public int SnapshotChainDepthWarning { get; set; } = 5;
    public int FailedLoginThreshold { get; set; } = 5;
    public int FailedLoginWindowMinutes { get; set; } = 10;
    public int BruteForceThreshold { get; set; } = 20;
    public int BruteForceWindowMinutes { get; set; } = 30;
    public double HostCpuPercentWarning { get; set; } = 85;
    public double HostCpuPercentCritical { get; set; } = 95;
    public double HostMemoryPercentWarning { get; set; } = 85;
    public double HostMemoryPercentCritical { get; set; } = 95;
    public double StorageFreePercentWarning { get; set; } = 20;
    public double StorageFreePercentCritical { get; set; } = 10;
    public int CertificateExpiryDaysWarning { get; set; } = 30;
    public double CapacityPercentWarning { get; set; } = 80;
}
