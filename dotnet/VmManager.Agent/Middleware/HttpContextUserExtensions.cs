namespace VmManager.Agent.Middleware;

public static class HttpContextUserExtensions
{
    public static string? GetVmUser(this HttpContext ctx) =>
        ctx.Items.TryGetValue("VmManager.User", out object? val) ? val as string : null;
}
