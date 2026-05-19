using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

public class QuotaService
{
    private readonly UserService _userService;
    private readonly VmOwnershipService _ownershipService;
    private readonly SettingsService _settingsService;
    private readonly EmailService _emailService;
    private readonly IVmBackend _backend;
    private readonly ILogger<QuotaService> _logger;

    public QuotaService(
        UserService userService,
        VmOwnershipService ownershipService,
        SettingsService settingsService,
        EmailService emailService,
        IVmBackend backend,
        ILogger<QuotaService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(emailService);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);
        _userService = userService;
        _ownershipService = ownershipService;
        _settingsService = settingsService;
        _emailService = emailService;
        _backend = backend;
        _logger = logger;
    }

    public async Task<QuotaCheckResult> CheckCanCreateVmAsync(string username)
    {
        AppSettings settings = _settingsService.Load();
        UserAccount? user = _userService.GetByUsername(username);

        int owned = _ownershipService.GetVmsOwnedBy(username).Count;
        int effectiveLimit = GetEffectiveLimit(user, settings);

        if (effectiveLimit > 0 && owned >= effectiveLimit)
        {
            return new QuotaCheckResult
            {
                Allowed = false,
                Reason =
                    $"VM limit reached ({owned}/{effectiveLimit}). Delete a VM or contact an admin to increase your quota.",
            };
        }

        if (settings.MaxTotalVms > 0)
        {
            List<VmInstance> allVms = await _backend.GetVmsAsync();
            if (allVms.Count >= settings.MaxTotalVms)
            {
                return new QuotaCheckResult
                {
                    Allowed = false,
                    Reason =
                        $"Global VM limit reached ({allVms.Count}/{settings.MaxTotalVms}). No more VMs can be created.",
                };
            }
        }

        return new QuotaCheckResult { Allowed = true };
    }

    public async Task<QuotaUsage> GetUsageAsync(string username)
    {
        AppSettings settings = _settingsService.Load();
        UserAccount? user = _userService.GetByUsername(username);

        int owned = _ownershipService.GetVmsOwnedBy(username).Count;
        int effectiveLimit = GetEffectiveLimit(user, settings);

        List<VmInstance> allVms = await _backend.GetVmsAsync();

        return new QuotaUsage
        {
            VmsOwned = owned,
            MaxVms = effectiveLimit,
            GlobalVmCount = allVms.Count,
            GlobalMaxVms = settings.MaxTotalVms,
        };
    }

    public async Task NotifyQuotaChangedAsync(string username, int oldMax, int newMax)
    {
        if (!_emailService.IsConfigured)
            return;

        string? email = GetUserEmail(username);
        if (email == null)
            return;

        int owned = _ownershipService.GetVmsOwnedBy(username).Count;
        string overQuotaNote =
            owned > newMax && newMax > 0
                ? $"<p>You currently have {owned} VMs. Your existing VMs will not be affected, but you cannot create new ones until you are under the limit.</p>"
                : "";

        string body =
            $@"
<h2>VM Quota Updated</h2>
<p>Your VM quota has been changed:</p>
<ul>
    <li>Previous limit: {FormatLimit(oldMax)}</li>
    <li>New limit: {FormatLimit(newMax)}</li>
    <li>Current usage: {owned} VMs</li>
</ul>
{overQuotaNote}";

        await _emailService.SendAsync(email, "VM Quota Updated", body);
    }

    public async Task CheckAndNotifyApproachingLimitAsync(string username)
    {
        if (!_emailService.IsConfigured)
            return;

        AppSettings settings = _settingsService.Load();
        UserAccount? user = _userService.GetByUsername(username);
        int owned = _ownershipService.GetVmsOwnedBy(username).Count;
        int effectiveLimit = GetEffectiveLimit(user, settings);

        if (effectiveLimit <= 0)
            return;

        double usage = (double)owned / effectiveLimit;
        if (usage < 0.8)
            return;

        string? email = GetUserEmail(username);
        if (email == null)
            return;

        string body =
            $@"
<h2>VM Quota Notice</h2>
<p>You are using {owned} of your {effectiveLimit} VM slots.</p>";

        await _emailService.SendAsync(
            email,
            $"VM Quota: {owned}/{effectiveLimit} slots used",
            body
        );
    }

    private int GetEffectiveLimit(UserAccount? user, AppSettings settings)
    {
        if (user == null)
            return settings.DefaultUserMaxVms;

        if (user.IsAdmin && user.MaxVms == 0)
            return -1;

        if (user.MaxVms == -1)
            return -1;

        if (user.MaxVms > 0)
            return user.MaxVms;

        return settings.DefaultUserMaxVms;
    }

    private string? GetUserEmail(string username)
    {
        UserAccount? user = _userService.GetByUsername(username);
        if (user == null)
            return null;

        if (user.IsAdmin)
            return string.IsNullOrWhiteSpace(user.Email) ? null : user.Email;

        return EmailValidator.IsValid(user.Username) ? user.Username : null;
    }

    private static string FormatLimit(int limit)
    {
        return limit switch
        {
            -1 => "Unlimited",
            0 => "Default",
            _ => limit.ToString(),
        };
    }
}
