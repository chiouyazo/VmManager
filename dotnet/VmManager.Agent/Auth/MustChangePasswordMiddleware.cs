namespace VmManager.Agent.Auth;

public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            bool mustChange = context.User.Claims.Any(c =>
                c.Type == "MustChangePassword" && c.Value == "true"
            );

            if (mustChange)
            {
                string path = context.Request.Path.Value ?? "";
                bool isAllowed =
                    path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/auth", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/health", StringComparison.OrdinalIgnoreCase);

                if (!isAllowed)
                {
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        "{\"error\":\"Password change required. Please change your password before continuing.\"}"
                    );
                    return;
                }
            }
        }

        await _next(context);
    }
}
