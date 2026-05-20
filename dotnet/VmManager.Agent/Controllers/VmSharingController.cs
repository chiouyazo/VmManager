using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/vms/{vmName}/sharing")]
[Authorize]
public class VmSharingController : ControllerBase
{
    private readonly VmSharingService _sharingService;
    private readonly VmOwnershipService _ownershipService;
    private readonly AuthorizationService _authorizationService;
    private readonly UserService _userService;
    private readonly RdpSessionStore _sessionStore;
    private readonly EmailService _emailService;
    private readonly ILogger<VmSharingController> _logger;

    public VmSharingController(
        VmSharingService sharingService,
        VmOwnershipService ownershipService,
        AuthorizationService authorizationService,
        UserService userService,
        RdpSessionStore sessionStore,
        EmailService emailService,
        ILogger<VmSharingController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(sharingService);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(emailService);
        ArgumentNullException.ThrowIfNull(logger);
        _sharingService = sharingService;
        _ownershipService = ownershipService;
        _authorizationService = authorizationService;
        _userService = userService;
        _sessionStore = sessionStore;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<VmShareEntry>), 200)]
    [ProducesResponseType(403)]
    public IActionResult GetShares(string vmName)
    {
        if (!_authorizationService.IsOwnerOrAdmin(User, vmName))
            return Forbid();

        List<VmShareEntry> shares = _sharingService.GetSharesForVm(vmName);
        return Ok(shares);
    }

    [HttpPost]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public IActionResult ShareVm(string vmName, [FromBody] ShareVmRequest request)
    {
        if (!_authorizationService.IsOwnerOrAdmin(User, vmName))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { error = "Username is required" });

        if (_userService.GetByUsername(request.Username) == null)
            return BadRequest(new { error = "User not found: " + request.Username });

        string owner = _ownershipService.GetOwner(vmName);
        if (string.Equals(owner, request.Username, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Cannot share a VM with its owner" });

        HashSet<string> validPermissions = request
            .Permissions.Where(p => Permission.Shareable.Contains(p))
            .ToHashSet();

        _sharingService.ShareVm(vmName, owner, request.Username, validPermissions);

        if (!validPermissions.Contains(Permission.RdpConnect))
            _sessionStore.DisconnectSessionsForUser(vmName, request.Username);

        _ = SendShareNotificationAsync(vmName, owner, request.Username, validPermissions);

        return NoContent();
    }

    private async Task SendShareNotificationAsync(
        string vmName,
        string owner,
        string recipient,
        HashSet<string> permissions
    )
    {
        try
        {
            string? email = GetUserEmail(recipient);
            if (string.IsNullOrWhiteSpace(email))
                return;

            string permList = string.Join(", ", permissions.Select(p => p.Split('.').Last()));

            string body =
                $@"
<h2>VM Shared With You</h2>
<p><b>{owner}</b> has shared the VM <b>{vmName}</b> with you.</p>
<p>Granted permissions: {permList}</p>
<p>You can access this VM from the VmManager client.</p>";

            await _emailService.SendAsync(email, "VM Shared: " + vmName, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send share notification for {VmName}", vmName);
        }
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

    [HttpDelete("{username}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    public IActionResult UnshareVm(string vmName, string username)
    {
        if (!_authorizationService.IsOwnerOrAdmin(User, vmName))
            return Forbid();

        _sharingService.UnshareVm(vmName, username);
        _sessionStore.DisconnectSessionsForUser(vmName, username);
        return NoContent();
    }

    [HttpPut("transfer")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public IActionResult TransferOwnership(
        string vmName,
        [FromBody] TransferOwnershipRequest request
    )
    {
        if (!_authorizationService.IsOwnerOrAdmin(User, vmName))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.NewOwnerUsername))
            return BadRequest(new { error = "New owner username is required" });

        if (_userService.GetByUsername(request.NewOwnerUsername) == null)
            return BadRequest(new { error = "User not found: " + request.NewOwnerUsername });

        string previousOwner = _ownershipService.GetOwner(vmName);
        _ownershipService.TransferOwnership(vmName, request.NewOwnerUsername);
        _sharingService.UnshareVm(vmName, request.NewOwnerUsername);

        _ = SendTransferEmailAsync(vmName, previousOwner, request.NewOwnerUsername);

        return NoContent();
    }

    private async Task SendTransferEmailAsync(string vmName, string fromUser, string toUser)
    {
        try
        {
            string? newOwnerEmail = GetUserEmail(toUser);
            if (!string.IsNullOrWhiteSpace(newOwnerEmail))
            {
                string body =
                    $@"
<h2>VM Ownership Transferred</h2>
<p>The VM <b>{vmName}</b> has been transferred to you by <b>{fromUser}</b>.</p>
<p>You are now the owner of this VM.</p>";
                await _emailService.SendAsync(newOwnerEmail, "VM Transferred: " + vmName, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send transfer email for {VmName}", vmName);
        }
    }
}
