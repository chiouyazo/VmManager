using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using VmManager.Agent.Services;

namespace VmManager.Agent.Auth;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly UserService _userService;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UserService userService
    )
        : base(options, logger, encoder)
    {
        ArgumentNullException.ThrowIfNull(userService);
        _userService = userService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (
            authHeader == null
            || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
        )
            return Task.FromResult(AuthenticateResult.NoResult());

        try
        {
            string decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(authHeader["Basic ".Length..])
            );
            int colon = decoded.IndexOf(':');
            if (colon <= 0)
                return Task.FromResult(AuthenticateResult.Fail("Invalid credentials format"));

            string username = decoded[..colon];
            string password = decoded[(colon + 1)..];

            if (!_userService.ValidateCredentials(username, password))
                return Task.FromResult(AuthenticateResult.Fail("Invalid username or password"));

            UserAccount? account = _userService.GetByUsername(username);
            if (account == null)
                return Task.FromResult(AuthenticateResult.Fail("User not found"));

            List<Claim> claims = [new Claim(ClaimTypes.Name, account.Username)];

            if (account.MustChangePassword)
                claims.Add(new Claim("MustChangePassword", "true"));

            if (account.IsAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                foreach (string permission in Permission.All)
                    claims.Add(new Claim(Permission.PermissionClaimType, permission));
            }
            else
            {
                foreach (string permission in account.Permissions)
                    claims.Add(new Claim(Permission.PermissionClaimType, permission));
            }

            ClaimsIdentity identity = new(claims, Scheme.Name);
            ClaimsPrincipal principal = new(identity);
            AuthenticationTicket ticket = new(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid authorization header"));
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Basic realm=\"VmManager\"";
        return Task.CompletedTask;
    }
}
