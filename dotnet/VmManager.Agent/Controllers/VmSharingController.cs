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
    private readonly ILogger<VmSharingController> _logger;

    public VmSharingController(
        VmSharingService sharingService,
        VmOwnershipService ownershipService,
        AuthorizationService authorizationService,
        UserService userService,
        RdpSessionStore sessionStore,
        ILogger<VmSharingController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(sharingService);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(logger);
        _sharingService = sharingService;
        _ownershipService = ownershipService;
        _authorizationService = authorizationService;
        _userService = userService;
        _sessionStore = sessionStore;
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

        return NoContent();
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

        _ownershipService.TransferOwnership(vmName, request.NewOwnerUsername);
        _sharingService.UnshareVm(vmName, request.NewOwnerUsername);
        return NoContent();
    }
}
