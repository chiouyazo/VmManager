namespace VmManager.Contracts.Models;

public class MonitoringSettings
{
    public bool Enabled { get; set; }
    public string DefaultNotificationEmail { get; set; } = "";

    public int VmStateIntervalSeconds { get; set; } = 30;
    public int MetricsIntervalSeconds { get; set; } = 60;
    public int PortCheckIntervalSeconds { get; set; } = 60;
    public int HostHealthIntervalSeconds { get; set; } = 300;
    public int DiskHealthIntervalSeconds { get; set; } = 3600;
    public int CapacityIntervalSeconds { get; set; } = 900;
    public int AgentHealthIntervalSeconds { get; set; } = 300;

    public int AlertRetentionDays { get; set; } = 30;
    public int MaxAlertCount { get; set; } = 10000;

    public MonitoringNotificationEntry VmCrash { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry VmStuckState { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry RdpUnreachable { get; set; } =
        new MonitoringNotificationEntry();
    public MonitoringNotificationEntry WinRmUnreachable { get; set; } =
        new MonitoringNotificationEntry();
    public MonitoringNotificationEntry VmUptimeExceeded { get; set; } =
        new MonitoringNotificationEntry();
    public MonitoringNotificationEntry SnapshotChainDeep { get; set; } =
        new MonitoringNotificationEntry();
    public MonitoringNotificationEntry FailedLogin { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry BruteForceDetected { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry HostCpuHigh { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry HostMemoryHigh { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry StorageLow { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry DiskSmartWarning { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry AgentUnhealthy { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry CertificateExpiring { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };
    public MonitoringNotificationEntry QuotaApproaching { get; set; } =
        new MonitoringNotificationEntry();
    public MonitoringNotificationEntry CapacityNearLimit { get; set; } =
        new MonitoringNotificationEntry { Enabled = true };

    public MonitoringThresholds Thresholds { get; set; } = new MonitoringThresholds();
}
