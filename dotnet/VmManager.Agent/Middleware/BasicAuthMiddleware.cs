using System.Security.Cryptography;
using System.Text;
using VmManager.Catalog.Shared;

namespace VmManager.Agent.Middleware;

public class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _credentialsPath;
    private readonly ILogger<BasicAuthMiddleware> _logger;
    private string? _expectedPassword;

    public BasicAuthMiddleware(
        RequestDelegate next,
        IAppPaths paths,
        ILogger<BasicAuthMiddleware> logger
    )
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _credentialsPath = Path.Combine(paths.AppDataDir, "api-credentials.txt");
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        SettingsService settings = context.RequestServices.GetRequiredService<SettingsService>();
        if (!settings.Load().SecureApi)
        {
            await _next(context);
            return;
        }

        string path = context.Request.Path.Value ?? "";
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        EnsurePassword();

        string? authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (
            authHeader != null
            && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
        )
        {
            try
            {
                string decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authHeader["Basic ".Length..])
                );
                int colon = decoded.IndexOf(':');
                if (colon > 0)
                {
                    string user = decoded[..colon];
                    string pass = decoded[(colon + 1)..];
                    if (user == "admin" && pass == _expectedPassword)
                    {
                        context.Items["VmManager.User"] = user;
                        await _next(context);
                        return;
                    }
                }
            }
            catch { }
        }

        context.Response.StatusCode = 401;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"VmManager\"";
        await context.Response.WriteAsync("Unauthorized");
    }

    private void EnsurePassword()
    {
        if (_expectedPassword != null)
            return;

        if (File.Exists(_credentialsPath))
        {
            string content = File.ReadAllText(_credentialsPath).Trim();
            int colon = content.IndexOf(':');
            _expectedPassword = colon > 0 ? content[(colon + 1)..] : content;
            return;
        }

        byte[] bytes = RandomNumberGenerator.GetBytes(24);
        _expectedPassword = Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "")[..32];
        Directory.CreateDirectory(Path.GetDirectoryName(_credentialsPath)!);
        File.WriteAllText(_credentialsPath, $"admin:{_expectedPassword}");
        _logger.LogInformation("API credentials generated. File: {Path}", _credentialsPath);
    }
}
