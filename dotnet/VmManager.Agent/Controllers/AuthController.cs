using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly UserService _userService;

    public AuthController(UserService userService)
    {
        ArgumentNullException.ThrowIfNull(userService);
        _userService = userService;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthenticatedUser), 200)]
    public IActionResult GetCurrentUser()
    {
        string username = User.Identity?.Name ?? "";
        bool isAdmin = User.IsInRole("Admin");

        HashSet<string> permissions = User
            .Claims.Where(c => c.Type == Permission.PermissionClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        bool mustChangePassword = User.Claims.Any(c =>
            c.Type == "MustChangePassword" && c.Value == "true"
        );

        return Ok(
            new AuthenticatedUser
            {
                Username = username,
                IsAdmin = isAdmin,
                Permissions = permissions,
                MustChangePassword = mustChangePassword,
            }
        );
    }

    [HttpPut("password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult ChangeOwnPassword([FromBody] ChangePasswordRequest request)
    {
        string username = User.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Password cannot be empty" });

        _userService.ChangePassword(username, request.NewPassword);
        return NoContent();
    }
}
