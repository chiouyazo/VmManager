using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permission.UsersManage)]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly VmSharingService _sharingService;
    private readonly QuotaService _quotaService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        UserService userService,
        VmSharingService sharingService,
        QuotaService quotaService,
        ILogger<UsersController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(sharingService);
        ArgumentNullException.ThrowIfNull(quotaService);
        ArgumentNullException.ThrowIfNull(logger);
        _userService = userService;
        _sharingService = sharingService;
        _quotaService = quotaService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<AuthenticatedUser>), 200)]
    public IActionResult ListUsers()
    {
        List<UserAccount> users = _userService.GetAll();
        List<AuthenticatedUser> result = users
            .Select(u => new AuthenticatedUser
            {
                Username = u.Username,
                IsAdmin = u.IsAdmin,
                Permissions = u.IsAdmin ? Permission.All : u.Permissions,
                Email = u.Email,
                MaxVms = u.MaxVms,
            })
            .ToList();

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AuthenticatedUser), 201)]
    [ProducesResponseType(400)]
    public IActionResult CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { error = "Username is required" });
        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Password is required" });

        try
        {
            HashSet<string> validPermissions = request
                .Permissions.Where(p => Permission.All.Contains(p))
                .ToHashSet();

            UserAccount account = _userService.CreateUser(
                request.Username,
                request.Password,
                validPermissions,
                request.IsAdmin
            );

            return Created(
                "",
                new AuthenticatedUser
                {
                    Username = account.Username,
                    IsAdmin = account.IsAdmin,
                    Permissions = account.IsAdmin ? Permission.All : account.Permissions,
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{username}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult DeleteUser(string username)
    {
        string currentUser = User.Identity?.Name ?? "";
        if (string.Equals(username, currentUser, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Cannot delete your own account" });

        try
        {
            _userService.DeleteUser(username);
            _sharingService.RemoveAllSharesForUser(username);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{username}/permissions")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult UpdatePermissions(
        string username,
        [FromBody] UpdatePermissionsRequest request
    )
    {
        try
        {
            HashSet<string> validPermissions = request
                .Permissions.Where(p => Permission.All.Contains(p))
                .ToHashSet();

            _userService.UpdatePermissions(username, validPermissions);
            if (request.IsAdmin.HasValue)
                _userService.UpdateAdmin(username, request.IsAdmin.Value);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{username}/rename")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult RenameUser(string username, [FromBody] RenameUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewUsername))
            return BadRequest(new { error = "New username is required" });

        UserAccount? user = _userService.GetByUsername(username);
        if (user != null && !user.IsAdmin && !EmailValidator.IsValid(request.NewUsername))
            return BadRequest(new { error = "Username must be a valid email address" });

        try
        {
            _userService.RenameUser(username, request.NewUsername);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{username}/password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult ResetPassword(string username, [FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Password cannot be empty" });

        try
        {
            _userService.ChangePassword(username, request.NewPassword);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{username}/email")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult UpdateEmail(string username, [FromBody] UpdateEmailRequest request)
    {
        try
        {
            _userService.UpdateEmail(username, request.Email);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{username}/quota")]
    [ProducesResponseType(typeof(QuotaUsage), 200)]
    public async Task<IActionResult> GetUserQuota(string username)
    {
        QuotaUsage usage = await _quotaService.GetUsageAsync(username);
        return Ok(usage);
    }

    [HttpPut("{username}/quota")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SetUserQuota(
        string username,
        [FromBody] SetQuotaRequest request
    )
    {
        UserAccount? user = _userService.GetByUsername(username);
        if (user == null)
            return BadRequest(new { error = "User not found" });

        int oldMax = user.MaxVms;
        _userService.UpdateMaxVms(username, request.MaxVms);

        if (oldMax != request.MaxVms)
            await _quotaService.NotifyQuotaChangedAsync(username, oldMax, request.MaxVms);

        return NoContent();
    }
}
