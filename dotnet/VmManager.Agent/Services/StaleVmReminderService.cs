using VmManager.Catalog.Shared;

namespace VmManager.Agent.Services;

public class StaleVmReminderService : BackgroundService
{
    private readonly VmTrackingService _trackingService;
    private readonly VmOwnershipService _ownershipService;
    private readonly UserService _userService;
    private readonly EmailService _emailService;
    private readonly SettingsService _settingsService;
    private readonly ILogger<StaleVmReminderService> _logger;

    public StaleVmReminderService(
        VmTrackingService trackingService,
        VmOwnershipService ownershipService,
        UserService userService,
        EmailService emailService,
        SettingsService settingsService,
        ILogger<StaleVmReminderService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(trackingService);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(emailService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);
        _trackingService = trackingService;
        _ownershipService = ownershipService;
        _userService = userService;
        _emailService = emailService;
        _settingsService = settingsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            await SendRemindersAsync();
        }
    }

    private async Task SendRemindersAsync()
    {
        try
        {
            AppSettings settings = _settingsService.Load();
            if (settings.StaleVmReminderDays <= 0 || !_emailService.IsConfigured)
                return;

            List<(string Name, DateTime CreatedAt, string Owner)> staleVms =
                _trackingService.GetVmsOlderThan(settings.StaleVmReminderDays, _ownershipService);

            Dictionary<string, List<(string Name, DateTime CreatedAt)>> byOwner =
                new Dictionary<string, List<(string, DateTime)>>();

            foreach ((string name, DateTime createdAt, string owner) in staleVms)
            {
                if (!byOwner.ContainsKey(owner))
                    byOwner[owner] = new List<(string, DateTime)>();
                byOwner[owner].Add((name, createdAt));
            }

            foreach (KeyValuePair<string, List<(string Name, DateTime CreatedAt)>> kvp in byOwner)
            {
                string email = GetUserEmail(kvp.Key);
                if (string.IsNullOrWhiteSpace(email))
                    continue;

                string vmList = string.Join(
                    "",
                    kvp.Value.Select(v =>
                        $"<li><b>{v.Name}</b> (created {v.CreatedAt:yyyy-MM-dd}, {(int)(DateTime.UtcNow - v.CreatedAt).TotalDays} days ago)</li>"
                    )
                );

                string body =
                    $@"
<h2>Stale VM Reminder</h2>
<p>The following VMs have been around for more than {settings.StaleVmReminderDays} days. If you no longer need them, please delete them to free up resources.</p>
<ul>{vmList}</ul>
<p>If you still need these VMs, no action is required. You will receive this reminder periodically.</p>";

                await _emailService.SendAsync(email, "Stale VM Reminder", body);
                _logger.LogInformation(
                    "Sent stale VM reminder to {Email} for {Count} VMs",
                    email,
                    kvp.Value.Count
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send stale VM reminders");
        }
    }

    private string GetUserEmail(string username)
    {
        UserAccount? user = _userService.GetByUsername(username);
        if (user == null)
            return "";

        if (user.IsAdmin)
            return user.Email;

        return EmailValidator.IsValid(user.Username) ? user.Username : "";
    }
}
