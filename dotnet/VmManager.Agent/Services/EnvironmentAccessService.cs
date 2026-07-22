using System.Security.Cryptography;
using VmManager.Catalog.Shared;

namespace VmManager.Agent.Services;

public class EnvironmentAccessService
{
    private readonly UserService _users;
    private readonly VmSharingService _sharing;
    private readonly VmOwnershipService _ownership;
    private readonly EmailService _email;
    private readonly ILogger<EnvironmentAccessService> _logger;

    public EnvironmentAccessService(
        UserService users,
        VmSharingService sharing,
        VmOwnershipService ownership,
        EmailService email,
        ILogger<EnvironmentAccessService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(sharing);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(logger);
        _users = users;
        _sharing = sharing;
        _ownership = ownership;
        _email = email;
        _logger = logger;
    }

    public async Task GrantAccessAsync(
        string vmName,
        string owner,
        IEnumerable<string> accessEmails
    )
    {
        if (!string.IsNullOrWhiteSpace(owner))
            _ownership.SetOwner(vmName, owner);

        foreach (string raw in accessEmails ?? [])
        {
            string email = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(email) || !EmailValidator.IsValid(email))
            {
                _logger.LogWarning("Skipping invalid access email {Email}", raw);
                continue;
            }

            if (string.Equals(email, owner, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                await EnsureUserAsync(email);
                _sharing.ShareVm(vmName, owner, email, [Permission.RdpConnect]);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to grant access to {Email} for {VmName}",
                    email,
                    vmName
                );
            }
        }
    }

    public void RevokeAccess(string vmName)
    {
        _sharing.RemoveAllSharesForVm(vmName);
    }

    private async Task EnsureUserAsync(string email)
    {
        if (_users.GetByUsername(email) != null)
            return;

        string tempPassword = GeneratePassword();
        _users.CreateUser(email, tempPassword, [.. Permission.DefaultUser], isAdmin: false);
        _logger.LogInformation("Auto-created VmManager user {Email} for environment access", email);

        await SendAccountCreatedMailAsync(email, tempPassword);
    }

    private async Task SendAccountCreatedMailAsync(string email, string tempPassword)
    {
        string body =
            $@"<h2>VmManager access</h2>
<p>An account was created for you so you can connect to a test environment.</p>
<ul>
  <li><b>Username:</b> {email}</li>
  <li><b>Temporary password:</b> {tempPassword}</li>
</ul>
<p>You'll be asked to set a new password on first sign-in. A separate message will
contain the environment connection link.</p>";
        await _email.SendAsync(email, "Your VmManager account", body);
    }

    private static string GeneratePassword()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(24);
        string raw = Convert
            .ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return raw[..Math.Min(20, raw.Length)] + "!9";
    }
}
