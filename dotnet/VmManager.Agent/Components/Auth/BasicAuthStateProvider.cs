using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Components.Auth;

public sealed class BasicAuthStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState = new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity())
    );

    private readonly UserService _userService;
    private ClaimsPrincipal _currentUser = new ClaimsPrincipal(new ClaimsIdentity());

    public BasicAuthStateProvider(UserService userService)
    {
        ArgumentNullException.ThrowIfNull(userService);
        _userService = userService;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(_currentUser));
    }

    public bool TryLogin(string username, string password)
    {
        if (!_userService.ValidateCredentials(username, password))
            return false;

        UserAccount? account = _userService.GetByUsername(username);
        if (account == null)
            return false;

        _currentUser = BuildPrincipal(account);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return true;
    }

    public void RestoreSession(string username, string password)
    {
        if (!_userService.ValidateCredentials(username, password))
            return;

        UserAccount? account = _userService.GetByUsername(username);
        if (account == null)
            return;

        _currentUser = BuildPrincipal(account);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void Logout()
    {
        _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public string CurrentUsername => _currentUser.Identity?.Name ?? "";

    private static ClaimsPrincipal BuildPrincipal(UserAccount account)
    {
        List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, account.Username) };

        if (account.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        foreach (string permission in account.Permissions)
            claims.Add(new Claim(Permission.PermissionClaimType, permission));

        ClaimsIdentity identity = new ClaimsIdentity(claims, "BasicAuth");
        return new ClaimsPrincipal(identity);
    }
}
