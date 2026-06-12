using System.Security.Principal;
using Serilog;
using Serilog.Events;
using VmManager.Agent;

string logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "VmManager.Agent",
    "Logs",
    "agent-.log"
);

LoggerConfiguration logConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

if (Environment.GetEnvironmentVariable("VMMANAGER_DEBUG_RDP") == "true")
{
    logConfig.MinimumLevel.Override("VmManager.Agent.Services.Rdp", LogEventLevel.Debug);
}

Log.Logger = logConfig
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

if (OperatingSystem.IsWindows())
{
    bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(
        WindowsBuiltInRole.Administrator
    );
    if (!isAdmin)
        Log.Warning("Not running as administrator. Hyper-V WMI queries will likely fail");
}
else if (OperatingSystem.IsLinux())
{
    if (Environment.UserName != "root")
        Log.Warning("Not running as root. libvirt operations will likely fail");
}

Log.Information("VmManager Agent starting");
Log.Information("  API:       http://localhost:18275");
Log.Information("  Swagger:   http://localhost:18275/swagger");
Log.Information("  RDP Proxy: port 13389 (token-authenticated)");

await AgentHost.RunAsync(args);
