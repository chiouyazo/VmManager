using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Monitoring.Checks;

public sealed class LoginMonitorCheck : IMonitoringCheck
{
    private readonly LoginAttemptTracker _tracker;
    private readonly UserService _userService;
    private readonly SettingsService _settingsService;

    public string Name => "FailedLogin";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public LoginMonitorCheck(
        LoginAttemptTracker tracker,
        UserService userService,
        SettingsService settingsService
    )
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(settingsService);
        _tracker = tracker;
        _userService = userService;
        _settingsService = settingsService;
    }

    public Task<List<MonitoringAlert>> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<MonitoringAlert> alerts = new List<MonitoringAlert>();
        MonitoringThresholds thresholds =
            _settingsService.Load().Monitoring?.Thresholds ?? new MonitoringThresholds();

        foreach (UserAccount user in _userService.GetAll())
        {
            TimeSpan window = TimeSpan.FromMinutes(thresholds.FailedLoginWindowMinutes);
            int count = _tracker.GetFailedAttemptCount(user.Username, window);

            if (count >= thresholds.BruteForceThreshold)
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Critical,
                        CheckName = "BruteForce",
                        Title = "Brute force detected for " + user.Username,
                        Message =
                            count
                            + " failed login attempts in "
                            + thresholds.BruteForceWindowMinutes
                            + " minutes.",
                    }
                );
            }
            else if (count >= thresholds.FailedLoginThreshold)
            {
                alerts.Add(
                    new MonitoringAlert
                    {
                        Severity = AlertSeverity.Warning,
                        CheckName = Name,
                        Title = "Multiple failed logins for " + user.Username,
                        Message =
                            count
                            + " failed login attempts in "
                            + thresholds.FailedLoginWindowMinutes
                            + " minutes.",
                    }
                );
            }
        }

        _tracker.Cleanup(TimeSpan.FromHours(1));
        return Task.FromResult(alerts);
    }
}
