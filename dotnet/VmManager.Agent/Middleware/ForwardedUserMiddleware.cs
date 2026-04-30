namespace VmManager.Agent.Middleware;

public class ForwardedUserMiddleware
{
    private readonly RequestDelegate _next;

    public ForwardedUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        string? user = context.Request.Headers["X-Forwarded-User"].FirstOrDefault();
        if (string.IsNullOrEmpty(user))
        {
            string? authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (
                authHeader != null
                && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
            )
            {
                try
                {
                    string decoded = System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(authHeader["Basic ".Length..])
                    );
                    int colon = decoded.IndexOf(':');
                    if (colon > 0)
                        user = decoded[..colon];
                }
                catch { }
            }
        }

        if (!string.IsNullOrEmpty(user))
            context.Items["VmManager.User"] = user;

        return _next(context);
    }
}
