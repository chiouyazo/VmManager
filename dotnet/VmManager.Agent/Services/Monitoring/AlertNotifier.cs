using System.Text;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring;

public sealed class AlertNotifier
{
    private readonly EmailService _emailService;
    private readonly SettingsService _settingsService;
    private readonly MetricsCache _metricsCache;
    private readonly ILogger<AlertNotifier> _logger;
    private readonly Dictionary<string, SentAlertInfo> _sentAlerts =
        new Dictionary<string, SentAlertInfo>();

    public AlertNotifier(
        EmailService emailService,
        SettingsService settingsService,
        MetricsCache metricsCache,
        ILogger<AlertNotifier> logger
    )
    {
        ArgumentNullException.ThrowIfNull(emailService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(metricsCache);
        ArgumentNullException.ThrowIfNull(logger);
        _emailService = emailService;
        _settingsService = settingsService;
        _metricsCache = metricsCache;
        _logger = logger;
    }

    public async Task NotifyAsync(MonitoringAlert alert, bool isResolved = false)
    {
        if (!_emailService.IsConfigured)
            return;

        AppSettings settings = _settingsService.Load();
        MonitoringSettings? monitoring = settings.Monitoring;
        if (monitoring == null)
            return;

        MonitoringNotificationEntry? entry = GetNotificationEntry(monitoring, alert.CheckName);
        if (entry == null || !entry.Enabled)
            return;

        string toAddress = !string.IsNullOrEmpty(entry.Email)
            ? entry.Email
            : monitoring.DefaultNotificationEmail;

        if (string.IsNullOrEmpty(toAddress))
            return;

        string hostname = Environment.MachineName;

        if (isResolved)
        {
            // Find the original alert info to reply to it
            string originalId = alert.Id.Replace("-resolved", "");
            _sentAlerts.TryGetValue(originalId, out SentAlertInfo? original);

            string subject =
                original?.Subject ?? ("[VmManager " + alert.Severity + "] " + alert.Title);
            string originalMessageId = "<vmmanager-alert-" + originalId + "@" + hostname + ">";
            string resolvedBody = BuildResolvedEmail(alert, original?.Body);

            try
            {
                await _emailService.SendAsync(
                    toAddress,
                    subject,
                    resolvedBody,
                    messageId: "<vmmanager-alert-" + alert.Id + "@" + hostname + ">",
                    inReplyTo: originalMessageId
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send resolution notification for {CheckName}",
                    alert.CheckName
                );
            }

            _sentAlerts.Remove(originalId);
        }
        else
        {
            string subject = "[VmManager " + alert.Severity + "] " + alert.Title;
            string messageId = "<vmmanager-alert-" + alert.Id + "@" + hostname + ">";
            string body = BuildAlertEmail(alert);

            _sentAlerts[alert.Id] = new SentAlertInfo
            {
                Subject = subject,
                Body = body,
                MessageId = messageId,
            };

            try
            {
                await _emailService.SendAsync(toAddress, subject, body, messageId: messageId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send alert notification for {CheckName}",
                    alert.CheckName
                );
            }
        }
    }

    private string BuildAlertEmail(MonitoringAlert alert)
    {
        string severityLabel = alert.Severity switch
        {
            AlertSeverity.Info => "INFO",
            AlertSeverity.Warning => "WARNING",
            AlertSeverity.Critical => "CRITICAL",
            AlertSeverity.Fatal => "FATAL",
            _ => "ALERT",
        };

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(severityLabel + ": " + alert.Title);
        sb.AppendLine();
        sb.AppendLine(alert.Message);
        sb.AppendLine();

        if (alert.VmName != null)
            sb.AppendLine("VM: " + alert.VmName);
        if (alert.SourceIp != null)
            sb.AppendLine("Source: " + alert.SourceIp);

        sb.AppendLine("Time: " + alert.Timestamp.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        sb.AppendLine("Check: " + alert.CheckName);
        sb.AppendLine("Alert ID: " + alert.Id);
        sb.AppendLine();
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine("System Status");
        sb.AppendLine("------------------------------------------------------------");
        sb.Append(BuildSystemContext());

        return "<pre style='font-family: monospace; white-space: pre-wrap;'>"
            + System.Net.WebUtility.HtmlEncode(sb.ToString())
            + "</pre>";
    }

    private string BuildResolvedEmail(MonitoringAlert alert, string? originalBody)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("RESOLVED: " + alert.Title);
        sb.AppendLine();
        sb.AppendLine("This condition has returned to normal.");
        sb.AppendLine();

        if (alert.VmName != null)
            sb.AppendLine("VM: " + alert.VmName);

        sb.AppendLine("Resolved at: " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        sb.AppendLine();
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine("Current System Status");
        sb.AppendLine("------------------------------------------------------------");
        sb.Append(BuildSystemContext());

        string resolvedHtml =
            "<pre style='font-family: monospace; white-space: pre-wrap;'>"
            + System.Net.WebUtility.HtmlEncode(sb.ToString())
            + "</pre>";

        if (!string.IsNullOrEmpty(originalBody))
        {
            resolvedHtml +=
                "<br><br>"
                + "<div style='border-left: 3px solid #ccc; padding-left: 12px; color: #666;'>"
                + "<p style='font-size: 12px; margin-bottom: 4px;'>Original alert:</p>"
                + originalBody
                + "</div>";
        }

        return resolvedHtml;
    }

    private string BuildSystemContext()
    {
        StringBuilder sb = new StringBuilder();

        HostMetrics host = _metricsCache.GetHostMetrics();
        sb.AppendLine();
        sb.AppendLine("HOST");
        sb.AppendLine("  CPU:      " + host.CpuPercent.ToString("F1") + "%");

        if (host.MemoryTotalBytes > 0)
        {
            double memPercent = (double)host.MemoryUsedBytes / host.MemoryTotalBytes * 100;
            sb.AppendLine(
                "  Memory:   "
                    + (host.MemoryUsedBytes / (1024 * 1024 * 1024.0)).ToString("F1")
                    + " GB / "
                    + (host.MemoryTotalBytes / (1024 * 1024 * 1024.0)).ToString("F1")
                    + " GB ("
                    + memPercent.ToString("F0")
                    + "%)"
            );
        }

        if (host.UptimeSeconds > 0)
        {
            TimeSpan uptime = TimeSpan.FromSeconds(host.UptimeSeconds);
            sb.AppendLine(
                "  Uptime:   "
                    + (int)uptime.TotalDays
                    + "d "
                    + uptime.Hours
                    + "h "
                    + uptime.Minutes
                    + "m"
            );
        }

        List<StorageMetrics> storages = _metricsCache.GetStorageMetrics();
        if (storages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("STORAGE");
            foreach (StorageMetrics storage in storages)
            {
                double freePercent = 100 - storage.UsedPercent;
                sb.AppendLine(
                    "  "
                        + storage.Name
                        + ": "
                        + (storage.UsedBytes / (1024 * 1024 * 1024.0)).ToString("F0")
                        + " GB / "
                        + (storage.TotalBytes / (1024 * 1024 * 1024.0)).ToString("F0")
                        + " GB ("
                        + freePercent.ToString("F0")
                        + "% free)"
                );
            }
        }

        List<VmMetrics> vms = _metricsCache.GetVmMetrics();
        List<VmMetrics> runningVms = vms.Where(v => v.State == "running").ToList();
        if (vms.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("VMs (" + runningVms.Count + " running / " + vms.Count + " total)");
            foreach (VmMetrics vm in runningVms)
            {
                sb.AppendLine(
                    "  "
                        + vm.Name.PadRight(20)
                        + " CPU "
                        + vm.CpuPercent.ToString("F1").PadLeft(5)
                        + "%"
                        + "  Mem "
                        + (vm.MemoryUsedBytes / (1024 * 1024 * 1024.0)).ToString("F1").PadLeft(5)
                        + " GB"
                );
            }
            foreach (VmMetrics vm in vms.Where(v => v.State != "running"))
            {
                sb.AppendLine("  " + vm.Name.PadRight(20) + " (" + vm.State + ")");
            }
        }

        return sb.ToString();
    }

    private static MonitoringNotificationEntry? GetNotificationEntry(
        MonitoringSettings settings,
        string checkName
    )
    {
        return checkName switch
        {
            "VmState" => settings.VmCrash,
            "VmStuckState" => settings.VmStuckState,
            "RdpPort" => settings.RdpUnreachable,
            "WinRmPort" => settings.WinRmUnreachable,
            "VmUptime" => settings.VmUptimeExceeded,
            "SnapshotDepth" => settings.SnapshotChainDeep,
            "FailedLogin" => settings.FailedLogin,
            "BruteForce" => settings.BruteForceDetected,
            "HostCpu" => settings.HostCpuHigh,
            "HostMemory" => settings.HostMemoryHigh,
            "Storage" => settings.StorageLow,
            "DiskHealth" => settings.DiskSmartWarning,
            "AgentHealth" => settings.AgentUnhealthy,
            "CertificateExpiry" => settings.CertificateExpiring,
            "Quota" => settings.QuotaApproaching,
            "Capacity" => settings.CapacityNearLimit,
            _ => null,
        };
    }

    private static readonly HashSet<string> ResolvableChecks = new HashSet<string>
    {
        "HostCpu",
        "HostMemory",
        "Storage",
        "RdpPort",
        "WinRmPort",
        "AgentHealth",
        "Capacity",
    };

    public static bool IsResolvable(string checkName)
    {
        return ResolvableChecks.Contains(checkName);
    }

    private sealed class SentAlertInfo
    {
        public string Subject { get; init; } = "";
        public string Body { get; init; } = "";
        public string MessageId { get; init; } = "";
    }
}
